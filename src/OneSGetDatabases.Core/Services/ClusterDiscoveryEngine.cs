using System.Collections.Concurrent;
using System.Management;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OneSGetDatabases.Core.Helpers;
using OneSGetDatabases.Core.Interfaces;
using OneSGetDatabases.Core.Models;

namespace OneSGetDatabases.Core.Services;

public record DiscoveredRagentService(string Name, int Port, string Path, string Version, string ClusterDir);
public record DiscoveredRasService(string Name, int RasPort, int TargetPort, string Path);

public partial class ClusterDiscoveryEngine : IClusterDiscoveryEngine
{
    private const string DiscoveredCacheFileName = "discovered_clusters_cache.json";

    private readonly ClusterDiscoveryConfig _config;
    private readonly ILogger<ClusterDiscoveryEngine> _logger;
    private readonly ConcurrentDictionary<string, List<ClusterConfig>> _memoryCache = new(StringComparer.OrdinalIgnoreCase);

    public ClusterDiscoveryEngine(
        IOptions<ClusterDiscoveryConfig> config,
        ILogger<ClusterDiscoveryEngine> logger)
    {
        _config = config.Value ?? new ClusterDiscoveryConfig();
        _logger = logger;

        // Load cached clusters from disk if available
        var diskCache = PersistentCacheHelper.LoadFromDisk<Dictionary<string, List<ClusterConfig>>>(DiscoveredCacheFileName);
        if (diskCache != null)
        {
            foreach (var (host, clusters) in diskCache)
            {
                _memoryCache[host] = clusters;
            }
        }
    }

