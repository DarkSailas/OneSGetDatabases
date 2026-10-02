using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OneSGetDatabases.Core.Helpers;
using OneSGetDatabases.Core.Interfaces;
using OneSGetDatabases.Core.Models;

namespace OneSGetDatabases.Core.Services;

/// <summary>
/// Находит веб-публикации 1С на IIS: читает applicationHost.config и default.vrd приложений
/// через административную шару (\\host\C$) и сопоставляет их с базами по Srvr/Ref строки подключения.
/// </summary>
public partial class IisPublicationService : IIisPublicationService
{
    private readonly IisPublicationsConfig _config;
    private readonly ILogger<IisPublicationService> _logger;

    public IisPublicationService(IOptions<IisPublicationsConfig> config, ILogger<IisPublicationService> logger)
    {
        _config = config.Value ?? new IisPublicationsConfig();
        _logger = logger;
    }

    public async Task AttachPublicationsAsync(IReadOnlyList<InfoBaseItem> bases, IEnumerable<string> iisHosts, CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled || bases.Count == 0) return;

        var hosts = iisHosts.Concat(_config.ExtraHosts)
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => h.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var found = new ConcurrentBag<IisPublication>();
        var timeout = TimeSpan.FromSeconds(Math.Max(5, _config.TimeoutSecondsPerHost));

