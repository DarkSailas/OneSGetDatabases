using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OneSGetDatabases.Core.Helpers;
using OneSGetDatabases.Core.Interfaces;
using OneSGetDatabases.Core.Models;

namespace OneSGetDatabases.Core.Services;

public class InfobaseDiscoveryService : IInfobaseDiscoveryService
{
    private const string ClusterHealthCacheFileName = "cluster_health_cache.json";

    private readonly IRacService _racService;
    private readonly ICimService _cimService;
    private readonly IActiveDirectoryService _adService;
    private readonly IConsulService _consulService;
    private readonly IDbmsInspectorService _dbmsInspector;
    private readonly IClusterDiscoveryEngine _discoveryEngine;
    private readonly ILogger<InfobaseDiscoveryService> _logger;
    private readonly List<ClusterConfig> _clusters;
    private readonly ConcurrentDictionary<string, ClusterHealthInfo> _clusterHealth = new(StringComparer.OrdinalIgnoreCase);

    public InfobaseDiscoveryService(
        IRacService racService,
        ICimService cimService,
        IClusterDiscoveryEngine discoveryEngine,
        IActiveDirectoryService adService,
        IConsulService consulService,
        IDbmsInspectorService dbmsInspector,
        IOptions<List<ClusterConfig>> clustersOptions,
        ILogger<InfobaseDiscoveryService> logger)
    {
        _racService = racService;
        _cimService = cimService;
        _discoveryEngine = discoveryEngine;
        _adService = adService;
        _consulService = consulService;
        _dbmsInspector = dbmsInspector;
        _logger = logger;
        _clusters = clustersOptions.Value ?? [];

        // Load pre-existing cluster health cache from disk
        var diskCache = PersistentCacheHelper.LoadFromDisk<Dictionary<string, ClusterHealthInfo>>(ClusterHealthCacheFileName);
        if (diskCache != null)
        {
            foreach (var kvp in diskCache)
            {
                _clusterHealth[kvp.Key] = kvp.Value;
            }
        }

        // Initialize any configured cluster not in cache
        foreach (var c in _clusters)
        {
            if (!_clusterHealth.ContainsKey(c.Server))
            {
                _clusterHealth[c.Server] = new ClusterHealthInfo
                {
                    Server = c.Server,
                    Host = c.Host,
                    ServerPort = c.ServerPort,
                    RasPort = c.RasPort,
                    RasAddress = c.RasAddress,
                    Environment = c.Environment,
                    Status = ClusterStatus.Offline,
                    ErrorMessage = "Ожидание первого опроса фоновой службой...",
                    LastCheckedAt = DateTime.UtcNow
                };
            }
        }
    }

    private readonly ConcurrentQueue<ClusterDiscoveryLogEntry> _discoveryLogs = new();

    public IReadOnlyList<ClusterHealthInfo> GetClusterHealthStatuses()
    {
        return _clusterHealth.Values.ToList();
    }

    public IReadOnlyList<ClusterDiscoveryLogEntry> GetDiscoveryLogs()
    {
        var engineLogs = _discoveryEngine.GetLastDiscoveryLogs();
        var all = new List<ClusterDiscoveryLogEntry>(engineLogs);
        all.AddRange(_discoveryLogs);
        return all.OrderByDescending(l => l.Timestamp).ToList();
    }

    public void RecordDiscoveryLog(string server, string host, int port, string environment, string stage, string level, string message, string details = "")
    {
        _discoveryLogs.Enqueue(new ClusterDiscoveryLogEntry
        {
            Timestamp = DateTime.UtcNow,
            Server = server,
            Host = host,
            Port = port,
            Environment = environment,
            Stage = stage,
            Level = level,
            Message = message,
            Details = details
        });
    }

    public async Task<List<InfoBaseItem>> DiscoverAllAsync(CancellationToken cancellationToken = default)
    {
        _discoveryLogs.Clear();
        // 1. Dynamic cluster discovery via WMI/CIM
        var dynamicClusters = await _discoveryEngine.DiscoverAllClustersAsync(cancellationToken);

        // 2. Merge with static clusters (deduplicating by Server address)
        var clusterMap = new Dictionary<string, ClusterConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var dc in dynamicClusters)
        {
            clusterMap[dc.Server] = dc;
        }
        foreach (var sc in _clusters)
        {
            clusterMap.TryAdd(sc.Server, sc);
        }

