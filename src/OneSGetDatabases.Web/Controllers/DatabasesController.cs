using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OneSGetDatabases.Core.Helpers;
using OneSGetDatabases.Core.Interfaces;
using OneSGetDatabases.Core.Models;

namespace OneSGetDatabases.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DatabasesController : ControllerBase
{
    private readonly IDatabaseCacheService _cache;
    private readonly IDbmsInspectorService _dbmsInspector;
    private readonly IInfobaseDiscoveryService _discoveryService;
    private readonly IOptions<UiConfig> _uiConfig;

    public DatabasesController(
        IDatabaseCacheService cache,
        IDbmsInspectorService dbmsInspector,
        IInfobaseDiscoveryService discoveryService,
        IOptions<UiConfig> uiConfig)
    {
        _cache = cache;
        _dbmsInspector = dbmsInspector;
        _discoveryService = discoveryService;
        _uiConfig = uiConfig;
    }

    [HttpGet("config")]
    public ActionResult<object> GetConfig()
    {
        var location = typeof(DatabasesController).Assembly.Location;
        var buildTime = !string.IsNullOrEmpty(location) && System.IO.File.Exists(location)
            ? System.IO.File.GetLastWriteTime(location)
            : DateTime.Now;

        return Ok(new
        {
            ShowMetrics = _uiConfig.Value.ShowMetrics,
            BuildDate = buildTime.ToString("dd.MM.yyyy")
        });
    }

    [HttpGet]
    public ActionResult<object> GetDatabases(
        [FromQuery] string? environment = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sqlServer = null,
        [FromQuery] string? platform = null,
        [FromQuery] string? cluster = null,
        [FromQuery] string? sortBy = "cluster",
        [FromQuery] string? sortDir = "asc",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 500)
    {
        var items = _cache.GetAll().AsEnumerable();

        if (!string.IsNullOrWhiteSpace(environment) && !environment.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var envParts = environment.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (envParts.Length > 0 && !envParts.Contains("ALL", StringComparer.OrdinalIgnoreCase))
            {
                items = items.Where(b => envParts.Any(e => e.Equals(b.Environment, StringComparison.OrdinalIgnoreCase)));
            }
        }

        if (!string.IsNullOrWhiteSpace(cluster) && !cluster.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var clusterParts = cluster.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (clusterParts.Length > 0)
            {
                items = items.Where(b => clusterParts.Any(c => c.Equals(b.Cluster, StringComparison.OrdinalIgnoreCase)));
            }
        }

        if (!string.IsNullOrWhiteSpace(sqlServer) && !sqlServer.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var sqlParts = sqlServer.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (sqlParts.Length > 0)
            {
                items = items.Where(b => sqlParts.Any(s => ServerNameHelper.IsSameServer(b.SQL, s)));
            }
        }

        if (!string.IsNullOrWhiteSpace(platform) && !platform.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var platParts = platform.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (platParts.Length > 0)
            {
                items = items.Where(b => platParts.Any(p => b.Platform.Contains(p, StringComparison.OrdinalIgnoreCase)));
            }
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            items = items.Where(b =>
                b.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.AccessGroup.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.SQLDbName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.Cluster.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.SQL.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.Consul.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        bool desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);

        items = (sortBy?.ToLowerInvariant()) switch
        {
            "env" or "environment" => desc ? items.OrderByDescending(b => ServerNameHelper.GetEnvironmentPriority(b.Environment)).ThenBy(b => ServerNameHelper.GetClusterSortKey(b.Cluster)).ThenBy(b => b.Name)
                                           : items.OrderBy(b => ServerNameHelper.GetEnvironmentPriority(b.Environment)).ThenBy(b => ServerNameHelper.GetClusterSortKey(b.Cluster)).ThenBy(b => b.Name),
            "name" => desc ? items.OrderByDescending(b => b.Name)
                           : items.OrderBy(b => b.Name),
            "description" or "desc" => desc ? items.OrderByDescending(b => b.Description).ThenBy(b => b.Name)
                                            : items.OrderBy(b => b.Description).ThenBy(b => b.Name),
            "cluster" or "default" => desc ? items.OrderByDescending(b => ServerNameHelper.GetEnvironmentPriority(b.Environment)).ThenByDescending(b => ServerNameHelper.GetClusterSortKey(b.Cluster)).ThenByDescending(b => b.Name)
                                           : items.OrderBy(b => ServerNameHelper.GetEnvironmentPriority(b.Environment)).ThenBy(b => ServerNameHelper.GetClusterSortKey(b.Cluster)).ThenBy(b => b.Name),
            "platform" => desc ? items.OrderByDescending(b => b.Platform).ThenBy(b => b.Name)
                               : items.OrderBy(b => b.Platform).ThenBy(b => b.Name),
            "sql" => desc ? items.OrderByDescending(b => b.SQL).ThenBy(b => b.Name)
                          : items.OrderBy(b => b.SQL).ThenBy(b => b.Name),
            "sqldbname" => desc ? items.OrderByDescending(b => b.SQLDbName).ThenBy(b => b.Name)
                                : items.OrderBy(b => b.SQLDbName).ThenBy(b => b.Name),
            "accessgroup" => desc ? items.OrderByDescending(b => b.AccessGroup).ThenBy(b => b.Name)
                                  : items.OrderBy(b => b.AccessGroup).ThenBy(b => b.Name),
            _ => desc ? items.OrderByDescending(b => ServerNameHelper.GetEnvironmentPriority(b.Environment)).ThenByDescending(b => ServerNameHelper.GetClusterSortKey(b.Cluster)).ThenByDescending(b => b.Name)
                      : items.OrderBy(b => ServerNameHelper.GetEnvironmentPriority(b.Environment)).ThenBy(b => ServerNameHelper.GetClusterSortKey(b.Cluster)).ThenBy(b => b.Name)
        };

        var total = items.Count();
        page = Math.Max(1, page);
        if (pageSize <= 0 || pageSize >= 100000)
        {
            pageSize = Math.Max(1, total);
        }
        else
        {
            pageSize = Math.Clamp(pageSize, 5, 100000);
        }

        var pagedItems = items
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Ok(new
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
            SortBy = sortBy,
            SortDir = sortDir,
            Items = pagedItems
        });
    }

