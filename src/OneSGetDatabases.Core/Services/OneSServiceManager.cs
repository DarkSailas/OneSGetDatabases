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
                var scope = ConnectScope(node.Host, TimeSpan.FromSeconds(5));

                var query = new SelectQuery("Win32_Service", "PathName LIKE '%ragent.exe%' OR PathName LIKE '%ras.exe%'");
                using var searcher = new ManagementObjectSearcher(scope, query);
                using var results = searcher.Get();

                var agents = new List<(string Name, string DisplayName, string State, string StartName, string Path, int Port, string ClusterDir, string Version)>();
                var rasList = new List<(string Name, string DisplayName, string State, int RasPort, int TargetPort)>();

                foreach (ManagementObject svc in results)
                {
                    using var owned = svc;
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

    private static readonly TimeSpan ActionTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(90);
    private static readonly string[] KnownActions = ["start", "stop", "restart", "restart-clean-cache"];
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _actionLocks = new(StringComparer.OrdinalIgnoreCase);

    private sealed record ActionOutcome(bool Success, string Message, string Status, string RasStatus);

    public async Task<ServiceActionResult> ExecuteServiceActionAsync(
        ServiceActionRequest request, string clientIp, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        string action = request.Action.Trim().ToLowerInvariant();
        OneSServiceInfo? known = null;
        ActionOutcome outcome;

        if (!KnownActions.Contains(action))
        {
            outcome = new ActionOutcome(false, $"Неизвестное действие: {request.Action}", "Unknown", "");
        }
        else
        {
            // Cluster directory, RAS service and port are taken from discovery, never from the client:
            // the directory ends up in a remote cmd.exe command line and decides which processes get terminated.
            var services = await GetAllServicesStatusAsync(cancellationToken: cancellationToken);
            known = services.FirstOrDefault(s =>
                string.Equals(s.Host, request.Host, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(s.ServiceName, request.ServiceName, StringComparison.OrdinalIgnoreCase));
            if (known == null)
            {
                outcome = new ActionOutcome(false,
                    $"Служба {request.ServiceName} на {request.Host} не найдена среди опрошенных служб. Обновите список служб.",
                    "Unknown", "");
            }
            else
            {
                var gate = _actionLocks.GetOrAdd($"{known.Host}|{known.ServiceName}", _ => new SemaphoreSlim(1, 1));
                if (!await gate.WaitAsync(TimeSpan.Zero, CancellationToken.None))
                {
                    outcome = new ActionOutcome(false,
                        $"Над службой {known.DisplayName} на {known.Host} уже выполняется операция. Дождитесь её завершения.",
                        "Unknown", "");
                }
                else
                {
                    try
                    {
                        _logger.LogInformation("Executing action {Action} on {Host}:{Service} from IP {IP}...",
                            action, known.Host, known.ServiceName, clientIp);
                        outcome = await RunActionAsync(ScopeClusterDir(known, services), action);
                    }
                    finally
                    {
                        gate.Release();
                    }
                }
            }
        }

        sw.Stop();
        await _auditLog.LogActionAsync(new AuditLogEntry
        {
            ClientIp = clientIp,
            ClientHostName = AuditLogService.ResolveHostName(clientIp),
            Host = known?.Host ?? request.Host,
            ClusterPort = known?.ClusterPort ?? 0,
            ServiceName = known?.ServiceName ?? request.ServiceName,
            DisplayName = known?.DisplayName ?? request.ServiceName,
            Action = action.ToUpperInvariant().Replace('-', '_'),
            Status = outcome.Success ? "SUCCESS" : "FAILED",
            ErrorMessage = outcome.Success ? "" : outcome.Message,
            DurationMs = sw.ElapsedMilliseconds
        }, CancellationToken.None);

        if (known != null && outcome.Status != "Unknown")
        {
            await UpdateCachedStatusAsync(known, outcome.Status, outcome.RasStatus);
        }

        return new ServiceActionResult
        {
            Success = outcome.Success,
            Message = outcome.Message,
            DurationMs = sw.ElapsedMilliseconds,
            CurrentStatus = outcome.Status,
            RasStatus = outcome.RasStatus
        };
    }

    /// <summary>
    /// Several agents on one host may share a srvinfo directory; cleaning snccntx* from it would wipe
    /// the other clusters' cache too, so the cleanup is narrowed to this agent's reg_{port+1} folder.
    /// </summary>
    public static OneSServiceInfo ScopeClusterDir(OneSServiceInfo svc, IEnumerable<OneSServiceInfo> all)
    {
        var dir = NormalizeDir(svc.ClusterDir);
        if (dir.Length == 0 || svc.ClusterPort <= 0) return svc;

        bool shared = all.Any(o =>
            !ReferenceEquals(o, svc) &&
            string.Equals(o.Host, svc.Host, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(o.ServiceName, svc.ServiceName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(NormalizeDir(o.ClusterDir), dir, StringComparison.OrdinalIgnoreCase));

        return shared ? svc with { ClusterDir = Path.Combine(dir, $"reg_{svc.ClusterPort + 1}") } : svc;
    }

    private static string NormalizeDir(string? dir) => (dir ?? "").Trim().TrimEnd('\\', '/');

    private async Task UpdateCachedStatusAsync(OneSServiceInfo service, string status, string rasStatus)
    {
        await _cacheLock.WaitAsync(CancellationToken.None);
        try
        {
            if (_cachedServices == null) return;
            _cachedServices = _cachedServices
                .Select(s => string.Equals(s.Host, service.Host, StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(s.ServiceName, service.ServiceName, StringComparison.OrdinalIgnoreCase)
                    ? s with { Status = status, RasStatus = rasStatus.Length > 0 ? rasStatus : s.RasStatus, LastCheckedAt = DateTime.UtcNow }
                    : s)
                .ToList();
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// Runs the action with its own timeout instead of the request token: a restart cut in half
    /// by a closed browser tab would leave the cluster stopped.
    /// </summary>
    private async Task<ActionOutcome> RunActionAsync(OneSServiceInfo svc, string action)
    {
        using var cts = new CancellationTokenSource(ActionTimeout);
        var token = cts.Token;
        ManagementScope? scope = null;
        bool success = false;
        string message;
        var warnings = new List<string>();

        try
        {
            scope = await Task.Run(() => ConnectScope(svc.Host, TimeSpan.FromSeconds(15)), token);

            switch (action)
            {
                case "start":
                    await StartClusterAsync(scope, svc, warnings, token);
                    message = $"Служба {svc.DisplayName} на сервере {svc.Host} запущена.";
                    success = true;
                    break;

                case "stop":
                    await StopClusterAsync(scope, svc, token);
                    message = $"Служба {svc.DisplayName} на сервере {svc.Host} остановлена, рабочие процессы и сеансы завершены.";
                    success = true;
                    break;

                default: // restart, restart-clean-cache
                    (success, message) = await RestartClusterAsync(scope, svc, action == "restart-clean-cache", warnings, token);
                    break;
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            message = $"Операция над службой {svc.DisplayName} на {svc.Host} не завершилась за {ActionTimeout.TotalMinutes:0} мин. Проверьте состояние службы.";
            _logger.LogError("Service action {Action} on {Host}:{Service} timed out", action, svc.Host, svc.ServiceName);
        }
        catch (Exception ex)
        {
            message = $"Ошибка операции над службой {svc.DisplayName} на {svc.Host}: {ex.Message}";
            _logger.LogError(ex, "Service action {Action} on {Host}:{Service} failed", action, svc.Host, svc.ServiceName);
        }

        if (warnings.Count > 0)
        {
            message += " " + string.Join(" ", warnings);
        }

        var (status, rasStatus) = scope == null ? ("Unknown", "") : await ReadStatusAsync(scope, svc);
        return new ActionOutcome(success, message, status, rasStatus);
    }

    /// <summary>
    /// Once the stop has been attempted the start always runs, on its own deadline: a failed cleanup
    /// or an exhausted action timeout must not leave the cluster down.
    /// </summary>
    private async Task<(bool Success, string Message)> RestartClusterAsync(
        ManagementScope scope, OneSServiceInfo svc, bool cleanCache, List<string> warnings, CancellationToken token)
    {
        string? stopError = null;
        try
        {
            await StopClusterAsync(scope, svc, token);
        }
        catch (OperationCanceledException)
        {
            stopError = "остановка не завершилась за отведённое время";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stop of {Host}:{Service} failed during restart", svc.Host, svc.ServiceName);
            stopError = ex.Message;
        }

        bool cleaned = false;
        string cacheNote = "";
        if (stopError == null)
        {
            await Task.Delay(2000, CancellationToken.None);
            if (cleanCache)
            {
                (cleaned, cacheNote) = await CleanCacheAsync(scope, svc, token);
            }
        }

        using var startCts = new CancellationTokenSource(StartTimeout);
        try
        {
            await StartClusterAsync(scope, svc, warnings, startCts.Token);
        }
        catch (Exception ex)
        {
            string reason = ex is OperationCanceledException ? $"не запустилась за {StartTimeout.TotalSeconds:0} с" : ex.Message;
            _logger.LogError(ex, "Start of {Host}:{Service} failed during restart", svc.Host, svc.ServiceName);
            return (false, $"Служба {svc.DisplayName} на {svc.Host} после остановки не запущена: {reason}. Запустите её вручную." +
                           (cacheNote.Length > 0 ? $" {cacheNote}" : ""));
        }

        if (stopError != null)
        {
            return (false, $"Служба {svc.DisplayName} на {svc.Host} не остановилась ({stopError}), перезапуск не выполнен. Служба запущена повторно, проверьте её состояние.");
        }

        if (!cleanCache)
        {
            return (true, $"Служба {svc.DisplayName} на сервере {svc.Host} перезапущена. Активные сеансы сброшены.");
        }

        return cleaned
            ? (true, $"Служба {svc.DisplayName} на {svc.Host} перезапущена с очисткой серверного кэша. {cacheNote}")
            : (false, $"Служба {svc.DisplayName} на {svc.Host} перезапущена, но кэш очищен не полностью. {cacheNote}");
    }

    private async Task StopClusterAsync(ManagementScope scope, OneSServiceInfo svc, CancellationToken token)
    {
        bool rasStopped = false;
        if (!string.IsNullOrEmpty(svc.RasServiceName))
        {
            try
            {
                await StopServiceWithTimeoutAsync(scope, svc.RasServiceName, 0, "", 15, token);
                rasStopped = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("RAS service {Ras} on {Host} did not stop: {Msg}", svc.RasServiceName, svc.Host, ex.Message);
            }
        }

        try
        {
            await StopServiceWithTimeoutAsync(scope, svc.ServiceName, svc.ClusterPort, svc.ClusterDir, 25, token);
        }
        catch when (rasStopped)
        {
            // The agent keeps running: bring its RAS back so the cluster stays manageable
            try
            {
                using var rasCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await StartServiceWithTimeoutAsync(scope, svc.RasServiceName, 20, rasCts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("RAS service {Ras} on {Host} was not restarted: {Msg}", svc.RasServiceName, svc.Host, ex.Message);
            }
            throw;
        }
    }

    private async Task StartClusterAsync(ManagementScope scope, OneSServiceInfo svc, List<string> warnings, CancellationToken token)
    {
        await StartServiceWithTimeoutAsync(scope, svc.ServiceName, 35, token);
        if (string.IsNullOrEmpty(svc.RasServiceName)) return;
        try
        {
            await StartServiceWithTimeoutAsync(scope, svc.RasServiceName, 20, token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("RAS service {Ras} on {Host} did not start: {Msg}", svc.RasServiceName, svc.Host, ex.Message);
            warnings.Add($"Служба RAS {svc.RasServiceName} не запустилась: {ex.Message}");
        }
    }

    /// <summary>Never throws: the caller must start the cluster again whatever happens here.</summary>
    private async Task<(bool Cleaned, string Note)> CleanCacheAsync(ManagementScope scope, OneSServiceInfo svc, CancellationToken token)
    {
        if (!IsSafeClusterDir(svc.ClusterDir))
        {
            return (false, "Каталог кластера не определён или недопустим, кэш не очищался.");
        }

        string uncPath = ToUncPath(svc.Host, svc.ClusterDir);
        try
        {
            if (await Task.Run(() => Directory.Exists(uncPath), token))
            {
                var result = await Task.Run(() => CleanSnccntxDirectories(uncPath), token);
                return result.Failed == 0
                    ? (true, $"Удалено каталогов snccntx*: {result.Deleted}.")
                    : (false, $"Удалено каталогов snccntx*: {result.Deleted}, не удалось удалить: {result.Failed}.");
            }

            bool done = await CleanSnccntxViaWmiAsync(scope, svc.ClusterDir, token);
            return done
                ? (true, "Административная шара недоступна: команда удаления snccntx* выполнена через WMI, результат не проверен.")
                : (false, $"Нет доступа к {uncPath}, и очистка через WMI не завершилась.");
        }
        catch (OperationCanceledException)
        {
            return (false, "Очистка кэша прервана по таймауту операции.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache cleanup in {Dir} on {Host} failed", svc.ClusterDir, svc.Host);
            return (false, $"Ошибка очистки кэша: {ex.Message}");
        }
    }

    private static async Task<(string Status, string RasStatus)> ReadStatusAsync(ManagementScope scope, OneSServiceInfo svc)
    {
        try
        {
            // WMI calls are synchronous COM without their own deadline: cap the wait for the answer
            return await Task.Run(() =>
            {
                string status = ReadServiceState(scope, svc.ServiceName);
                string ras = string.IsNullOrEmpty(svc.RasServiceName) ? "NotFound" : ReadServiceState(scope, svc.RasServiceName);
                return (status, ras);
            }).WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            return ("Unknown", "");
        }
    }

    private static string ReadServiceState(ManagementScope scope, string serviceName)
    {
        try
        {
            using var svc = new ManagementObject(scope, new ManagementPath(ServicePath(serviceName)), null);
            svc.Get();
            return svc["State"]?.ToString() ?? "Unknown";
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return "Unknown";
        }
    }

    private static ManagementScope ConnectScope(string host, TimeSpan timeout)
    {
        var options = new ConnectionOptions
        {
            Timeout = timeout,
            EnablePrivileges = true,
            Impersonation = ImpersonationLevel.Impersonate,
            Authentication = AuthenticationLevel.PacketPrivacy
        };

        try
        {
            var scope = new ManagementScope($@"\\{host}\root\cimv2", options);
            scope.Connect();
            return scope;
        }
        catch (UnauthorizedAccessException) when (!host.Contains('.'))
        {
            // Kerberos needs the FQDN: retry with the DNS name when the short name was refused
            string fqdn = host;
            try
            {
                var entry = System.Net.Dns.GetHostEntry(host);
                if (!string.IsNullOrEmpty(entry.HostName) && entry.HostName.Contains('.'))
                {
                    fqdn = entry.HostName;
                }
            }
            catch (Exception ex) when (ex is SocketException or ArgumentException)
            {
                // Keep the original error below
            }

            if (string.Equals(fqdn, host, StringComparison.OrdinalIgnoreCase))
            {
                throw;
            }

            var scope = new ManagementScope($@"\\{fqdn}\root\cimv2", options);
            scope.Connect();
            return scope;
        }
    }

    private static string ServicePath(string serviceName) =>
        $"Win32_Service.Name='{serviceName.Replace(@"\", @"\\").Replace("'", @"\'")}'";

    public static string ToUncPath(string host, string localPath)
    {
        if (string.IsNullOrWhiteSpace(localPath)) return string.Empty;
        localPath = localPath.Trim('"', ' ', '\'');
        if (localPath.StartsWith(@"\\")) return localPath;

        if (localPath.Length >= 2 && localPath[1] == ':')
        {
            char drive = localPath[0];
            string rest = localPath.Substring(2).TrimStart('\\', '/');
            return $@"\\{host}\{drive}$\{rest}";
        }

        return localPath;
    }

    /// <summary>
    /// Cluster directory is safe to pass to the remote cmd.exe and to clean recursively:
    /// an absolute drive path below the root, without cmd metacharacters or parent segments.
    /// </summary>
    public static bool IsSafeClusterDir(string? clusterDir)
    {
        if (string.IsNullOrWhiteSpace(clusterDir)) return false;
        string dir = clusterDir.Trim('"', ' ', '\'');
        if (dir.IndexOfAny(['"', '&', '|', '<', '>', '^', '%', '\r', '\n']) >= 0) return false;
        if (dir.Length < 4 || !char.IsAsciiLetter(dir[0]) || dir[1] != ':' || dir[2] != '\\') return false;
        var segments = dir[3..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        // Windows drops trailing dots and spaces, so "..." or ". " resolve to the parent or the same folder
        return segments.Length > 0 && !segments.Any(s => s.TrimEnd('.', ' ').Length == 0);
    }

    public readonly record struct SnccntxCleanResult(int Deleted, int Failed);

    public static SnccntxCleanResult CleanSnccntxDirectories(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath)) return default;
        directoryPath = directoryPath.Trim('"', ' ', '\'');
        if (!Directory.Exists(directoryPath)) return default;

        int deleted = 0, failed = 0;
        // Only directories whose name starts with snccntx; cluster registry files stay untouched
        // Materialized first: deleting while enumerating would disturb the walk; unreadable subfolders are skipped
        var options = new System.IO.EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var dir in Directory.GetDirectories(directoryPath, "snccntx*", options))
        {
            // A nested snccntx* folder is already gone together with its parent
            if (!Directory.Exists(dir)) continue;
            try
            {
                Directory.Delete(dir, recursive: true);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed++;
            }
        }

        return new SnccntxCleanResult(deleted, failed);
    }

    private static async Task<bool> CleanSnccntxViaWmiAsync(ManagementScope scope, string localClusterDir, CancellationToken token)
    {
        string cleanDir = localClusterDir.Trim('"', ' ', '\'').TrimEnd('\\');

        return await Task.Run(async () =>
        {
            try
            {
                using var procClass = new ManagementClass(scope, new ManagementPath("Win32_Process"), null);
                using var inParams = procClass.GetMethodParameters("Create");
                // cmd strips the outer quotes; the directory was checked by IsSafeClusterDir
                // /d skips AutoRun, /v:off keeps "!" literal
                inParams["CommandLine"] = $"cmd.exe /d /v:off /c \"for /d /r \"{cleanDir}\" %d in (snccntx*) do @if exist \"%d\" rd /s /q \"%d\"\"";
                using var outParams = procClass.InvokeMethod("Create", inParams, null);
                if (Convert.ToUInt32(outParams?["ReturnValue"] ?? 1u) != 0) return false;
                uint pid = Convert.ToUInt32(outParams!["ProcessId"]);

                // Win32_Process gives no exit code: wait until cmd.exe is gone before starting the agent
                var deadline = DateTime.UtcNow.AddSeconds(60);
                while (DateTime.UtcNow < deadline)
                {
                    await Task.Delay(1000, token);
                    using var searcher = new ManagementObjectSearcher(scope, new SelectQuery("Win32_Process", $"ProcessId = {pid}"));
                    using var found = searcher.Get();
                    if (found.Count == 0) return true;
                }

                // Still deleting after a minute: stop it so it does not race with the starting agent
                KillProcessById(scope, pid);
                return false;
            }
            catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
            {
                return false;
            }
        }, token);
    }

    private static async Task StartServiceWithTimeoutAsync(
        ManagementScope scope, string serviceName, int timeoutSeconds, CancellationToken cancellationToken)
    {
        await Task.Run(async () =>
        {
            using var svc = new ManagementObject(scope, new ManagementPath(ServicePath(serviceName)), null);
            svc.Get();
            string state = svc["State"]?.ToString() ?? "";
            if (state.Equals("Running", StringComparison.OrdinalIgnoreCase)) return;

            using var outParams = svc.InvokeMethod("StartService", null, null);
            uint ret = Convert.ToUInt32(outParams?["ReturnValue"] ?? 1u);
            if (ret != 0 && ret != 10) // 10 = service already running
            {
                throw new InvalidOperationException($"WMI StartService для '{serviceName}' завершился ошибкой: {GetWmiServiceErrorMessage(ret)} (код {ret})");
            }

            var stopTime = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (DateTime.UtcNow < stopTime)
            {
                await Task.Delay(1000, cancellationToken);
                svc.Get();
                if (string.Equals(svc["State"]?.ToString(), "Running", StringComparison.OrdinalIgnoreCase))
                    return;
            }

            svc.Get();
            string finalState = svc["State"]?.ToString() ?? "Unknown";
            if (!string.Equals(finalState, "Running", StringComparison.OrdinalIgnoreCase))
            {
                throw new TimeoutException($"Служба '{serviceName}' не перешла в состояние Running за {timeoutSeconds} с. Текущее состояние: {finalState}");
            }
        }, cancellationToken);
    }

    private static async Task StopServiceWithTimeoutAsync(
        ManagementScope scope, string serviceName, int clusterPort, string clusterDir, int timeoutSeconds, CancellationToken cancellationToken)
    {
        await Task.Run(async () =>
        {
            using var svc = new ManagementObject(scope, new ManagementPath(ServicePath(serviceName)), null);
            svc.Get();
            uint agentPid = 0;
            if (svc["ProcessId"] != null && uint.TryParse(svc["ProcessId"]?.ToString(), out var parsedPid))
            {
                agentPid = parsedPid;
            }

            if (!IsStopped(svc))
            {
                try
                {
                    using var _ = svc.InvokeMethod("StopService", null, null);
                }
                catch (ManagementException)
                {
                    // The state check below decides; a hung agent is terminated
                }

                var stopTime = DateTime.UtcNow.AddSeconds(timeoutSeconds);
                while (DateTime.UtcNow < stopTime && !IsStopped(svc))
                {
                    await Task.Delay(1000, cancellationToken);
                }

                if (!IsStopped(svc) && agentPid > 0)
                {
                    KillProcessById(scope, agentPid);
                    var forceTime = DateTime.UtcNow.AddSeconds(5);
                    while (DateTime.UtcNow < forceTime && !IsStopped(svc))
                    {
                        await Task.Delay(1000, cancellationToken);
                    }
                }
            }

            // Terminate the cluster's rphost/rmngr: drops client sessions and releases locks on snccntx*
            if (clusterPort > 0 || !string.IsNullOrWhiteSpace(clusterDir))
            {
                KillClusterWorkerProcesses(scope, clusterPort, clusterDir, agentPid);
            }

            if (!IsStopped(svc))
            {
                throw new TimeoutException($"Служба '{serviceName}' не остановилась за {timeoutSeconds} с. Текущее состояние: {svc["State"]}");
            }
        }, cancellationToken);
    }

    private static bool IsStopped(ManagementObject svc)
    {
        try
        {
            svc.Get();
            return string.Equals(svc["State"]?.ToString(), "Stopped", StringComparison.OrdinalIgnoreCase);
        }
        catch (ManagementException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"-range\s+\S+", RegexOptions.IgnoreCase)]
    private static partial Regex PortRangeArgRegex();

    [GeneratedRegex(@"(?:-regport\s+|-port\s+|:)(\d+)(?!\d)", RegexOptions.IgnoreCase)]
    private static partial Regex ClusterPortArgRegex();

    /// <summary>
    /// rphost/rmngr belongs to the cluster when its command line names the cluster directory
    /// (as a whole path) or the agent/registry port (as a whole number, the -range list excluded).
    /// </summary>
    public static bool IsClusterWorkerCommandLine(string cmdLine, int clusterPort, string clusterDir)
    {
        if (string.IsNullOrEmpty(cmdLine)) return false;

        string dir = (clusterDir ?? "").Trim('"', ' ', '\'').TrimEnd('\\', '/');
        if (dir.Length > 3)
        {
            int idx = 0;
            while ((idx = cmdLine.IndexOf(dir, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int end = idx + dir.Length;
                if (end == cmdLine.Length || cmdLine[end] is '"' or '\\' or '/' or ' ' or '\t') return true;
                idx = end;
            }
        }

        if (clusterPort <= 0) return false;
        string withoutRange = PortRangeArgRegex().Replace(cmdLine, " ");
        foreach (Match m in ClusterPortArgRegex().Matches(withoutRange))
        {
            if (int.TryParse(m.Groups[1].Value, out int port) && (port == clusterPort || port == clusterPort + 1))
                return true;
        }
        return false;
    }

    private static void KillClusterWorkerProcesses(ManagementScope scope, int clusterPort, string clusterDir, uint agentPid)
    {
        try
        {
            var query = new SelectQuery("Win32_Process", "Name = 'rphost.exe' OR Name = 'rmngr.exe'");
            using var searcher = new ManagementObjectSearcher(scope, query);
            using var processes = searcher.Get();

            foreach (ManagementObject proc in processes)
            {
                using (proc)
                {
                    try
                    {
                        uint ppid = proc["ParentProcessId"] != null ? Convert.ToUInt32(proc["ParentProcessId"]) : 0;
                        string cmdLine = proc["CommandLine"]?.ToString() ?? "";
                        bool belongsToCluster = (agentPid > 0 && ppid == agentPid)
                            || IsClusterWorkerCommandLine(cmdLine, clusterPort, clusterDir);
                        if (belongsToCluster)
                        {
                            proc.InvokeMethod("Terminate", [0]);
                        }
                    }
                    catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
                    {
                        // Process already exited
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            // Worker cleanup is best effort; the service state check reports real failures
        }
    }

    private static void KillProcessById(ManagementScope scope, uint pid)
    {
        try
        {
            using var proc = new ManagementObject(scope, new ManagementPath($"Win32_Process.Handle='{pid}'"), null);
            proc.InvokeMethod("Terminate", [0]);
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            // Process already exited
        }
    }

    private static string GetWmiServiceErrorMessage(uint code) => code switch
    {
        2 => "Отказано в доступе (Access Denied)",
        3 => "Запущены зависимые службы (Dependent Services Running)",
        4 => "Неверный запрос управления службой (Invalid Service Control)",
        5 => "Служба не может принять команду в данный момент (Service Cannot Accept Control)",
        6 => "Служба не активна (Service Not Active)",
        7 => "Таймаут запроса к службе (Service Request Timeout)",
        8 => "Неизвестный сбой (Unknown Failure)",
        9 => "Путь к исполняемому файлу не найден (Path Not Found)",
        10 => "Служба уже запущена (Service Already Running)",
        11 => "База данных диспетчера служб заблокирована (Database Locked)",
        14 => "Служба отключена (Service Disabled)",
        15 => "Ошибка входа в систему учетной записи службы (Logon Failed)",
        _ => $"Код {code}"
    };

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