        var activeClusters = clusterMap.Values.ToList();
        _logger.LogInformation("Starting full discovery across {Count} active clusters ({Dynamic} dynamic, {Static} static)...", 
            activeClusters.Count, dynamicClusters.Count, _clusters.Count);

        // Preload AD groups cache first
        try
        {
            await _adService.RefreshCacheAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("AD Cache refresh failed: {Msg}", ex.Message);
        }

        var allResults = new List<InfoBaseItem>();
        var clusterTasks = activeClusters.Select(c => DiscoverClusterAsync(c, cancellationToken));
        var clusterResults = await Task.WhenAll(clusterTasks);

        foreach (var res in clusterResults)
        {
            allResults.AddRange(res);
        }

        // Prune stale clusters from _clusterHealth that are no longer active
        var activeKeys = new HashSet<string>(activeClusters.Select(c => c.Server), StringComparer.OrdinalIgnoreCase);
        foreach (var key in _clusterHealth.Keys)
        {
            if (!activeKeys.Contains(key))
            {
                _clusterHealth.TryRemove(key, out _);
            }
        }

        // Persist cluster health statuses
        try
        {
            PersistentCacheHelper.SaveToDisk(ClusterHealthCacheFileName, _clusterHealth.ToDictionary(k => k.Key, v => v.Value));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to persist cluster health cache");
        }

        _logger.LogInformation("Full discovery complete. Total databases discovered: {Total}", allResults.Count);

