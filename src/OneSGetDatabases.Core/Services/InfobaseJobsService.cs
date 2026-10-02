using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OneSGetDatabases.Core.Helpers;
using OneSGetDatabases.Core.Interfaces;
using OneSGetDatabases.Core.Models;

namespace OneSGetDatabases.Core.Services;

public class InfobaseJobsService : IInfobaseJobsService
{
    private readonly IDatabaseCacheService _cache;
    private readonly IClusterDiscoveryEngine _discoveryEngine;
    private readonly IRacService _racService;
    private readonly ClusterDiscoveryConfig _discoveryConfig;
    private readonly List<ClusterConfig> _staticClusters;
    private readonly SqlRestoreConfig _restoreConfig;
    private readonly ILogger<InfobaseJobsService> _logger;

    public InfobaseJobsService(
        IDatabaseCacheService cache,
        IClusterDiscoveryEngine discoveryEngine,
        IRacService racService,
        IOptions<ClusterDiscoveryConfig> discoveryConfig,
        IOptions<List<ClusterConfig>> staticClusters,
        IOptions<SqlRestoreConfig> restoreConfig,
        ILogger<InfobaseJobsService> logger)
    {
        _cache = cache;
        _discoveryEngine = discoveryEngine;
        _racService = racService;
        _discoveryConfig = discoveryConfig.Value;
        _staticClusters = staticClusters.Value ?? [];
        _restoreConfig = restoreConfig.Value;
        _logger = logger;
    }