    private static readonly ConcurrentDictionary<string, (DateTime CachedAt, List<DatabaseDbmsSummaryRow> Rows)> _summaryRowsCache = new(StringComparer.OrdinalIgnoreCase);
    private const string SummaryRowsCacheFileName = "dbms_summary_rows.json";

    static DatabasesController()
    {
        var saved = PersistentCacheHelper.LoadFromDisk<Dictionary<string, List<DatabaseDbmsSummaryRow>>>(SummaryRowsCacheFileName);
        if (saved != null)
        {
            foreach (var kvp in saved)
            {
                _summaryRowsCache[kvp.Key] = (DateTime.UtcNow, kvp.Value);
            }
        }
    }

    [HttpGet("files")]
    public async Task<ActionResult<object>> GetDatabaseFiles(
        [FromQuery] string? environment = null,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sqlServer = null,
        [FromQuery] string? cluster = null,
        [FromQuery] string? sortBy = "size",
        [FromQuery] string? sortDir = "desc",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 500,
        CancellationToken cancellationToken = default)
    {
        var allRows = await GetOrBuildSummaryRowsAsync(environment, cancellationToken);
        var items = allRows.AsEnumerable();

        if (string.Equals(status, "EXISTS", StringComparison.OrdinalIgnoreCase))
        {
            items = items.Where(b => b.TotalSizeBytes > 0);
        }
        else if (string.Equals(status, "MISSING", StringComparison.OrdinalIgnoreCase))
        {
            items = items.Where(b => b.TotalSizeBytes == 0);
        }

        if (!string.IsNullOrWhiteSpace(environment) && !environment.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var envParts = environment.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (envParts.Length > 0 && !envParts.Contains("ALL", StringComparer.OrdinalIgnoreCase))
            {
                items = items.Where(b => envParts.Any(e => e.Equals(b.Environment, StringComparison.OrdinalIgnoreCase)));
            }
        }

        if (!string.IsNullOrWhiteSpace(cluster) && !cluster.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var clusterParts = cluster.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (clusterParts.Length > 0)
            {
                items = items.Where(b => clusterParts.Any(c => c.Equals(b.Cluster, StringComparison.OrdinalIgnoreCase)));
            }
        }

        if (!string.IsNullOrWhiteSpace(sqlServer) && !sqlServer.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var sqlParts = sqlServer.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (sqlParts.Length > 0)
            {
                items = items.Where(b => sqlParts.Any(s => ServerNameHelper.IsSameServer(b.SqlServer, s)));
            }
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            items = items.Where(b =>
                b.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.SqlDbName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.DataFilesPath.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.LogFilesPath.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.Cluster.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.SqlServer.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        bool desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);

        items = (sortBy?.ToLowerInvariant()) switch
        {
            "env" or "environment" => desc ? items.OrderByDescending(b => ServerNameHelper.GetEnvironmentPriority(b.Environment)).ThenByDescending(b => b.TotalSizeBytes)
                                           : items.OrderBy(b => ServerNameHelper.GetEnvironmentPriority(b.Environment)).ThenBy(b => b.TotalSizeBytes),
            "name" => desc ? items.OrderByDescending(b => b.Name).ThenByDescending(b => b.TotalSizeBytes)
                           : items.OrderBy(b => b.Name).ThenBy(b => b.TotalSizeBytes),
            "cluster" => desc ? items.OrderByDescending(b => ServerNameHelper.GetClusterSortKey(b.Cluster)).ThenByDescending(b => b.TotalSizeBytes)
                              : items.OrderBy(b => ServerNameHelper.GetClusterSortKey(b.Cluster)).ThenBy(b => b.TotalSizeBytes),
            "sql" or "sqlserver" => desc ? items.OrderByDescending(b => b.SqlServer).ThenByDescending(b => b.TotalSizeBytes)
                                         : items.OrderBy(b => b.SqlServer).ThenBy(b => b.TotalSizeBytes),
            "sqldbname" => desc ? items.OrderByDescending(b => b.SqlDbName).ThenByDescending(b => b.TotalSizeBytes)
                                : items.OrderBy(b => b.SqlDbName).ThenBy(b => b.TotalSizeBytes),
            "data" or "datafiles" => desc ? items.OrderByDescending(b => b.DataFilesPath)
                                          : items.OrderBy(b => b.DataFilesPath),
            "log" or "logfiles" => desc ? items.OrderByDescending(b => b.LogFilesPath)
                                        : items.OrderBy(b => b.LogFilesPath),
            _ => desc ? items.OrderByDescending(b => b.TotalSizeBytes).ThenBy(b => b.Name)
                      : items.OrderBy(b => b.TotalSizeBytes).ThenBy(b => b.Name)
        };

        var total = items.Count();
        page = Math.Max(1, page);
        if (pageSize <= 0 || pageSize >= 100000)
        {
            pageSize = Math.Max(1, total);
        }
        else
        {
            pageSize = Math.Clamp(pageSize, 5, 100000);
        }

        var pagedItems = items
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Ok(new
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
            SortBy = sortBy,
            SortDir = sortDir,
            Items = pagedItems
        });
    }

