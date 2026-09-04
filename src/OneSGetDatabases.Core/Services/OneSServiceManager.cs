using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OneSGetDatabases.Core.Interfaces;
using OneSGetDatabases.Core.Models;

namespace OneSGetDatabases.Core.Services;

public partial class OneSServiceManager : IOneSServiceManager
{
    private readonly ClusterDiscoveryConfig _discoveryConfig;
    private readonly IAuditLogService _auditLog;
    private readonly ILogger<OneSServiceManager> _logger;

    public OneSServiceManager(
        IOptions<ClusterDiscoveryConfig> discoveryConfig,
        IAuditLogService auditLog,
        ILogger<OneSServiceManager> logger)
    {
        _discoveryConfig = discoveryConfig.Value ?? new ClusterDiscoveryConfig();
        _auditLog = auditLog;
        _logger = logger;
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

    private IReadOnlyList<OneSServiceInfo>? _cachedServices;
    private DateTime _lastCacheTime = DateTime.MinValue;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public async ValueTask<IReadOnlyList<OneSServiceInfo>> GetAllServicesStatusAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _cachedServices != null && (DateTime.UtcNow - _lastCacheTime).TotalMinutes < 35)
        {
            return _cachedServices;
        }

        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _cachedServices != null && (DateTime.UtcNow - _lastCacheTime).TotalMinutes < 35)
            {
                return _cachedServices;
            }

            if (_discoveryConfig.Servers.Count == 0)
            {
                return [];
            }

            var results = new ConcurrentBag<OneSServiceInfo>();

            await Parallel.ForEachAsync(_discoveryConfig.Servers, new ParallelOptions
            {
                MaxDegreeOfParallelism = 8,
                CancellationToken = cancellationToken
            }, async (node, token) =>
            {
                try
                {
                    var services = await QueryHostServicesAsync(node, token);
                    foreach (var s in services)
                    {
                        results.Add(s);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get services from {Host}: {Msg}", node.Host, ex.Message);
                }
            });

            var sorted = results
                .OrderBy(s => s.Environment)
                .ThenBy(s => s.Host)
                .ThenBy(s => s.ClusterPort)
                .ToList();

            _cachedServices = sorted;
            _lastCacheTime = DateTime.UtcNow;
            return sorted;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private async Task<List<OneSServiceInfo>> QueryHostServicesAsync(
        ServerNodeConfig node, CancellationToken cancellationToken)
    {
        bool canConnect = await TestPortAsync(node.Host, 5985, 1000) || await TestPortAsync(node.Host, 135, 1000);
        if (!canConnect)
        {
            _logger.LogWarning("Host {Host} unreachable via RPC/WinRM for service query", node.Host);
            return [];
        }

        return await Task.Run(() =>
        {
            var list = new List<OneSServiceInfo>();
            try
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
                    string fqdn = node.Host;
                    try
                    {
                        var entry = System.Net.Dns.GetHostEntry(node.Host);
                        if (!string.IsNullOrEmpty(entry.HostName) && entry.HostName.Contains('.'))
                        {
                            fqdn = entry.HostName;
                        }
                    }
                    catch
                    {
                        // Ignore DNS lookup failure
                    }

                    if (!string.Equals(fqdn, node.Host, StringComparison.OrdinalIgnoreCase))
                    {
                        scope = new ManagementScope($@"\\{fqdn}\root\cimv2", options);
                        scope.Connect();
                    }
                    else
                    {
                        throw;
                    }
                }

                var query = new SelectQuery("Win32_Service", "PathName LIKE '%ragent.exe%' OR PathName LIKE '%ras.exe%'");
                using var searcher = new ManagementObjectSearcher(scope, query);
                using var results = searcher.Get();

                var agents = new List<(string Name, string DisplayName, string State, string StartName, string Path, int Port, string ClusterDir, string Version)>();
                var rasList = new List<(string Name, string DisplayName, string State, int RasPort, int TargetPort)>();

                foreach (ManagementObject svc in results)
                {
                    string name = svc["Name"]?.ToString() ?? "";
                    string displayName = svc["DisplayName"]?.ToString() ?? name;
                    string state = svc["State"]?.ToString() ?? "Unknown";
                    string startName = svc["StartName"]?.ToString() ?? "";
                    string pathName = svc["PathName"]?.ToString() ?? "";

                    if (pathName.Contains("ragent.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        int port = 1540;
                        var pMatch = AgentPortRegex().Match(pathName);
                        if (pMatch.Success && int.TryParse(pMatch.Groups[1].Value, out int p)) port = p;

                        string clusterDir = "";
                        var qMatch = QuotedClusterDirRegex().Match(pathName);
                        if (qMatch.Success) clusterDir = qMatch.Groups[1].Value;
                        else
                        {
                            var uMatch = UnquotedClusterDirRegex().Match(pathName);
                            if (uMatch.Success) clusterDir = uMatch.Groups[1].Value;
                        }

                        string ver = "Unknown";
                        var verMatch = PlatformVersionRegex().Match(pathName);
                        if (verMatch.Success) ver = verMatch.Groups[1].Value;

                        agents.Add((name, displayName, state, startName, pathName, port, clusterDir, ver));
                    }
                    else if (pathName.Contains("ras.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        int rasPort = 1545;
                        var rpMatch = RasPortRegex().Match(pathName);
                        if (rpMatch.Success && int.TryParse(rpMatch.Groups[1].Value, out int rp)) rasPort = rp;

                        int targetPort = 1540;
                        var tpMatch = RasTargetHostPortRegex().Match(pathName);
                        if (tpMatch.Success && int.TryParse(tpMatch.Groups[1].Value, out int tp)) targetPort = tp;
                        else
                        {
                            string cleaned = RasPortRegex().Replace(pathName, " ");
                            cleaned = Regex.Replace(cleaned, @"--(?:service|range\s+\S+)", " ", RegexOptions.IgnoreCase);
                            var trailingMatch = RasTrailingPortRegex().Match(cleaned);
                            if (trailingMatch.Success && int.TryParse(trailingMatch.Groups[1].Value, out int trP)) targetPort = trP;
                        }

                        rasList.Add((name, displayName, state, rasPort, targetPort));
                    }
                }

                foreach (var agent in agents)
                {
                    var matchingRas = rasList.FirstOrDefault(r => r.TargetPort == agent.Port);

                    list.Add(new OneSServiceInfo
                    {
                        Host = node.Host,
                        Environment = node.Environment,
                        ClusterPort = agent.Port,
                        DisplayName = agent.DisplayName,
                        ServiceName = agent.Name,
                        Status = agent.State,
                        StartName = agent.StartName,
                        ClusterDir = agent.ClusterDir,
                        PlatformVersion = agent.Version,
                        RasServiceName = matchingRas.Name ?? "",
                        RasPort = matchingRas.Name != null ? matchingRas.RasPort : 0,
                        RasStatus = matchingRas.State ?? "NotFound",
                        LastCheckedAt = DateTime.UtcNow
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed querying WMI services on {Host}: {Msg}", node.Host, ex.Message);
            }

            return list;
        }, cancellationToken);
    }

    public async Task<ServiceActionResult> ExecuteServiceActionAsync(
        ServiceActionRequest request, string clientIp, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        string actionUpper = request.Action.ToUpperInvariant().Replace('-', '_');
        string currentStatus = "Unknown";
        bool success = false;
        string message = "";
        string displayName = request.ServiceName;

        try
        {
            _logger.LogInformation("Executing action {Action} on {Host}:{Service} from IP {IP}...",
                request.Action, request.Host, request.ServiceName, clientIp);

            var scope = new ManagementScope($@"\\{request.Host}\root\cimv2", new ConnectionOptions
            {
                Timeout = TimeSpan.FromSeconds(15),
                EnablePrivileges = true,
                Impersonation = ImpersonationLevel.Impersonate
            });
            await Task.Run(() => scope.Connect(), cancellationToken);

            // Fetch Russian DisplayName for audit logging
            displayName = await GetServiceDisplayNameAsync(scope, request.ServiceName, cancellationToken);

            switch (request.Action.ToLowerInvariant())
            {
                case "start":
                    await StartServiceWithTimeoutAsync(scope, request.ServiceName, 30, cancellationToken);
                    if (!string.IsNullOrEmpty(request.RasServiceName))
                    {
                        try { await StartServiceWithTimeoutAsync(scope, request.RasServiceName, 15, cancellationToken); } catch { }
                    }
                    currentStatus = "Running";
                    success = true;
                    message = $"Служба {displayName} на сервере {request.Host} успешно запущена.";
                    break;

                case "stop":
                    if (!string.IsNullOrEmpty(request.RasServiceName))
                    {
                        try { await StopServiceWithTimeoutAsync(scope, request.RasServiceName, 15, cancellationToken); } catch { }
                    }
                    await StopServiceWithTimeoutAsync(scope, request.ServiceName, 30, cancellationToken);
                    currentStatus = "Stopped";
                    success = true;
                    message = $"Служба {displayName} на сервере {request.Host} успешно остановлена.";
                    break;

                case "restart":
                    if (!string.IsNullOrEmpty(request.RasServiceName))
                    {
                        try { await StopServiceWithTimeoutAsync(scope, request.RasServiceName, 15, cancellationToken); } catch { }
                    }
                    await StopServiceWithTimeoutAsync(scope, request.ServiceName, 30, cancellationToken);
                    await Task.Delay(2000, cancellationToken);
                    await StartServiceWithTimeoutAsync(scope, request.ServiceName, 30, cancellationToken);
                    if (!string.IsNullOrEmpty(request.RasServiceName))
                    {
                        try { await StartServiceWithTimeoutAsync(scope, request.RasServiceName, 15, cancellationToken); } catch { }
                    }
                    currentStatus = "Running";
                    success = true;
                    message = $"Служба {displayName} на сервере {request.Host} успешно перезапущена.";
                    break;

                case "restart-clean-cache":
                    // 1. Stop RAS and Ragent
                    if (!string.IsNullOrEmpty(request.RasServiceName))
                    {
                        try { await StopServiceWithTimeoutAsync(scope, request.RasServiceName, 15, cancellationToken); } catch { }
                    }
                    await StopServiceWithTimeoutAsync(scope, request.ServiceName, 35, cancellationToken);
                    await Task.Delay(2500, cancellationToken);

                    // 2. Safe cleanup of snccntx* folders
                    int cleanedCount = 0;
                    if (!string.IsNullOrWhiteSpace(request.ClusterDir))
                    {
                        string uncPath = ToUncPath(request.Host, request.ClusterDir);
                        cleanedCount = CleanSnccntxDirectories(uncPath);
                    }

                    // 3. Start Ragent and RAS
                    await StartServiceWithTimeoutAsync(scope, request.ServiceName, 35, cancellationToken);
                    if (!string.IsNullOrEmpty(request.RasServiceName))
                    {
                        try { await StartServiceWithTimeoutAsync(scope, request.RasServiceName, 15, cancellationToken); } catch { }
                    }
                    currentStatus = "Running";
                    success = true;
                    message = $"Служба {displayName} на {request.Host} перезапущена с очисткой серверного кэша (удалено каталогов snccntx*: {cleanedCount}).";
                    break;

                default:
                    throw new ArgumentException($"Неизвестное действие: {request.Action}");
            }
        }
        catch (Exception ex)
        {
            success = false;
            message = $"Ошибка выполнения {request.Action} для {request.ServiceName} на {request.Host}: {ex.Message}";
            _logger.LogError(ex, "Service action failed: {Msg}", message);
        }
        finally
        {
            sw.Stop();

            // Record in audit log
            await _auditLog.LogActionAsync(new AuditLogEntry
            {
                ClientIp = clientIp,
                ClientHostName = AuditLogService.ResolveHostName(clientIp),
                Host = request.Host,
                ClusterPort = request.ClusterPort,
                ServiceName = request.ServiceName,
                DisplayName = displayName,
                Action = actionUpper,
                Status = success ? "SUCCESS" : "FAILED",
                ErrorMessage = success ? "" : message,
                DurationMs = sw.ElapsedMilliseconds
            }, CancellationToken.None);

            if (success)
            {
                _lastCacheTime = DateTime.MinValue;
            }
        }

        return new ServiceActionResult
        {
            Success = success,
            Message = message,
            DurationMs = sw.ElapsedMilliseconds,
            CurrentStatus = currentStatus
        };
    }

    public static string ToUncPath(string host, string localPath)
    {
        if (string.IsNullOrWhiteSpace(localPath)) return string.Empty;
        if (localPath.StartsWith(@"\\")) return localPath;

        if (localPath.Length >= 2 && localPath[1] == ':')
        {
            char drive = localPath[0];
            string rest = localPath.Substring(2).TrimStart('\\', '/');
            return $@"\\{host}\{drive}$\{rest}";
        }

        return localPath;
    }

    public static int CleanSnccntxDirectories(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            return 0;

        int count = 0;
        try
        {
            // Only select directories starting with snccntx
            var sncDirs = Directory.GetDirectories(directoryPath, "snccntx*", SearchOption.AllDirectories);
            foreach (var dir in sncDirs)
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                    count++;
                }
                catch { }
            }
        }
        catch { }

        return count;
    }

    private static async Task StartServiceWithTimeoutAsync(
        ManagementScope scope, string serviceName, int timeoutSeconds, CancellationToken cancellationToken)
    {
        await Task.Run(async () =>
        {
            using var svc = new ManagementObject(scope, new ManagementPath($"Win32_Service.Name='{serviceName}'"), null);
            svc.Get();
            string state = svc["State"]?.ToString() ?? "";
            if (state.Equals("Running", StringComparison.OrdinalIgnoreCase)) return;

            var outParams = svc.InvokeMethod("StartService", null, null);
            uint ret = (uint)(outParams?["ReturnValue"] ?? 1u);
            if (ret != 0 && ret != 10) // 10 = service already running
            {
                throw new InvalidOperationException($"WMI StartService returned code {ret}");
            }

            // Wait for Running
            var stopTime = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (DateTime.UtcNow < stopTime)
            {
                await Task.Delay(1000, cancellationToken);
                svc.Get();
                if (string.Equals(svc["State"]?.ToString(), "Running", StringComparison.OrdinalIgnoreCase))
                    return;
            }
        }, cancellationToken);
    }

    private static async Task StopServiceWithTimeoutAsync(
        ManagementScope scope, string serviceName, int timeoutSeconds, CancellationToken cancellationToken)
    {
        await Task.Run(async () =>
        {
            using var svc = new ManagementObject(scope, new ManagementPath($"Win32_Service.Name='{serviceName}'"), null);
            svc.Get();
            string state = svc["State"]?.ToString() ?? "";
            if (state.Equals("Stopped", StringComparison.OrdinalIgnoreCase)) return;

            var outParams = svc.InvokeMethod("StopService", null, null);
            uint ret = (uint)(outParams?["ReturnValue"] ?? 1u);
            if (ret != 0 && ret != 5) // 5 = service cannot accept control right now or already stopping
            {
                // Proceed to poll
            }

            // Wait for Stopped
            var stopTime = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (DateTime.UtcNow < stopTime)
            {
                await Task.Delay(1000, cancellationToken);
                svc.Get();
                if (string.Equals(svc["State"]?.ToString(), "Stopped", StringComparison.OrdinalIgnoreCase))
                    return;
            }
        }, cancellationToken);
    }

    private static async Task<string> GetServiceDisplayNameAsync(
        ManagementScope scope, string serviceName, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var svc = new ManagementObject(scope, new ManagementPath($"Win32_Service.Name='{serviceName}'"), null);
                svc.Get();
                return svc["DisplayName"]?.ToString() ?? serviceName;
            }
            catch
            {
                return serviceName;
            }
        }, cancellationToken);
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
}