        // Background Pre-warming of DBMS file sizes and AD group rosters
        _ = Task.Run(async () =>
        {
            try
            {
                // 1. Pre-warm unique SQL servers files and sizes in parallel
                var uniqueSqlServers = allResults
                    .Select(b => b.SQL)
                    .Where(s => !string.IsNullOrEmpty(s) && !s.Equals("Неизвестно", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                await Parallel.ForEachAsync(uniqueSqlServers, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (server, ct) =>
                {
                    try
                    {
                        await _dbmsInspector.GetServerAllDatabaseFilesAsync(server, ct);
                    }
                    catch { }
                });

                // 2. Pre-warm unique AD groups roster
                var uniqueGroups = allResults
                    .SelectMany(b => new[] { b.AccessGroup, b.RaGroup, b.OneCGroup })
                    .Where(g => !string.IsNullOrEmpty(g) && g != "Отсутствует" && g != "—" && !g.Contains("не удалось"))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                await Parallel.ForEachAsync(uniqueGroups, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (grp, ct) =>
                {
                    try
                    {
                        await _adService.GetGroupMembersAsync(grp, ct);
                    }
                    catch { }
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Background cache pre-warming completed with notice");
            }
        }, CancellationToken.None);

        return allResults;
    }

    public async Task<List<InfoBaseItem>> DiscoverClusterAsync(ClusterConfig cluster, CancellationToken cancellationToken = default)
    {
        var result = new List<InfoBaseItem>();
        string host = cluster.Host;
        int port = cluster.ServerPort;
        string rasAddress = cluster.RasAddress;

        var health = new ClusterHealthInfo
        {
            Server = cluster.Server,
            Host = host,
            ServerPort = port,
            RasPort = cluster.RasPort,
            RasAddress = rasAddress,
            Environment = cluster.Environment,
            LastCheckedAt = DateTime.UtcNow
        };

        // If RAS was not found / not configured for this cluster
        if (cluster.RasPort <= 0)
        {
            health.Status = ClusterStatus.Offline;
            health.IsRasPortOpen = false;
            health.ErrorMessage = "Служба RAS не найдена на сервере (служба ras.exe не запущена или не привязана к кластеру)";

            try
            {
                var (svcUser, _, _, _) = await _cimService.GetServiceInfoAsync(host, port, cancellationToken);
                if (!string.IsNullOrEmpty(svcUser))
                {
                    health.CimStatus = svcUser;
                }
            }
            catch
            {
                // Ignore CIM error on offline RAS
            }

            _clusterHealth[cluster.Server] = health;
            RecordDiscoveryLog(cluster.Server, host, port, cluster.Environment, "Служба RAS", "Warning",
                $"Служба удаленного администрирования ras.exe не обнаружена для кластера {cluster.Server}. Для сбора списка информационных баз необходимо запустить службу ras.exe.");

            _logger.LogWarning("Cluster {Server}: No RAS port detected. Skipping RAC query.", cluster.Server);
            return result;
        }

        // 1. Multi-attempt TCP check for RAS port availability
        bool isRasOpen = await TestPortWithRetriesAsync(host, cluster.RasPort, timeoutMs: 4000, maxRetries: 2);
        health.IsRasPortOpen = isRasOpen;

        if (!isRasOpen)
        {
            health.Status = ClusterStatus.Offline;
            health.ErrorMessage = $"Служба RAS (порт {cluster.RasPort}) остановлена или не отвечает";

            try
            {
                var (svcUser, _, _, _) = await _cimService.GetServiceInfoAsync(host, port, cancellationToken);
                if (!string.IsNullOrEmpty(svcUser))
                {
                    health.CimStatus = svcUser;
                }
            }
            catch
            {
                // Ignore CIM error on offline RAS
            }

            _clusterHealth[cluster.Server] = health;
            RecordDiscoveryLog(cluster.Server, host, cluster.RasPort, cluster.Environment, "Порт RAS", "Error",
                $"Служба RAS (порт {cluster.RasPort}) не отвечает на сервере {host}. Служба ras.exe остановлена или заблокирована брандмауэром.");

            _logger.LogWarning("Cluster {Server}: RAS port {RasPort} is unreachable. Skipping RAC query.", cluster.Server, cluster.RasPort);
            return result;
        }

        _logger.LogInformation("Scanning cluster {Server} (RAS: {Ras}, TCP: {TcpStatus})...", 
            cluster.Server, rasAddress, isRasOpen ? "Open" : "Not answering");

        try
        {
            // 2. Resolve host IP
            string serverIp = await GetHostIpAsync(host);

            // 3. Query CIM service info
            var (svcUser, svcName, clusterPath, cimErr) = await _cimService.GetServiceInfoAsync(host, port, cancellationToken);
            if (cimErr != null)
            {
                _logger.LogDebug("CIM Info for {Server}: {Err}", cluster.Server, cimErr);
                RecordDiscoveryLog(cluster.Server, host, port, cluster.Environment, "WMI / Служба", "Warning",
                    $"Не удалось получить свойства службы ragent через WMI: {cimErr}");
            }
            else if (!string.IsNullOrEmpty(svcUser))
            {
                health.CimStatus = svcUser;
            }

            // 4. Get platform version
            string platformOut = await _racService.RunSmartRacAsync(
                rasAddress, "agent", "version", adminUser: cluster.ClusterUser, adminPwd: cluster.ClusterPassword, cancellationToken: cancellationToken);
            string platform = RacParser.ParsePlatformVersion(platformOut);
            cluster.Platform = platform;
            health.PlatformVersion = platform;

            // 5. Get clusters list
            string clusterListOut = await _racService.RunSmartRacAsync(
                rasAddress, "cluster", "list", adminUser: cluster.ClusterUser, adminPwd: cluster.ClusterPassword, cancellationToken: cancellationToken);
            var clusters = RacParser.ParseClusters(clusterListOut);

            if (clusters.Count == 0)
            {
                if (string.IsNullOrWhiteSpace(clusterListOut))
                {
                    health.Status = ClusterStatus.Offline;
                    health.DatabasesCount = 0;
                    health.ErrorMessage = $"Не удалось получить ответ от службы RAS ({rasAddress})";
                }
                else
                {
                    health.Status = ClusterStatus.Empty;
                    health.DatabasesCount = 0;
                    health.ErrorMessage = "В RAS-агенте не зарегистрировано ни одного кластера 1С";
                }
                _clusterHealth[cluster.Server] = health;
                _logger.LogDebug("No 1C clusters returned from RAS {RasAddress}", rasAddress);
                RecordDiscoveryLog(cluster.Server, host, port, cluster.Environment, "Кластер 1С", "Warning",
                    $"В RAS-агенте ({rasAddress}) не обнаружено зарегистрированных кластеров 1С с портом {port}. Проверьте привязку службы RAS к агенту или перезапустите службу.");
                return result;
            }

            foreach (var (clusterUuid, clusterName) in clusters)
            {
                // 6. Get infobases list
                string ibSummaryOut = await _racService.RunSmartRacAsync(
                    rasAddress, "infobase", "summary list", clusterId: clusterUuid,
                    adminUser: cluster.ClusterUser, adminPwd: cluster.ClusterPassword, cancellationToken: cancellationToken);

                var infobases = RacParser.ParseInfobases(ibSummaryOut);

                foreach (var (ibUuid, ibName, ibDesc) in infobases)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    // 7. Get connection details (DB Server, DB Name)
                    string ibDetailsOut = await _racService.RunSmartRacAsync(
                        rasAddress, "infobase", $"info --infobase={ibUuid}", clusterId: clusterUuid,
                        adminUser: cluster.ClusterUser, adminPwd: cluster.ClusterPassword, cancellationToken: cancellationToken);

                    var (dbServer, dbName, dbmsType) = RacParser.ParseInfobaseDetails(ibDetailsOut);

                    // 8. Resolve Consul
                    var (consulName, consulSqlServer) = await _consulService.ResolveServiceAsync(ibName, cancellationToken);

                    string finalSqlServer = !string.IsNullOrEmpty(dbServer) ? dbServer : consulSqlServer;
                    string finalSqlDbName = !string.IsNullOrEmpty(dbName) ? dbName : "Неизвестно";

                    // 9. Resolve Access Group
                    string accessGroup = _adService.ResolveAccessGroup(ibName);

                    // 10. Resolve SA_INFO
                    var (raGroup, oneCGroup, v8iFile) = _adService.ResolveSaInfoGroups(ibName, platform);

                    var item = new InfoBaseItem
                    {
                        Name = ibName,
                        Description = ibDesc,
                        UUID = ibUuid,
                        AccessGroup = accessGroup,
                        Cluster = cluster.Server,
                        ServerIP = serverIp,
                        ClusterUUID = clusterUuid,
                        Platform = platform,
                        Consul = consulName,
                        SQL = finalSqlServer,
                        SQLDbName = finalSqlDbName,
                        ServiceUser = svcUser,
                        ServiceName = svcName,
                        ClusterPath = clusterPath,
                        Environment = cluster.Environment,
                        V8iFile = v8iFile,
                        RaGroup = raGroup,
                        OneCGroup = oneCGroup,
                        LastScannedAt = DateTime.UtcNow
                    };

                    result.Add(item);
                }
            }

            health.DatabasesCount = result.Count;
            health.Status = result.Count > 0 ? ClusterStatus.Online : ClusterStatus.Empty;
            if (result.Count == 0)
            {
                health.ErrorMessage = "Кластер доступен, но не содержит информационных баз";
            }
            _clusterHealth[cluster.Server] = health;
        }
        catch (Exception ex)
        {
            string errMsg = ex.Message;
            health.ErrorMessage = errMsg;
            if (errMsg.Contains("аутентификаци", StringComparison.OrdinalIgnoreCase) ||
                errMsg.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                errMsg.Contains("access denied", StringComparison.OrdinalIgnoreCase))
            {
                health.Status = ClusterStatus.AuthError;
            }
            else
            {
                health.Status = ClusterStatus.Error;
            }
            _clusterHealth[cluster.Server] = health;
            _logger.LogError(ex, "Failed to discover cluster {Server}: {Message}", cluster.Server, ex.Message);
            RecordDiscoveryLog(cluster.Server, host, port, cluster.Environment, "Опрос RAC",
                health.Status == ClusterStatus.AuthError ? "AuthError" : "Error",
                $"Сбой опроса кластера: {errMsg}", ex.ToString());
        }

        return result;
    }

    private static async Task<string> GetHostIpAsync(string hostName)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(hostName);
            var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            return ipv4?.ToString() ?? "Неизвестно";
        }
        catch
        {
            return "Неизвестно";
        }
    }

    private static async Task<bool> TestPortWithRetriesAsync(string host, int port, int timeoutMs = 4000, int maxRetries = 2)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            // 1. Try short hostname
            if (await TryConnectAsync(host, port, timeoutMs))
            {
                return true;
            }

            // 2. Try FQDN
            if (!host.Contains('.') && !string.IsNullOrWhiteSpace(host))
            {
                var fqdn = $"{host}.example.corp";
                if (await TryConnectAsync(fqdn, port, timeoutMs))
                {
                    return true;
                }
            }

            // 3. Try resolved IP
            var ip = await GetHostIpAsync(host);
            if (ip != "Неизвестно" && ip != host)
            {
                if (await TryConnectAsync(ip, port, timeoutMs))
                {
                    return true;
                }
            }

            if (attempt < maxRetries)
            {
                await Task.Delay(500);
            }
        }

        return false;
    }

    private static async Task<bool> TryConnectAsync(string target, int port, int timeoutMs)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            using var client = new TcpClient();
            await client.ConnectAsync(target, port, cts.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }
}