    [GeneratedRegex(@"-port\s+(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex AgentPortRegex();

    [GeneratedRegex(@"-d\s+""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex QuotedClusterDirRegex();

    [GeneratedRegex(@"-d\s+(\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex UnquotedClusterDirRegex();

    [GeneratedRegex(@"\\(\d+\.\d+\.\d+\.\d+)\\", RegexOptions.IgnoreCase)]
    private static partial Regex PlatformVersionRegex();

    [GeneratedRegex(@"(?:--port|-port|/port)[:=\s]+(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex RasPortRegex();

    [GeneratedRegex(@"(?:localhost|127\.0\.0\.1|\b[a-zA-Z][\w\.-]*):(\d{4,5})", RegexOptions.IgnoreCase)]
    private static partial Regex RasTargetHostPortRegex();

    [GeneratedRegex(@"(?:^|\s)(\d{4,5})(?:\s|$)", RegexOptions.IgnoreCase)]
    private static partial Regex RasTrailingPortRegex();

    public static DiscoveredRagentService ParseRagentCommandLine(string serviceName, string commandLine)
    {
        int port = 1540;
        var portMatch = AgentPortRegex().Match(commandLine);
        if (portMatch.Success && int.TryParse(portMatch.Groups[1].Value, out int p))
        {
            port = p;
        }

        string version = "Unknown";
        var verMatch = PlatformVersionRegex().Match(commandLine);
        if (verMatch.Success)
        {
            version = verMatch.Groups[1].Value;
        }

        string clusterDir = "Unknown";
        var qMatch = QuotedClusterDirRegex().Match(commandLine);
        if (qMatch.Success)
        {
            clusterDir = qMatch.Groups[1].Value;
        }
        else
        {
            var uMatch = UnquotedClusterDirRegex().Match(commandLine);
            if (uMatch.Success)
            {
                clusterDir = uMatch.Groups[1].Value;
            }
        }

        return new DiscoveredRagentService(serviceName, port, commandLine, version, clusterDir);
    }

    public static DiscoveredRasService ParseRasCommandLine(string serviceName, string commandLine)
    {
        int rasPort = 1545;
        var rasPortMatch = RasPortRegex().Match(commandLine);
        if (rasPortMatch.Success && int.TryParse(rasPortMatch.Groups[1].Value, out int rp))
        {
            rasPort = rp;
        }

        int targetPort = 1540;
        var hostPortMatch = RasTargetHostPortRegex().Match(commandLine);
        if (hostPortMatch.Success && int.TryParse(hostPortMatch.Groups[1].Value, out int hp))
        {
            targetPort = hp;
        }
        else
        {
            // Try matching standalone trailing port number e.g. "ras.exe cluster 2540"
            string cleaned = RasPortRegex().Replace(commandLine, " ");
            cleaned = Regex.Replace(cleaned, @"--(?:service|range\s+\S+)", " ", RegexOptions.IgnoreCase);
            var trailingMatch = RasTrailingPortRegex().Match(cleaned);
            if (trailingMatch.Success && int.TryParse(trailingMatch.Groups[1].Value, out int tp))
            {
                targetPort = tp;
            }
        }

        return new DiscoveredRasService(serviceName, rasPort, targetPort, commandLine);
    }

    public static IReadOnlyList<ClusterConfig> MatchClusters(
        ServerNodeConfig node,
        string defaultClusterUser,
        string defaultClusterPassword,
        IEnumerable<DiscoveredRagentService> agents,
        IEnumerable<DiscoveredRasService> rasServices)
    {
        var clusters = new List<ClusterConfig>();
        var user = !string.IsNullOrWhiteSpace(node.ClusterUser) ? node.ClusterUser : defaultClusterUser;
        var pwd = !string.IsNullOrWhiteSpace(node.ClusterPassword) ? node.ClusterPassword : defaultClusterPassword;
        var rasList = rasServices.ToList();

        foreach (var agent in agents)
        {
            var matchingRas = rasList.FirstOrDefault(r => r.TargetPort == agent.Port);
            int rasPort = matchingRas != null ? matchingRas.RasPort : 0;

            clusters.Add(new ClusterConfig
            {
                Server = $"{node.Host}:{agent.Port}",
                RasPort = rasPort,
                Environment = node.Environment,
                ClusterUser = user,
                ClusterPassword = pwd,
                Platform = agent.Version
            });
        }

        return clusters;
    }

    private readonly ConcurrentQueue<ClusterDiscoveryLogEntry> _engineLogs = new();

    public IReadOnlyList<ClusterDiscoveryLogEntry> GetLastDiscoveryLogs() => _engineLogs.ToList();

    public async ValueTask<IReadOnlyList<ClusterConfig>> DiscoverHostClustersAsync(
        ServerNodeConfig node, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(node.Host))
            return [];

        // Pre-check connectivity to WinRM (5985) or RPC (135) with 1s timeout
        bool canConnect = await TestPortAsync(node.Host, 5985, 1000) || await TestPortAsync(node.Host, 135, 1000);
        if (!canConnect)
        {
            _logger.LogWarning("Host {Host} is unreachable via WinRM (5985) and RPC (135). Checking cache...", node.Host);
            _engineLogs.Enqueue(new ClusterDiscoveryLogEntry
            {
                Timestamp = DateTime.UtcNow,
                Server = node.Host,
                Host = node.Host,
                Environment = node.Environment,
                Stage = "WinRM/RPC",
                Level = "Error",
                Message = $"Хост {node.Host} недоступен по портам WinRM (5985) и RPC (135). Проверьте сетевую связность или правила брандмауэра."
            });

            if (_memoryCache.TryGetValue(node.Host, out var cachedClusters))
            {
                return cachedClusters;
            }
            return [];
        }

        try
        {
            var hostClusters = await Task.Run<IReadOnlyList<ClusterConfig>>(() =>
            {
                var options = new ConnectionOptions
                {
                    Timeout = TimeSpan.FromSeconds(5),
                    EnablePrivileges = true,
                    Impersonation = ImpersonationLevel.Impersonate,
                    Authentication = AuthenticationLevel.PacketPrivacy
                };

                ManagementScope scope;
                try
                {
                    scope = new ManagementScope($@"\\{node.Host}\root\cimv2", options);
                    scope.Connect();
                }
                catch (UnauthorizedAccessException) when (!node.Host.Contains('.'))
                {
                    scope = new ManagementScope($@"\\{node.Host}.example.corp\root\cimv2", options);
                    scope.Connect();
                }

                var query = new SelectQuery("Win32_Service", "PathName LIKE '%ragent.exe%' OR PathName LIKE '%ras.exe%'");
                using var searcher = new ManagementObjectSearcher(scope, query);
                using var results = searcher.Get();

                var agents = new List<DiscoveredRagentService>();
                var rasServices = new List<DiscoveredRasService>();

                foreach (ManagementObject svc in results)
                {
                    string pathName = svc["PathName"]?.ToString() ?? "";
                    string serviceName = svc["Name"]?.ToString() ?? "";

                    if (pathName.Contains("ragent.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        agents.Add(ParseRagentCommandLine(serviceName, pathName));
                    }
                    else if (pathName.Contains("ras.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        rasServices.Add(ParseRasCommandLine(serviceName, pathName));
                    }
                }

                return MatchClusters(node, _config.DefaultClusterUser, _config.DefaultClusterPassword, agents, rasServices);
            }, cancellationToken);

            if (hostClusters.Count > 0)
            {
                _memoryCache[node.Host] = hostClusters.ToList();
                _logger.LogInformation("Host {Host} auto-discovery: found {Count} 1C cluster(s) with matching RAS", 
                    node.Host, hostClusters.Count);
            }
            else
            {
                _logger.LogWarning("Host {Host} auto-discovery: no active 1C Agent services found", node.Host);
                _engineLogs.Enqueue(new ClusterDiscoveryLogEntry
                {
                    Timestamp = DateTime.UtcNow,
                    Server = node.Host,
                    Host = node.Host,
                    Environment = node.Environment,
                    Stage = "Службы 1С",
                    Level = "Warning",
                    Message = $"На сервере {node.Host} не найдено активных служб ragent.exe."
                });
            }

            return hostClusters;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WMI query failed for {Host}: {Message}. Attempting TCP port-probing fallback...", node.Host, ex.Message);

            // TCP Port-probing fallback for 1C Agent and RAS (cures WMI Access Denied)
            var probedClusters = await TryProbeHostClustersViaTcpAsync(node, cancellationToken);
            if (probedClusters.Count > 0)
            {
                _memoryCache[node.Host] = probedClusters;
                var primaryCluster = probedClusters[0];
                int agentPort = primaryCluster.ServerPort;
                int rasPort = primaryCluster.RasPort;
                if (rasPort > 0)
                {
                    _logger.LogInformation("Host {Host}: TCP fallback successfully discovered active 1C cluster (Port {AgentPort}, RAS {RasPort})", node.Host, agentPort, rasPort);

                    _engineLogs.Enqueue(new ClusterDiscoveryLogEntry
                    {
                        Timestamp = DateTime.UtcNow,
                        Server = node.Host,
                        Host = node.Host,
                        Port = agentPort,
                        Environment = node.Environment,
                        Stage = "TCP Фоллбэк",
                        Level = "Warning",
                        Message = $"WMI недоступен ({ex.Message}). Применён TCP-опрос: обнаружен кластер 1С (порт {agentPort}, RAS: {rasPort}).",
                        Details = $"На сервере {node.Host} WMI вернул {ex.GetType().Name} (0x80070005). Порт агента {agentPort} и порт RAS {rasPort} открыты по сети."
                    });
                }
                else
                {
                    _logger.LogWarning("Host {Host}: 1C Agent is active on port {AgentPort}, but RAS service is unreachable", node.Host, agentPort);

                    _engineLogs.Enqueue(new ClusterDiscoveryLogEntry
                    {
                        Timestamp = DateTime.UtcNow,
                        Server = node.Host,
                        Host = node.Host,
                        Port = agentPort,
                        Environment = node.Environment,
                        Stage = "Служба RAS",
                        Level = "Warning",
                        Message = $"WMI недоступен ({ex.Message}). Обнаружен кластер 1С (порт {agentPort}), но служба RAS не найдена.",
                        Details = $"На сервере {node.Host} обнаружен открытый порт ragent {agentPort}, однако служба удаленного администрирования ras.exe не обнаружена по сети. Для сбора информационных баз запустите службу ras.exe на сервере {node.Host}."
                    });
                }

                return probedClusters;
            }

            _engineLogs.Enqueue(new ClusterDiscoveryLogEntry
            {
                Timestamp = DateTime.UtcNow,
                Server = node.Host,
                Host = node.Host,
                Environment = node.Environment,
                Stage = "WMI/CIM",
                Level = "Error",
                Message = $"Ошибка WMI-запроса к {node.Host}: {ex.Message}",
                Details = SanitizeErrorDetails(ex)
            });

            if (_memoryCache.TryGetValue(node.Host, out var cachedClusters))
            {
                return cachedClusters;
            }
            return [];
        }
    }

    public async ValueTask<IReadOnlyList<ClusterConfig>> DiscoverAllClustersAsync(CancellationToken cancellationToken = default)
    {
        _engineLogs.Clear();
        if (!_config.Enabled || _config.Servers.Count == 0)
        {
            _logger.LogDebug("ClusterDiscovery is disabled or has no servers configured.");
            return [];
        }

        _logger.LogInformation("Starting dynamic cluster auto-discovery across {Count} host(s)...", _config.Servers.Count);

        var discovered = new ConcurrentBag<ClusterConfig>();

        await Parallel.ForEachAsync(_config.Servers, new ParallelOptions
        {
            MaxDegreeOfParallelism = 8,
            CancellationToken = cancellationToken
        }, async (node, token) =>
        {
            try
            {
                var clusters = await DiscoverHostClustersAsync(node, token);
                foreach (var c in clusters)
                {
                    discovered.Add(c);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error discovering clusters on host {Host}: {Msg}", node.Host, ex.Message);
            }
        });

        var resultList = discovered.ToList();

        // Save fresh snapshot to disk cache
        try
        {
            var diskSnapshot = _memoryCache.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
            PersistentCacheHelper.SaveToDisk(DiscoveredCacheFileName, diskSnapshot);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to persist discovered clusters cache to disk");
        }

        _logger.LogInformation("Dynamic cluster auto-discovery finished. Total clusters discovered: {Count}", resultList.Count);
        return resultList;
    }

    private static async Task<bool> TestPortAsync(string host, int port, int timeoutMs)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var delayTask = Task.Delay(timeoutMs);

            if (await Task.WhenAny(connectTask, delayTask) == connectTask)
            {
                return client.Connected;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static readonly int[] CandidateAgentPorts = [1540, 2540, 3040, 4540, 6040];

    private static int[] GetCandidateRasPortsForAgent(int agentPort) => agentPort switch
    {
        1540 => [1545],
        2540 => [2545],
        3040 => [9004, 3045],
        4540 => [9007, 4545],
        6040 => [9010, 6045],
        _ => [agentPort + 5]
    };

    private async Task<List<ClusterConfig>> TryProbeHostClustersViaTcpAsync(
        ServerNodeConfig node, CancellationToken cancellationToken)
    {
        var clusters = new List<ClusterConfig>();
        var user = !string.IsNullOrWhiteSpace(node.ClusterUser) ? node.ClusterUser : _config.DefaultClusterUser;
        var pwd = !string.IsNullOrWhiteSpace(node.ClusterPassword) ? node.ClusterPassword : _config.DefaultClusterPassword;

        // Probe for active 1C Agent ports
        var openAgentPorts = new List<int>();
        foreach (int ap in CandidateAgentPorts)
        {
            if (cancellationToken.IsCancellationRequested) break;
            if (await TestPortAsync(node.Host, ap, 800))
            {
                openAgentPorts.Add(ap);
            }
        }

        if (openAgentPorts.Count == 0)
        {
            return [];
        }

        foreach (int ap in openAgentPorts)
        {
            var candidateRasPorts = GetCandidateRasPortsForAgent(ap);
            int discoveredRasPort = 0;
            foreach (int rp in candidateRasPorts)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (await TestPortAsync(node.Host, rp, 600))
                {
                    discoveredRasPort = rp;
                    break;
                }
            }

            clusters.Add(new ClusterConfig
            {
                Server = $"{node.Host}:{ap}",
                RasPort = discoveredRasPort,
                Environment = node.Environment,
                ClusterUser = user,
                ClusterPassword = pwd,
                Platform = "8.3"
            });
        }

        return clusters;
    }

    private static string SanitizeErrorDetails(Exception ex)
    {
        if (ex is UnauthorizedAccessException || ex.HResult == unchecked((int)0x80070005))
        {
            return "0x80070005 (E_ACCESSDENIED): Отказано в доступе к WMI. Проверьте права учетной записи службы в DCOM/WMI на целевом сервере.";
        }

        var msg = $"{ex.GetType().Name}: {ex.Message}";
        if (ex.InnerException != null)
        {
            msg += $" -> {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
        }
        return msg;
    }
}