    public async Task<InfobaseJobsResult> DenyScheduledJobsAsync(
        string sqlServer,
        string sqlDatabase,
        string? infobaseName,
        string? clusterHost,
        CancellationToken cancellationToken = default)
    {
        var dev = _cache.GetDev();
        var items = dev
            .Where(i => SameSqlServer(i.SQL, sqlServer) && i.SQLDbName.Equals(sqlDatabase, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (items.Count == 0 && !string.IsNullOrWhiteSpace(infobaseName))
        {
            items = dev
                .Where(i => i.Name.Equals(infobaseName, StringComparison.OrdinalIgnoreCase)
                            && (string.IsNullOrWhiteSpace(clusterHost) || HostOf(i.Cluster).Equals(clusterHost.Trim(), StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        if (items.Count == 0)
            return new InfobaseJobsResult(false, $"Информационная база 1С для {sqlServer}/{sqlDatabase} не найдена в кэше DEV-кластеров");

        var messages = new List<string>();
        bool allOk = true;

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (ok, msg) = await DenyForInfobaseAsync(item, cancellationToken).ConfigureAwait(false);
            allOk &= ok;
            messages.Add($"{item.Name} ({item.Cluster}): {msg}");
        }

        return new InfobaseJobsResult(allOk, string.Join("; ", messages));
    }

    private async Task<(bool Success, string Message)> DenyForInfobaseAsync(InfoBaseItem item, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(item.UUID) || string.IsNullOrWhiteSpace(item.ClusterUUID))
            return (false, "в кэше нет UUID базы или кластера");

        var cluster = await ResolveClusterAsync(item, ct).ConfigureAwait(false);
        if (cluster == null || cluster.RasPort <= 0)
            return (false, $"не найден RAS для кластера {item.Cluster}");

        string clusterUser = !string.IsNullOrWhiteSpace(cluster.ClusterUser) ? cluster.ClusterUser : _discoveryConfig.DefaultClusterUser;
        string clusterPwd = !string.IsNullOrWhiteSpace(cluster.ClusterUser) ? cluster.ClusterPassword : _discoveryConfig.DefaultClusterPassword;
        // Infobase administrators from settings are tried in order; the cluster admin account is the last resort
        var candidates = _restoreConfig.InfobaseAdminCredentials
            .Where(c => !string.IsNullOrWhiteSpace(c.Username))
            .Select(c => (User: c.Username.Trim(), Password: c.EffectivePassword))
            .ToList();
        if (!string.IsNullOrWhiteSpace(clusterUser)
            && !candidates.Any(c => c.User.Equals(clusterUser, StringComparison.OrdinalIgnoreCase)))
        {
            candidates.Add((clusterUser, clusterPwd));
        }
        if (candidates.Count == 0)
            candidates.Add(("", ""));

        var secrets = candidates.Select(c => c.Password).Append(clusterPwd).ToList();
        var errors = new List<string>();

        // Cluster-level auth: configured admin first, then none (a DEV cluster may have no administrators,
        // and passing an unknown cluster admin makes rac fail with "cluster administrator is not authenticated")
        var clusterAuthOptions = new List<(string User, string Password)>();
        if (!string.IsNullOrWhiteSpace(clusterUser))
            clusterAuthOptions.Add((clusterUser, clusterPwd));
        clusterAuthOptions.Add(("", ""));
        int clusterAuthIndex = 0;

        foreach (var (ibUser, ibPwd) in candidates)
        {
            string who = string.IsNullOrWhiteSpace(ibUser) ? "без пользователя ИБ" : $"'{ibUser}'";

            while (true)
            {
                var (cUser, cPwd) = clusterAuthOptions[clusterAuthIndex];
                var result = await _racService.RunRacAsync(
                    BuildUpdateArgs(cluster.RasAddress, item, cUser, cPwd, ibUser, ibPwd), timeoutSeconds: 30, cancellationToken: ct).ConfigureAwait(false);

                if (result.Success)
                {
                    _logger.LogInformation("Регламентные задания заблокированы для {Ib} на {Ras} (пользователь ИБ {User})", item.Name, cluster.RasAddress, who);
                    return (true, $"регламентные задания заблокированы (пользователь ИБ {who})");
                }

                string error = MaskAll(CompactLines(string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error), secrets);
                string clusterWho = string.IsNullOrWhiteSpace(cUser) ? "без администратора кластера" : $"администратор кластера '{cUser}'";
                _logger.LogWarning("rac отклонил блокировку регламентных заданий для {Ib} на {Ras} ({ClusterAuth}, пользователь ИБ {User}): {Error}",
                    item.Name, cluster.RasAddress, clusterWho, who, error);

                if (IsRasUnavailable(result))
                {
                    errors.Add($"{who}: {error}");
                    return (false, $"RAS {cluster.RasAddress} недоступен: {string.Join("; ", errors)}");
                }

                if (IsClusterAuthError(error) && clusterAuthIndex < clusterAuthOptions.Count - 1)
                {
                    clusterAuthIndex++;
                    continue; // same infobase credentials with the next cluster auth option
                }

                errors.Add($"{who} ({clusterWho}): {error}");
                break; // next infobase credentials
            }
        }

        return (false, $"ошибка rac ({string.Join("; ", errors)}). Проверьте SqlRestore:InfobaseAdminCredentials — нужен администратор информационной базы");
    }

    private static string BuildUpdateArgs(string rasAddress, InfoBaseItem item, string clusterUser, string clusterPwd, string ibUser, string ibPwd)
    {
        var args = new List<string>
        {
            rasAddress,
            "infobase",
            "update",
            $"--cluster={item.ClusterUUID}"
        };
        if (!string.IsNullOrWhiteSpace(clusterUser))
        {
            args.Add($"--cluster-user={Quote(clusterUser)}");
            args.Add($"--cluster-pwd={Quote(clusterPwd)}");
        }
        args.Add($"--infobase={item.UUID}");
        if (!string.IsNullOrWhiteSpace(ibUser))
        {
            args.Add($"--infobase-user={Quote(ibUser)}");
            args.Add($"--infobase-pwd={Quote(ibPwd)}");
        }
        args.Add("--scheduled-jobs-deny=on");
        return string.Join(' ', args);
    }

    private static bool IsClusterAuthError(string error)
        => error.Contains("Администратор кластера", StringComparison.OrdinalIgnoreCase)
           || error.Contains("cluster administrator", StringComparison.OrdinalIgnoreCase);

    // rac.exe itself returns 255/-1 on any error, so availability is decided by the message, not the exit code
    private static bool IsRasUnavailable(RacResult result)
    {
        if (result.ExitCode == -2) return true; // timeout from RacService
        string text = result.Error + " " + result.Output;
        return text.Contains("rac.exe not found", StringComparison.OrdinalIgnoreCase)
               || text.Contains("10061", StringComparison.Ordinal)
               || text.Contains("10060", StringComparison.Ordinal)
               || text.Contains("connection refused", StringComparison.OrdinalIgnoreCase)
               || text.Contains("No connection could be made", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Сервер не обнаружен", StringComparison.OrdinalIgnoreCase);
    }

    private static string MaskAll(string text, IEnumerable<string?> secrets)
    {
        foreach (var s in secrets)
        {
            if (string.IsNullOrEmpty(s)) continue;
            text = Mask(text, s, Quote(s));
        }
        return text;
    }

    private async Task<ClusterConfig?> ResolveClusterAsync(InfoBaseItem item, CancellationToken ct)
    {
        var fromStatic = _staticClusters.FirstOrDefault(c =>
            c.RasPort > 0 && c.Server.Equals(item.Cluster, StringComparison.OrdinalIgnoreCase));
        if (fromStatic != null) return fromStatic;

        string host = HostOf(item.Cluster);
        var node = _discoveryConfig.Servers.FirstOrDefault(n => ServerNameHelper.IsSameServer(n.Host, host))
                   ?? new ServerNodeConfig { Host = host, Environment = item.Environment };

        var clusters = await _discoveryEngine.DiscoverHostClustersAsync(node, ct).ConfigureAwait(false);
        int port = PortOf(item.Cluster);

        return clusters.FirstOrDefault(c => c.Server.Equals(item.Cluster, StringComparison.OrdinalIgnoreCase))
               ?? clusters.FirstOrDefault(c => c.ServerPort == port);
    }

    private static bool SameSqlServer(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        if (a.Trim().Equals(b.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        return ServerNameHelper.IsSameServer(a, b);
    }

    private static string HostOf(string cluster) => cluster.Split(':')[0].Trim();

    private static int PortOf(string cluster)
        => cluster.Contains(':') && int.TryParse(cluster.Split(':')[1], out var p) ? p : 1540;

    // Windows command-line quoting (CommandLineToArgvW rules): backslashes before a quote and at the end are doubled
    private static string Quote(string value)
    {
        var sb = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char c in value)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            sb.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
            sb.Append(c);
            backslashes = 0;
        }
        sb.Append('\\', backslashes * 2);
        sb.Append('"');
        return sb.ToString();
    }

    // rac prints "Ошибка операции администрирования" followed by the actual reason on the next line
    private static string CompactLines(string text)
        => string.Join(" / ", text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string Mask(string text, params string?[] secrets)
    {
        foreach (var s in secrets)
        {
            if (!string.IsNullOrEmpty(s))
                text = text.Replace(s, "***", StringComparison.Ordinal);
        }
        return text;
    }
}