    private async Task<List<DatabaseDbmsSummaryRow>> GetOrBuildSummaryRowsAsync(string? environment, CancellationToken cancellationToken)
    {
        string envKey = string.IsNullOrWhiteSpace(environment) ? "ALL" : environment.ToUpperInvariant();

        if (_summaryRowsCache.TryGetValue(envKey, out var cached) && (DateTime.UtcNow - cached.CachedAt).TotalMinutes < 15 && cached.Rows.Count > 0)
        {
            return cached.Rows;
        }

        var allBases = _cache.GetAll();
        var baseList = allBases.AsEnumerable();
        if (!envKey.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            baseList = baseList.Where(b => b.Environment.Equals(envKey, StringComparison.OrdinalIgnoreCase));
        }

        var list = baseList.ToList();
        var uniqueSqlServers = list
            .Select(b => b.SQL)
            .Where(s => !string.IsNullOrEmpty(s) && !s.Equals("Неизвестно", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var serverFileTasks = uniqueSqlServers.ToDictionary(
            s => s,
            s => _dbmsInspector.GetServerAllDatabaseFilesAsync(s, cancellationToken),
            StringComparer.OrdinalIgnoreCase
        );

        await Task.WhenAll(serverFileTasks.Values);

        var allRows = new List<DatabaseDbmsSummaryRow>();
        foreach (var b in list)
        {
            if (string.IsNullOrEmpty(b.SQL) || b.SQL.Equals("Неизвестно", StringComparison.OrdinalIgnoreCase)) continue;

            List<DbmsFileItem>? files = null;
            if (serverFileTasks.TryGetValue(b.SQL, out var task) && task.IsCompletedSuccessfully)
            {
                task.Result.TryGetValue(b.SQLDbName, out files);
            }

            if (files == null)
            {
                var matchedTask = serverFileTasks.FirstOrDefault(kvp => ServerNameHelper.IsSameServer(kvp.Key, b.SQL)).Value;
                if (matchedTask != null && matchedTask.IsCompletedSuccessfully)
                {
                    matchedTask.Result.TryGetValue(b.SQLDbName, out files);
                }
            }

            if (files == null && b.DbmsDetails?.Files != null && b.DbmsDetails.Files.Count > 0)
            {
                files = b.DbmsDetails.Files;
            }

            if (files != null && files.Count > 0)
            {
                var dataFiles = files.Where(f => !f.FileType.Equals("LOG", StringComparison.OrdinalIgnoreCase)).ToList();
                var logFiles = files.Where(f => f.FileType.Equals("LOG", StringComparison.OrdinalIgnoreCase)).ToList();

                string dataPaths = string.Join("; ", dataFiles.Select(f => f.PhysicalPath).Where(p => !string.IsNullOrEmpty(p)));
                string logPaths = string.Join("; ", logFiles.Select(f => f.PhysicalPath).Where(p => !string.IsNullOrEmpty(p)));

                allRows.Add(new DatabaseDbmsSummaryRow
                {
                    Environment = b.Environment,
                    Name = b.Name,
                    Cluster = b.Cluster,
                    SqlServer = b.SQL,
                    SqlDbName = b.SQLDbName,
                    TotalSizeBytes = files.Sum(f => f.SizeBytes),
                    DataFilesPath = !string.IsNullOrEmpty(dataPaths) ? dataPaths : "—",
                    LogFilesPath = !string.IsNullOrEmpty(logPaths) ? logPaths : "—"
                });
            }
            else
            {
                allRows.Add(new DatabaseDbmsSummaryRow
                {
                    Environment = b.Environment,
                    Name = b.Name,
                    Cluster = b.Cluster,
                    SqlServer = b.SQL,
                    SqlDbName = b.SQLDbName,
                    TotalSizeBytes = 0,
                    DataFilesPath = "—",
                    LogFilesPath = "—"
                });
            }
        }

        if (allRows.Count > 0)
        {
            _summaryRowsCache[envKey] = (DateTime.UtcNow, allRows);
            var snapshot = _summaryRowsCache.ToDictionary(k => k.Key, v => v.Value.Rows, StringComparer.OrdinalIgnoreCase);
            PersistentCacheHelper.SaveToDisk(SummaryRowsCacheFileName, snapshot);
        }

        return allRows;
    }

    [HttpGet("details")]
    public async Task<ActionResult<DbmsDetails>> GetDatabaseDetailsQuery(
        [FromQuery] string environment,
        [FromQuery] string? cluster,
        [FromQuery] string name,
        CancellationToken cancellationToken)
    {
        var item = _cache.Find(environment, cluster ?? "", name);
        if (item == null)
        {
            item = _cache.GetAll().FirstOrDefault(b => 
                b.Environment.Equals(environment, StringComparison.OrdinalIgnoreCase) && 
                b.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(cluster) || b.Cluster.Contains(cluster, StringComparison.OrdinalIgnoreCase) || cluster.Contains(b.Cluster, StringComparison.OrdinalIgnoreCase)));
        }

        if (item == null)
        {
            return NotFound(new { Message = $"База данных '{name}' ({environment}) не найдена в кэше" });
        }

        var details = await _dbmsInspector.InspectDatabaseAsync(item.SQL, item.SQLDbName, cancellationToken: cancellationToken);
        item.DbmsDetails = details;

        return Ok(details);
    }

    [HttpGet("{environment}/{cluster}/{name}/details")]
    public async Task<ActionResult<DbmsDetails>> GetDatabaseDetails(
        string environment,
        string cluster,
        string name,
        CancellationToken cancellationToken)
    {
        var item = _cache.Find(environment, cluster, name);
        if (item == null)
        {
            item = _cache.GetAll().FirstOrDefault(b => 
                b.Environment.Equals(environment, StringComparison.OrdinalIgnoreCase) && 
                b.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        if (item == null)
        {
            return NotFound(new { Message = $"База данных '{name}' в кластере '{cluster}' ({environment}) не найдена в кэше" });
        }

        var details = await _dbmsInspector.InspectDatabaseAsync(item.SQL, item.SQLDbName, cancellationToken: cancellationToken);
        item.DbmsDetails = details;

        return Ok(details);
    }

    [HttpGet("stats")]
    public ActionResult<object> GetStats()
    {
        var all = _cache.GetAll();
        var dev = _cache.GetDev();
        var prod = _cache.GetProd();

        var clusters = all.Select(b => b.Cluster).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var sqlServers = all
            .Select(b => ServerNameHelper.NormalizeServerName(b.SQL))
            .Where(s => !string.IsNullOrEmpty(s) && !s.Equals("неизвестно", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var withAccessGroup = all.Count(b => b.AccessGroup != "Отсутствует");

        bool hasPostgres = all.Any(b => 
            (b.DbmsDetails != null && b.DbmsDetails.DbmsType != null && b.DbmsDetails.DbmsType.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase)) ||
            (b.SQL != null && (b.SQL.Contains(":5432") || b.SQL.Contains("pg", StringComparison.OrdinalIgnoreCase))));
        bool hasMsSql = all.Any(b => 
            !string.IsNullOrWhiteSpace(b.SQL) && 
            !b.SQL.Equals("неизвестно", StringComparison.OrdinalIgnoreCase) &&
            !(b.SQL.Contains(":5432") || b.SQL.Contains("pg", StringComparison.OrdinalIgnoreCase)));

        string dbmsSubtitle;
        if (hasMsSql && hasPostgres)
        {
            dbmsSubtitle = "MS SQL & PostgreSQL";
        }
        else if (hasPostgres)
        {
            dbmsSubtitle = "PostgreSQL";
        }
        else if (hasMsSql)
        {
            dbmsSubtitle = "MS SQL Server";
        }
        else
        {
            dbmsSubtitle = "СУБД";
        }

        var healthList = _discoveryService.GetClusterHealthStatuses();
        int totalClusters = healthList.Count > 0 ? healthList.Count : clusters;

        return Ok(new
        {
            TotalDatabases = all.Count,
            ProdDatabases = prod.Count,
            DevDatabases = dev.Count,
            UniqueClusters = clusters,
            TotalClusters = totalClusters,
            UniqueSqlServers = sqlServers,
            SqlServersSubtitle = dbmsSubtitle,
            WithAccessGroupCount = withAccessGroup,
            AccessGroupCoveragePercent = all.Count > 0 ? Math.Round((withAccessGroup / (double)all.Count) * 100, 1) : 0,
            LastScanTime = _cache.LastScanTime,
            LastConfluenceSyncTime = _cache.LastConfluenceSyncTime
        });
    }

        [HttpGet("clusters/health")]
    public ActionResult<object> GetClustersHealth()
    {
        var healthList = _discoveryService.GetClusterHealthStatuses();
        int total = healthList.Count;
        int online = healthList.Count(h => h.Status == ClusterStatus.Online);
        int empty = healthList.Count(h => h.Status == ClusterStatus.Empty);
        int offline = healthList.Count(h => h.Status == ClusterStatus.Offline);
        int errors = healthList.Count(h => h.Status == ClusterStatus.Error || h.Status == ClusterStatus.AuthError);

        var sorted = healthList
            .OrderBy(h => ServerNameHelper.GetEnvironmentPriority(h.Environment))
            .ThenBy(h => ServerNameHelper.GetClusterSortKey(h.Server))
            .Select(h => new
            {
                Server = h.Server,
                Host = h.Host,
                ServerPort = h.ServerPort,
                RasPort = h.RasPort,
                RasAddress = h.RasPort > 0 ? (string.IsNullOrEmpty(h.RasAddress) ? $"{h.Host}:{h.RasPort}" : h.RasAddress) : "—",
                Environment = h.Environment,
                Status = h.Status.ToString(),
                IsRasPortOpen = h.IsRasPortOpen,
                PlatformVersion = h.PlatformVersion ?? "—",
                DatabasesCount = h.DatabasesCount,
                CimStatus = h.CimStatus ?? "—",
                ErrorMessage = h.ErrorMessage,
                LastCheckedAt = h.LastCheckedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss")
            });

        var logs = _discoveryService.GetDiscoveryLogs()
            .Select(l => new
            {
                Timestamp = l.Timestamp.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
                Server = l.Server,
                Host = l.Host,
                Port = l.Port,
                Environment = l.Environment,
                Stage = l.Stage,
                Level = l.Level,
                Message = l.Message,
                Details = l.Details
            });

        return Ok(new
        {
            Total = total,
            Online = online,
            Empty = empty,
            Offline = offline,
            Errors = errors,
            Clusters = sorted,
            Logs = logs
        });
    }

    [HttpGet("clusters/logs")]
    public ActionResult<object> GetClusterLogs()
    {
        var logs = _discoveryService.GetDiscoveryLogs()
            .Select(l => new
            {
                Timestamp = l.Timestamp.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
                Server = l.Server,
                Host = l.Host,
                Port = l.Port,
                Environment = l.Environment,
                Stage = l.Stage,
                Level = l.Level,
                Message = l.Message,
                Details = l.Details
            });

        return Ok(logs);
    }

    [HttpGet("filters")]
    public ActionResult<object> GetFilterOptions()
    {
        var all = _cache.GetAll();
        return Ok(new
        {
            Environments = new[] { "ALL", "PROD", "DEV" },
            SqlServers = all
                .Select(b => ServerNameHelper.NormalizeServerName(b.SQL))
                .Where(s => !string.IsNullOrEmpty(s) && !s.Equals("неизвестно", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Clusters = all
                .Select(b => b.Cluster?.Trim())
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Platforms = all
                .Select(b => b.Platform?.Trim())
                .Where(p => !string.IsNullOrEmpty(p) && !p.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList()
        });
    }
}