        await Parallel.ForEachAsync(hosts, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken }, async (host, token) =>
        {
            try
            {
                var publications = await Task.Run(() => ReadHostPublications(host), token).WaitAsync(timeout, token);
                foreach (var p in publications) found.Add(p);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("IIS {Host}: чтение публикаций не уложилось в {Seconds} с", host, (int)timeout.TotalSeconds);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("IIS {Host}: не удалось прочитать публикации: {Message}", host, ex.Message);
            }
        });

        var matches = MatchPublications(bases, found.ToList());
        int published = 0;
        foreach (var b in bases)
        {
            var main = matches.TryGetValue(b, out var pubs)
                ? PickMainPublication(pubs.Where(p => IsNamedAfterBase(p.AppPath, b.Name) && IsOnBaseServer(p, b))
                                          .Select(p => WithConsulUrl(p, b, ResolveBaseUrl(p.Host))), b.Name)
                : null;
            b.WebPublicationDetails = main == null ? [] : [main];
            b.WebPublications = b.WebPublicationDetails.Select(p => p.Url).ToList();
            if (b.WebPublications.Count > 0) published++;
        }

        _logger.LogInformation("IIS: найдено публикаций 1С: {Publications}, сопоставлено баз: {Bases}", found.Count, published);
    }

    private List<IisPublication> ReadHostPublications(string host)
    {
        string configPath = Path.Combine($@"\\{host}\C$", _config.ApplicationHostConfigPath);
        if (!File.Exists(configPath))
        {
            // No IIS on this host (or no access to the admin share)
            return [];
        }

        var apps = ParseApplicationHost(File.ReadAllText(configPath));
        string? baseUrl = ResolveBaseUrl(host);
        string fqdn = host;
        try { fqdn = System.Net.Dns.GetHostEntry(host).HostName; } catch { }

        var result = new List<IisPublication>();
        foreach (var app in apps)
        {
            string? uncDir = ToAdminSharePath(host, ExpandIisVariables(app.PhysicalPath));
            if (uncDir == null) continue;

            string vrdPath = Path.Combine(uncDir, "default.vrd");
            if (!File.Exists(vrdPath)) continue;

            VrdInfo? vrd;
            try { vrd = ParseVrd(File.ReadAllText(vrdPath)); }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "IIS {Host}: не удалось разобрать {Vrd}", host, vrdPath);
                continue;
            }
            if (vrd == null || string.IsNullOrWhiteSpace(vrd.Ref)) continue;

            result.Add(new IisPublication
            {
                Host = host,
                SiteName = app.SiteName,
                AppPath = app.AppPath,
                PhysicalPath = app.PhysicalPath,
                AppPool = app.AppPool,
                Url = BuildUrl(baseUrl, app, fqdn),
                IbServers = vrd.Servers,
                IbRef = vrd.Ref
            });
        }

        _logger.LogDebug("IIS {Host}: приложений {Apps}, публикаций 1С {Publications}", host, apps.Count, result.Count);
        return result;
    }

    private string? ResolveBaseUrl(string host)
    {
        foreach (var (key, value) in _config.BaseUrls)
        {
            if (!string.IsNullOrWhiteSpace(value) && ServerNameHelper.IsSameServer(key, host))
                return value;
        }
        return null;
    }

    // ---------------------------------------------------------------- parsing (pure, unit-tested)

    internal sealed record IisApp(string SiteName, string AppPath, string PhysicalPath, IReadOnlyList<(string Protocol, string BindingInformation)> Bindings, string AppPool = "DefaultAppPool");

    internal sealed record VrdInfo(List<string> Servers, string Ref);

    /// <summary>Все приложения и виртуальные каталоги сайтов из applicationHost.config.</summary>
    internal static List<IisApp> ParseApplicationHost(string xml)
    {
        var doc = XDocument.Parse(xml);
        var result = new List<IisApp>();

        // applicationPool: application -> site applicationDefaults -> sites applicationDefaults -> DefaultAppPool
        string globalPool = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "sites")?
            .Elements().FirstOrDefault(e => e.Name.LocalName == "applicationDefaults")?
            .Attribute("applicationPool")?.Value ?? "DefaultAppPool";

        foreach (var site in doc.Descendants().Where(e => e.Name.LocalName == "site"))
        {
            string siteName = (string?)site.Attribute("name") ?? "";
            var bindings = site.Descendants()
                .Where(e => e.Name.LocalName == "binding")
                .Select(b => ((string?)b.Attribute("protocol") ?? "", (string?)b.Attribute("bindingInformation") ?? ""))
                .ToList();

            string sitePool = site.Elements().FirstOrDefault(e => e.Name.LocalName == "applicationDefaults")?
                .Attribute("applicationPool")?.Value ?? globalPool;

            foreach (var app in site.Elements().Where(e => e.Name.LocalName == "application"))
            {
                string appPath = (string?)app.Attribute("path") ?? "/";
                string appPool = (string?)app.Attribute("applicationPool") ?? sitePool;
                foreach (var vdir in app.Elements().Where(e => e.Name.LocalName == "virtualDirectory"))
                {
                    string vdirPath = (string?)vdir.Attribute("path") ?? "/";
                    string physical = (string?)vdir.Attribute("physicalPath") ?? "";
                    if (string.IsNullOrWhiteSpace(physical)) continue;

                    string fullPath = vdirPath == "/"
                        ? appPath
                        : appPath.TrimEnd('/') + "/" + vdirPath.TrimStart('/');
                    result.Add(new IisApp(siteName, fullPath, physical, bindings, appPool));
                }
            }
        }

        return result;
    }

    [GeneratedRegex(@"(?i)\bSrvr\s*=\s*""?([^"";]+)""?")]
    private static partial Regex SrvrRegex();

    [GeneratedRegex(@"(?i)\bRef\s*=\s*""?([^"";]+)""?")]
    private static partial Regex RefRegex();

    [GeneratedRegex(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?\.service\.consul$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ConsulDnsNameRegex();

    /// <summary>Строка подключения из default.vrd: Srvr="host:port";Ref="base";</summary>
    internal static VrdInfo? ParseVrd(string xml)
    {
        var doc = XDocument.Parse(xml);
        var point = doc.Root;
        if (point == null) return null;

        string ib = (string?)point.Attribute("ib") ?? "";
        var refMatch = RefRegex().Match(ib);
        if (!refMatch.Success) return null; // file infobase (File=...) or empty

        var servers = new List<string>();
        var srvrMatch = SrvrRegex().Match(ib);
        if (srvrMatch.Success)
        {
            servers.AddRange(srvrMatch.Groups[1].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return new VrdInfo(servers, refMatch.Groups[1].Value.Trim());
    }

    internal static string ExpandIisVariables(string path)
        => path.Replace("%SystemDrive%", "C:", StringComparison.OrdinalIgnoreCase)
               .Replace("%SystemRoot%", @"C:\Windows", StringComparison.OrdinalIgnoreCase)
               .Replace("%windir%", @"C:\Windows", StringComparison.OrdinalIgnoreCase);

    internal static string? ToAdminSharePath(string host, string localPath)
    {
        if (localPath.StartsWith(@"\\", StringComparison.Ordinal)) return localPath; // already UNC
        if (localPath.Length >= 3 && char.IsLetter(localPath[0]) && localPath[1] == ':' && localPath[2] == '\\')
            return $@"\\{host}\{localPath[0]}$\{localPath[3..]}";
        return null;
    }

    /// <summary>
    /// Адрес через Consul: если у базы есть сервис app-&lt;база&gt; и он указывает на тот же сервер, где опубликована база,
    /// хост в ссылке заменяется на &lt;сервис&gt;.service.consul (схема, порт и путь сохраняются).
    /// Ссылки с собственным заголовком узла сайта IIS не трогаются: по имени Consul такой сайт не ответит.
    /// Сервис с «_» или другими недопустимыми для DNS символами (app-UTD_Test) Consul по DNS не отдаёт:
    /// для такой базы остаётся адрес сервера из BaseUrls или привязки IIS.
    /// </summary>
    internal static IisPublication WithConsulUrl(IisPublication pub, InfoBaseItem item, string? hostBaseUrl)
    {
        if (!ConsulDnsNameRegex().IsMatch(item.Consul)) return pub;
        if (!ServerNameHelper.IsSameServer(item.ConsulHost, pub.Host)) return pub;
        if (!Uri.TryCreate(pub.Url, UriKind.Absolute, out var uri)) return pub;

        bool directHost = ServerNameHelper.IsSameServer(uri.Host, pub.Host);
        bool fromBaseUrl = hostBaseUrl != null && Uri.TryCreate(hostBaseUrl, UriKind.Absolute, out var baseUri)
            && baseUri.Host.Equals(uri.Host, StringComparison.OrdinalIgnoreCase);
        if (!directHost && !fromBaseUrl) return pub;

        var builder = new UriBuilder(uri) { Host = item.Consul.ToLowerInvariant() };
        return pub with { Url = builder.Uri.AbsoluteUri };
    }

    /// <summary>
    /// Основная публикация базы называется так же, как база (регистр не важен). Прочие публикации
    /// той же базы (/HRMCorp25_, /DocMng_Web) служебные и не показываются.
    /// </summary>
    internal static bool IsNamedAfterBase(string appPath, string baseName)
    {
        string name = appPath.TrimEnd('/');
        name = name[(name.LastIndexOf('/') + 1)..];
        return name.Length > 0 && name.Equals(baseName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Публикация базы должна стоять на сервере её кластера или на сервере, куда указывает её сервис Consul.
    /// Публикации той же базы на других серверах IIS (старые копии, чужой кластер через псевдоним в Srvr) не показываются.
    /// </summary>
    internal static bool IsOnBaseServer(IisPublication pub, InfoBaseItem item) =>
        ServerNameHelper.IsSameServer(pub.Host, ClusterHost(item.Cluster))
        || ServerNameHelper.IsSameServer(pub.Host, item.ConsulHost);

    /// <summary>
    /// У базы показывается одна публикация: адрес через Consul важнее прямого имени сервера,
    /// затем имя приложения в том же регистре, что и имя базы.
    /// </summary>
    internal static IisPublication? PickMainPublication(IEnumerable<IisPublication> publications, string baseName = "") =>
        publications
            .OrderByDescending(p => Uri.TryCreate(p.Url, UriKind.Absolute, out var uri)
                                    && uri.Host.EndsWith(".service.consul", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(p => baseName.Length > 0 && p.AppPath.TrimEnd('/').EndsWith("/" + baseName, StringComparison.Ordinal))
            .FirstOrDefault();

    internal static string BuildUrl(string? baseUrl, IisApp app, string fqdn)
    {
        string path = app.AppPath.Replace(" ", "%20");
        if (!string.IsNullOrWhiteSpace(baseUrl))
            return baseUrl.TrimEnd('/') + path;

        // Prefer http as the published links use it; fall back to https
        var binding = app.Bindings.FirstOrDefault(b => b.Protocol.Equals("http", StringComparison.OrdinalIgnoreCase));
        if (binding == default)
            binding = app.Bindings.FirstOrDefault(b => b.Protocol.Equals("https", StringComparison.OrdinalIgnoreCase));
        string scheme = binding == default ? "http" : binding.Protocol.ToLowerInvariant();

        string hostName = fqdn;
        string portPart = "";
        if (binding != default)
        {
            // bindingInformation = "ip:port:hostheader"
            var parts = binding.BindingInformation.Split(':');
            if (parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2])) hostName = parts[2];
            if (parts.Length >= 2 && int.TryParse(parts[1], out int port)
                && !(scheme == "http" && port == 80) && !(scheme == "https" && port == 443))
            {
                portPart = ":" + port;
            }
        }

        return $"{scheme}://{hostName}{portPart}{path}";
    }

    /// <summary>
    /// Сопоставляет публикации с базами: имя базы (Ref) и хост кластера из Srvr.
    /// При нескольких кандидатах уточняет по порту (порт агента или его regport = порт + 1).
    /// Если хост в Srvr — псевдоним (например, имя Consul), допускается совпадение только по уникальному имени базы.
    /// </summary>
    internal static Dictionary<InfoBaseItem, List<IisPublication>> MatchPublications(IReadOnlyList<InfoBaseItem> bases, IReadOnlyList<IisPublication> publications)
    {
        var result = new Dictionary<InfoBaseItem, List<IisPublication>>(ReferenceEqualityComparer.Instance);

        foreach (var pub in publications)
        {
            var byName = bases.Where(b => b.Name.Equals(pub.IbRef, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byName.Count == 0) continue;

            var servers = pub.IbServers.Select(SplitHostPort).ToList();
            var byHost = byName.Where(b => servers.Any(s => ServerNameHelper.IsSameServer(s.Host, ClusterHost(b.Cluster)))).ToList();

            List<InfoBaseItem> candidates;
            if (byHost.Count > 0) candidates = byHost;
            else if (byName.Count == 1) candidates = byName;
            else candidates = [];

            if (candidates.Count > 1)
            {
                var byPort = candidates.Where(b =>
                {
                    int agentPort = ClusterPort(b.Cluster);
                    return servers.Any(s => s.Port == agentPort || s.Port == agentPort + 1);
                }).ToList();
                if (byPort.Count > 0) candidates = byPort;
            }

            foreach (var b in candidates)
            {
                if (!result.TryGetValue(b, out var pubs))
                {
                    pubs = [];
                    result[b] = pubs;
                }
                if (!pubs.Any(p => p.Url.Equals(pub.Url, StringComparison.OrdinalIgnoreCase)))
                    pubs.Add(pub);
            }
        }

        return result;
    }

    private static (string Host, int Port) SplitHostPort(string server)
    {
        int idx = server.LastIndexOf(':');
        if (idx > 0 && int.TryParse(server[(idx + 1)..], out int port))
            return (server[..idx], port);
        return (server, 1541); // default cluster manager port of an agent on 1540
    }

    private static string ClusterHost(string cluster) => cluster.Split(':')[0];

    private static int ClusterPort(string cluster)
        => cluster.Contains(':') && int.TryParse(cluster.Split(':')[1], out int p) ? p : 1540;
}
