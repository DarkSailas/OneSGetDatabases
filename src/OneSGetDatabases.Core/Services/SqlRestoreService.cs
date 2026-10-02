using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OneSGetDatabases.Core.Interfaces;
using OneSGetDatabases.Core.Models;
using static OneSGetDatabases.Core.Services.RestoreChainPlanner;

namespace OneSGetDatabases.Core.Services;

public partial class SqlRestoreService : ISqlRestoreService
{
    private readonly SqlRestoreConfig _config;
    private readonly DbmsConnectionConfig _dbmsConfig;
    private readonly IAuditLogService _auditLogService;
    private readonly IInfobaseJobsService _jobsService;
    private readonly ILogger<SqlRestoreService> _logger;

    private readonly ConcurrentDictionary<string, RestoreOperationState> _operations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _startLock = new();

    private IReadOnlyList<BackupCatalogSource>? _cachedSources;
    private DateTime _sourcesCacheExpiry = DateTime.MinValue;
    private readonly SemaphoreSlim _sourcesLock = new(1, 1);

    public SqlRestoreService(
        IOptions<SqlRestoreConfig> config,
        IOptions<DbmsConnectionConfig> dbmsConfig,
        IAuditLogService auditLogService,
        IInfobaseJobsService jobsService,
        ILogger<SqlRestoreService> logger)
    {
        _config = config.Value;
        _dbmsConfig = dbmsConfig.Value;
        _auditLogService = auditLogService;
        _jobsService = jobsService;
        _logger = logger;
    }

    [GeneratedRegex(@"(_\d+_of_\d+|_part\d+|_\d{1,2})(?=\.(bak|diff|dif|trn)$)", RegexOptions.IgnoreCase)]
    private static partial Regex StripFamilyRegex();

    private static readonly HashSet<string> DefaultSystemDatabases = new(StringComparer.OrdinalIgnoreCase)
    {
        "master", "model", "msdb", "tempdb", "distribution", "susdb", "dbadb", "dbasqlperformance",
        "reportserver", "reportservertempdb"
    };

    private static readonly HashSet<string> BackupExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bak", ".diff", ".dif", ".trn"
    };

    public bool IsCatalogExcluded(string catalogName)
    {
        if (string.IsNullOrWhiteSpace(catalogName)) return false;
        if (_config.ExcludedCatalogs != null)
        {
            foreach (var pat in _config.ExcludedCatalogs)
            {
                if (string.IsNullOrWhiteSpace(pat)) continue;
                if (MatchesPattern(pat, catalogName)) return true;
            }
        }
        return false;
    }

    public bool IsDatabaseExcluded(string dbName)
    {
        if (string.IsNullOrWhiteSpace(dbName)) return false;
        if (DefaultSystemDatabases.Contains(dbName)) return true;
        if (dbName.StartsWith("ReportServer", StringComparison.OrdinalIgnoreCase)) return true;
        if (_config.ExcludedDatabases != null)
        {
            foreach (var pat in _config.ExcludedDatabases)
            {
                if (string.IsNullOrWhiteSpace(pat)) continue;
                if (MatchesPattern(pat, dbName)) return true;
            }
        }
        return false;
    }

    public static bool MatchesPattern(string pattern, string value)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(value))
            return false;

        pattern = pattern.Trim();
        value = value.Trim();

        if (string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase))
            return true;

        if (pattern.Contains('*') || pattern.Contains('?'))
        {
            try
            {
                string regexPattern = "^" + Regex.Escape(pattern)
                    .Replace(@"\*", ".*")
                    .Replace(@"\?", ".") + "$";
                return Regex.IsMatch(value, regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch
            {
                return System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, value, ignoreCase: true);
            }
        }

        return false;
    }

    public IReadOnlyList<string> GetExcludedDatabases() => _config.ExcludedDatabases ?? [];
    public IReadOnlyList<string> GetExcludedCatalogs() => _config.ExcludedCatalogs ?? [];

    public async ValueTask<IReadOnlyList<BackupCatalogSource>> GetBackupSourcesAsync(CancellationToken ct = default)
    {
        if (_cachedSources != null && DateTime.UtcNow < _sourcesCacheExpiry)
        {
            return _cachedSources;
        }

        await _sourcesLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cachedSources != null && DateTime.UtcNow < _sourcesCacheExpiry)
            {
                return _cachedSources;
            }

            var sources = await Task.Run(() =>
            {
                var list = new List<BackupCatalogSource>();
                string root = _config.BackupArchiveRoot;

                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    _logger.LogWarning("Каталог архива бэкапов недоступен: {Root}", root);
                    return list;
                }

                try
                {
                    var rootDir = new DirectoryInfo(root);
                    foreach (var serverDir in rootDir.EnumerateDirectories())
                    {
                        ct.ThrowIfCancellationRequested();
                        if (serverDir.Name.StartsWith('$') || serverDir.Name.StartsWith('.'))
                            continue;

                        if (IsCatalogExcluded(serverDir.Name))
                            continue;

                        try
                        {
                            foreach (var dbDir in serverDir.EnumerateDirectories())
                            {
                                if (dbDir.Name.StartsWith('$') || dbDir.Name.StartsWith('.'))
                                    continue;

                                if (IsDatabaseExcluded(dbDir.Name))
                                    continue;

                                // Catalogs without any backup file are useless as a restore source
                                if (!ContainsBackupFiles(dbDir))
                                    continue;

                                list.Add(new BackupCatalogSource
                                {
                                    Server = serverDir.Name,
                                    Database = dbDir.Name,
                                    DisplayName = $"{serverDir.Name} / {dbDir.Name}",
                                    FullPath = dbDir.FullName
                                });
                            }
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.LogWarning(ex, "Ошибка чтения папки сервера бэкапов: {ServerDir}", serverDir.FullName);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Ошибка перечисления корня архива бэкапов: {Root}", root);
                }

                return list.OrderBy(s => s.Server).ThenBy(s => s.Database).ToList();
            }, ct).ConfigureAwait(false);

            _cachedSources = sources;
            _sourcesCacheExpiry = DateTime.UtcNow.AddMinutes(Math.Max(5, _config.CacheDurationMinutes));
            return sources;
        }
        finally
        {
            _sourcesLock.Release();
        }
    }

    public async ValueTask<IReadOnlyList<RestoreTimelinePoint>> GetTimelinePointsAsync(string sourceServer, string sourceDatabase, CancellationToken ct = default)
    {
        if (!IsSafePathSegment(sourceServer) || !IsSafePathSegment(sourceDatabase))
            return [];

        if (IsCatalogExcluded(sourceServer) || IsDatabaseExcluded(sourceDatabase))
            return [];

        return await Task.Run(() =>
        {
            string dbPath = Path.Combine(_config.BackupArchiveRoot, sourceServer, sourceDatabase);
            if (!Directory.Exists(dbPath))
            {
                _logger.LogWarning("Каталог бэкапов базы не найден: {Path}", dbPath);
                return (IReadOnlyList<RestoreTimelinePoint>)[];
            }

            // Flat layout (SERVER\DB\*.bak), Ola Hallengren layout (SERVER\DB\FULL|DIFF|LOG\*.bak|*.trn)
            // and nested date folders (SERVER\DB\FULL\2026-09\*.bak)
            var enumOptions = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = MaxBackupSearchDepth,
                IgnoreInaccessible = true
            };

            var dirInfo = new DirectoryInfo(dbPath);
            var fileInfos = dirInfo.EnumerateFiles("*", enumOptions)
                .Where(f => BackupExtensions.Contains(f.Extension))
                .Select(f =>
                {
                    string? typeFolder = FindTypeFolder(dirInfo.FullName, f.DirectoryName);
                    return new BackupFileInfo
                    {
                        FileName = f.Name,
                        FullPath = f.FullName,
                        BackupDate = f.LastWriteTime,
                        Type = ClassifyBackupFile(f.Name, typeFolder),
                        SizeBytes = f.Length,
                        ServerName = sourceServer,
                        DatabaseName = sourceDatabase
                    };
                })
                .ToList();

            if (fileInfos.Count == 0)
            {
                _logger.LogWarning("В каталоге {Path} не найдено файлов бэкапа (.bak/.diff/.dif/.trn) на глубине до {Depth} уровней (учетная запись {Account})",
                    dbPath, MaxBackupSearchDepth, $"{Environment.UserDomainName}\\{Environment.UserName}");
                return [];
            }

            // Striped backups (several files of one media set) are grouped into one point:
            // same name without the stripe suffix and same type, finished within a short window
            var stripeWindow = TimeSpan.FromMinutes(10);
            var groups = new List<List<BackupFileInfo>>();
            foreach (var byName in fileInfos.GroupBy(f => $"{StripFamilyRegex().Replace(f.FileName, "")}|{f.Type}", StringComparer.OrdinalIgnoreCase))
            {
                List<BackupFileInfo>? current = null;
                foreach (var f in byName.OrderBy(f => f.BackupDate))
                {
                    if (current == null || f.BackupDate - current[0].BackupDate > stripeWindow)
                    {
                        current = [];
                        groups.Add(current);
                    }
                    current.Add(f);
                }
            }
            groups = groups.OrderByDescending(g => g.Max(f => f.BackupDate)).ToList();

            var points = new List<RestoreTimelinePoint>();
            foreach (var g in groups)
            {
                var sample = g.First();
                var maxDate = g.Max(f => f.BackupDate);
                var totalBytes = g.Sum(f => f.SizeBytes);
                var filePaths = g.OrderBy(f => f.FileName).Select(f => f.FullPath).ToList();

                string sizeDisplay = FormatBytes(totalBytes);
                string typeRu = sample.Type switch
                {
                    BackupType.Full => "Полный",
                    BackupType.Differential => "Разностный",
                    BackupType.Log => "Журнал транзакций",
                    _ => "Бэкап"
                };

                string pointId = $"{sourceServer}|{sourceDatabase}|{maxDate:O}|{sample.Type}|{Path.GetFileName(filePaths[0])}";

                points.Add(new RestoreTimelinePoint
                {
                    Id = pointId,
                    Date = maxDate.ToString("yyyy-MM-dd"),
                    Time = maxDate.ToString("HH:mm:ss"),
                    Type = sample.Type,
                    SizeBytes = totalBytes,
                    SizeDisplay = sizeDisplay,
                    DisplayName = $"{maxDate:HH:mm:ss} — {typeRu} бэкап ({sizeDisplay})",
                    BackupDate = maxDate,
                    FilePaths = filePaths
                });
            }

            return (IReadOnlyList<RestoreTimelinePoint>)points;
        }, ct).ConfigureAwait(false);
    }

    public async ValueTask<RestorePlanPreview> GetRestorePlanPreviewAsync(string sourceServer, string sourceDatabase, string pointId, CancellationToken ct = default)
    {
        var points = await GetTimelinePointsAsync(sourceServer, sourceDatabase, ct).ConfigureAwait(false);
        var target = points.FirstOrDefault(p => p.Id == pointId);
        if (target == null)
            return new RestorePlanPreview { Error = "Точка восстановления не найдена в архиве. Обновите список точек." };

        RestoreChainCandidates candidates;
        try
        {
            candidates = BuildCandidates(points, target);
        }
        catch (InvalidOperationException ex)
        {
            return new RestorePlanPreview { TargetMoment = FormatMoment(target.BackupDate), Error = ex.Message };
        }

        var steps = new List<RestorePlanStep> { SingleStep(candidates.Full) };
        DateTime baseDate = candidates.Full.BackupDate;

        if (candidates.Differential != null)
        {
            steps.Add(SingleStep(candidates.Differential));
            baseDate = candidates.Differential.BackupDate;
        }

        if (target.Type == BackupType.Log)
        {
            // Display only logs after the base backup; the LSN check at start decides the exact set
            var logs = candidates.Logs
                .Where(l => l.BackupDate > baseDate || ReferenceEquals(l, target))
                .OrderBy(l => l.BackupDate)
                .ToList();
            long logBytes = logs.Sum(l => l.SizeBytes);
            steps.Add(new RestorePlanStep
            {
                Type = BackupType.Log,
                From = FormatMoment(logs[0].BackupDate),
                To = FormatMoment(logs[^1].BackupDate),
                BackupCount = logs.Count,
                FileCount = logs.Sum(l => l.FilePaths.Count),
                SizeBytes = logBytes,
                SizeDisplay = FormatBytes(logBytes)
            });
        }

        long total = steps.Sum(s => s.SizeBytes);
        return new RestorePlanPreview
        {
            TargetMoment = FormatMoment(target.BackupDate),
            Steps = steps,
            TotalFiles = steps.Sum(s => s.FileCount),
            TotalBytes = total,
            TotalDisplay = FormatBytes(total)
        };

        static RestorePlanStep SingleStep(RestoreTimelinePoint p) => new()
        {
            Type = p.Type,
            From = FormatMoment(p.BackupDate),
            To = FormatMoment(p.BackupDate),
            FileCount = p.FilePaths.Count,
            SizeBytes = p.SizeBytes,
            SizeDisplay = FormatBytes(p.SizeBytes)
        };
    }

    private static string FormatMoment(DateTime d) => d.ToString("dd.MM.yyyy HH:mm:ss");

    private const int MaxBackupSearchDepth = 3;

    /// <summary>Есть ли в каталоге базы хотя бы один файл бэкапа (та же глубина и расширения, что у поиска точек).</summary>
    internal static bool ContainsBackupFiles(DirectoryInfo dbDir)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = MaxBackupSearchDepth,
            IgnoreInaccessible = true
        };
        return dbDir.EnumerateFiles("*", options).Any(f => BackupExtensions.Contains(f.Extension));
    }
    private static readonly string[] TypeFolderNames = ["FULL", "DIFF", "LOG"];

    /// <summary>Первая папка FULL/DIFF/LOG на пути от каталога базы до файла.</summary>
    internal static string? FindTypeFolder(string dbPath, string? fileDirectory)
    {
        if (string.IsNullOrEmpty(fileDirectory)) return null;
        string relative = Path.GetRelativePath(dbPath, fileDirectory);
        if (relative == ".") return null;
        return relative
            .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(s => TypeFolderNames.Contains(s, StringComparer.OrdinalIgnoreCase));
    }

    public async ValueTask<BackupCatalogDiagnostics> DiagnoseCatalogAsync(string sourceServer, string sourceDatabase, CancellationToken ct = default)
    {
        string account = $"{Environment.UserDomainName}\\{Environment.UserName}";
        if (!IsSafePathSegment(sourceServer) || !IsSafePathSegment(sourceDatabase))
            return new BackupCatalogDiagnostics { Path = "", Account = account, Exists = false, Error = "Недопустимое имя каталога" };

        string dbPath = Path.Combine(_config.BackupArchiveRoot, sourceServer, sourceDatabase);

        return await Task.Run(() =>
        {
            var extensions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var subDirs = new List<string>();
            var denied = new List<string>();
            var samples = new List<string>();
            int totalFiles = 0;
            const int maxListed = 20;

            try
            {
                if (!Directory.Exists(dbPath))
                {
                    return new BackupCatalogDiagnostics { Path = dbPath, Account = account, Exists = false, Error = "Каталог не существует или нет доступа" };
                }

                var queue = new Queue<(string Dir, int Depth)>();
                queue.Enqueue((dbPath, 0));
                while (queue.Count > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    var (dir, depth) = queue.Dequeue();
                    try
                    {
                        foreach (var file in Directory.EnumerateFiles(dir))
                        {
                            totalFiles++;
                            string ext = Path.GetExtension(file);
                            ext = string.IsNullOrEmpty(ext) ? "(без расширения)" : ext.ToLowerInvariant();
                            extensions[ext] = extensions.GetValueOrDefault(ext) + 1;
                            if (samples.Count < 10)
                                samples.Add(Path.GetRelativePath(dbPath, file));
                        }

                        if (depth >= MaxBackupSearchDepth + 1) continue;
                        foreach (var sub in Directory.EnumerateDirectories(dir))
                        {
                            if (subDirs.Count < maxListed)
                                subDirs.Add(Path.GetRelativePath(dbPath, sub));
                            queue.Enqueue((sub, depth + 1));
                        }
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                    {
                        if (denied.Count < maxListed)
                            denied.Add($"{Path.GetRelativePath(dbPath, dir)}: {ex.Message}");
                    }
                }

                return new BackupCatalogDiagnostics
                {
                    Path = dbPath,
                    Account = account,
                    Exists = true,
                    TotalFiles = totalFiles,
                    Extensions = extensions,
                    SubDirectories = subDirs,
                    AccessErrors = denied,
                    SampleFiles = samples,
                    SearchDepth = MaxBackupSearchDepth
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new BackupCatalogDiagnostics { Path = dbPath, Account = account, Exists = false, Error = ex.Message };
            }
        }, ct).ConfigureAwait(false);
    }

    public ValueTask<RestoreOperationState> StartRestoreAsync(RestoreStartRequest request, string clientIp, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.TargetServer) || string.IsNullOrWhiteSpace(request.TargetDatabase))
            throw new ArgumentException("Целевой сервер СУБД и имя базы обязательны");

        string normTargetServer = request.TargetServer.Trim().ToLowerInvariant();
        if (normTargetServer.Contains("prod"))
        {
            string msg = $"Безопасность: сервер {request.TargetServer} содержит признак PROD. Восстановление разрешено только на серверы среды DEV/TEST.";
            LogRejection("REJECT_RESTORE_PROD", request, clientIp, msg);
            throw new InvalidOperationException(msg);
        }

        if (_config.AllowedTargetServers.Count > 0 && !IsAllowedTargetServer(request.TargetServer, _config.AllowedTargetServers, _config.AllowedDomainSuffixes))
        {
            string msg = $"Безопасность: сервер {request.TargetServer} отсутствует в белом списке разрешенных DEV-серверов.";
            LogRejection("REJECT_RESTORE_NOT_WHITELISTED", request, clientIp, msg);
            throw new InvalidOperationException(msg);
        }

        if (IsDatabaseExcluded(request.TargetDatabase))
        {
            string msg = $"Безопасность: восстановление системной базы данных '{request.TargetDatabase}' запрещено.";
            LogRejection("REJECT_RESTORE_SYSTEM_DB", request, clientIp, msg);
            throw new InvalidOperationException(msg);
        }

        if (request.TargetDatabase.Length > 128 || !IsSafePathSegment(request.TargetDatabase))
            throw new ArgumentException($"Недопустимое имя целевой базы данных '{request.TargetDatabase}'.");

        if (!IsSafePathSegment(request.SourceServer) || !IsSafePathSegment(request.SourceDatabase))
            throw new ArgumentException("Недопустимый каталог архива бэкапов.");

        if (string.IsNullOrWhiteSpace(request.PointId))
            throw new ArgumentException("Не выбрана точка восстановления.");

        RestoreOperationState state;
        lock (_startLock)
        {
            PruneFinishedOperations();

            bool alreadyRunning = _operations.Values.Any(o =>
                (o.Status == RestoreOperationStatus.Running || o.Status == RestoreOperationStatus.Queued)
                && o.TargetServer.Equals(request.TargetServer, StringComparison.OrdinalIgnoreCase)
                && o.TargetDatabase.Equals(request.TargetDatabase, StringComparison.OrdinalIgnoreCase));

            if (alreadyRunning)
                throw new InvalidOperationException($"Восстановление базы {request.TargetDatabase} на {request.TargetServer} уже выполняется.");

            string opId = Guid.NewGuid().ToString("N")[..12];
            state = new RestoreOperationState
            {
                OperationId = opId,
                TargetServer = request.TargetServer,
                TargetDatabase = request.TargetDatabase,
                SourceServer = request.SourceServer,
                SourceDatabase = request.SourceDatabase,
                InfobaseName = request.InfobaseName ?? request.TargetDatabase,
                Status = RestoreOperationStatus.Running,
                PercentComplete = 1,
                Stage = "Инициализация операции восстановления...",
                StartTime = DateTime.UtcNow
            };

            _operations[opId] = state;
        }

        _ = WriteAuditAsync(new AuditLogEntry
        {
            TimestampUtc = DateTime.UtcNow,
            Action = "START_RESTORE_DATABASE",
            Status = "QUEUED",
            Host = request.TargetServer,
            ServiceName = request.TargetDatabase,
            DisplayName = $"Восстановление базы {request.TargetDatabase}",
            ClientIp = clientIp,
            DurationMs = 0,
            ErrorMessage = $"Запуск восстановления базы {request.TargetDatabase} на {request.TargetServer} из архива {request.SourceServer}/{request.SourceDatabase}."
        });

        _ = Task.Run(() => ExecuteRestorePipelineAsync(request, state, clientIp), CancellationToken.None);

        return ValueTask.FromResult(state);
    }

    public RestoreOperationState? GetOperationStatus(string operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId)) return null;
        _operations.TryGetValue(operationId, out var state);
        return state;
    }

    public IReadOnlyList<RestoreOperationState> GetActiveOperations()
    {
        // Running operations plus the latest finished one per database (last 12 h),
        // so a failure stays visible in the table after a page reload
        var recentThreshold = DateTime.UtcNow.AddHours(-12);
        return _operations.Values
            .Where(o => o.Status == RestoreOperationStatus.Running || o.Status == RestoreOperationStatus.Queued
                        || (o.EndTime.HasValue && o.EndTime.Value >= recentThreshold))
            .GroupBy(o => $"{o.TargetServer}|{o.TargetDatabase}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(o => o.StartTime).First())
            .OrderByDescending(o => o.StartTime)
            .ToList();
    }

    private void PruneFinishedOperations()
    {
        var threshold = DateTime.UtcNow.AddHours(-24);
        foreach (var kvp in _operations)
        {
            if (kvp.Value.EndTime.HasValue && kvp.Value.EndTime.Value < threshold)
                _operations.TryRemove(kvp.Key, out _);
        }
    }

    private void LogRejection(string action, RestoreStartRequest request, string clientIp, string message)
    {
        _ = WriteAuditAsync(new AuditLogEntry
        {
            TimestampUtc = DateTime.UtcNow,
            Action = action,
            Status = "FAILED",
            Host = request.TargetServer,
            ServiceName = request.TargetDatabase,
            DisplayName = $"Отказ восстановления базы {request.TargetDatabase}",
            ClientIp = clientIp,
            DurationMs = 0,
            ErrorMessage = message
        });
    }

    private async Task WriteAuditAsync(AuditLogEntry entry)
    {
        try
        {
            await _auditLogService.LogActionAsync(entry).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось записать событие {Action} в журнал аудита", entry.Action);
        }
    }

    private sealed record RestoreStep(RestoreTimelinePoint Point, BackupHeader Header);

    private sealed record TargetMetadata(
        bool Exists,
        string StateDesc,
        string? OwnerName,
        string? DefaultDataPath,
        string? DefaultLogPath,
        List<ExistingDbFile> Files,
        List<(string UserName, string? LoginName)> Users,
        List<(string UserName, string RoleName)> RoleMembers);

    /// <summary>Диапазон процентов общего прогресса, отведённый текущему шагу RESTORE.</summary>
    private sealed class ProgressWindow
    {
        public volatile int From;
        public volatile int To;
        public volatile string Label = "";
    }

    private async Task ExecuteRestorePipelineAsync(RestoreStartRequest request, RestoreOperationState state, string clientIp)
    {
        var sw = Stopwatch.StartNew();
        string db = request.TargetDatabase;
        string qdb = QuoteIdentifier(db);
        string masterConnString = BuildConnectionString(request.TargetServer, "master");
        var warnings = new List<string>();
        var skippedUsers = new List<string>();
        var restoredUsers = new List<string>();
        int restoreTimeout = Math.Max(3600, _config.CommandTimeoutMinutes * 60);

        var connBuilder = new SqlConnectionStringBuilder(masterConnString);
        string authDesc = connBuilder.IntegratedSecurity ? "Windows Integrated Security" : $"SQL User '{connBuilder.UserID}'";
        _logger.LogInformation("Начало восстановления {TargetDb} на {TargetServer} из {SourceServer}/{SourceDb} (учетная запись: {AuthDesc})",
            db, request.TargetServer, request.SourceServer, request.SourceDatabase, authDesc);

        int filesUsed = 0;
        bool jobsDenied = false;

        try
        {
            // 1. Resolve selected point and candidate chain
            SetStage(state, "Поиск файлов бэкапа...", 2);
            var points = await GetTimelinePointsAsync(request.SourceServer, request.SourceDatabase, CancellationToken.None).ConfigureAwait(false);
            var target = points.FirstOrDefault(p => p.Id == request.PointId)
                ?? throw new InvalidOperationException("Выбранная точка восстановления не найдена в архиве (список файлов изменился). Обновите список точек и выберите её заново.");
            var candidates = BuildCandidates(points, target);

            await using var conn = new SqlConnection(masterConnString);
            await conn.OpenAsync().ConfigureAwait(false);
            int spid = Convert.ToInt32(await ExecuteScalarAsync(conn, "SELECT @@SPID;", 10).ConfigureAwait(false));

            // 2. Validate chain by backup headers (LSN)
            SetStage(state, "Проверка цепочки бэкапов (HEADERONLY)...", 4);
            var steps = await BuildRestoreStepsAsync(conn, candidates, warnings).ConfigureAwait(false);
            filesUsed = steps.Sum(s => s.Point.FilePaths.Count);

            // 3. Target database metadata: files, owner, users and roles
            SetStage(state, "Сохранение метаданных и прав целевой базы...", 7);
            var meta = await ReadTargetMetadataAsync(conn, db).ConfigureAwait(false);
            if (meta.Exists && !meta.StateDesc.Equals("ONLINE", StringComparison.OrdinalIgnoreCase) && request.RestorePermissions)
                warnings.Add($"Целевая база в состоянии {meta.StateDesc}: пользователи и роли до восстановления не прочитаны.");

            // 4. File layout of the full backup -> MOVE clauses
            SetStage(state, "Анализ структуры файлов бэкапа (FILELISTONLY)...", 9);
            var fullStep = steps[0];
            var backupFiles = await ReadFileListAsync(conn, fullStep).ConfigureAwait(false);
            var moves = PlanFileMoves(backupFiles, meta.Files, meta.DefaultDataPath, meta.DefaultLogPath, db);

            // 5. Scheduled jobs deny is a cluster-side flag: set it before production data appears in the DEV base.
            // If it cannot be set, stop here — otherwise exchanges/mailings would run on a copy of production data.
            if (request.DenyScheduledJobsIn1C)
            {
                SetStage(state, "Блокировка регламентных заданий в 1С...", 11);
                var jobs = await _jobsService.DenyScheduledJobsAsync(request.TargetServer, db, request.InfobaseName, request.ClusterHost, CancellationToken.None).ConfigureAwait(false);
                if (!jobs.Success)
                {
                    throw new InvalidOperationException(
                        $"Не удалось заблокировать регламентные задания в 1С: {jobs.Message}. База не изменялась. Снимите флажок блокировки, если восстановление без неё допустимо.");
                }
                jobsDenied = true;
            }

            // 6. Take database offline: unlike SINGLE_USER, 1C worker processes cannot grab the only connection before RESTORE
            SetStage(state, "Отключение активных соединений (OFFLINE)...", 13);
            if (meta.Exists && meta.StateDesc.Equals("ONLINE", StringComparison.OrdinalIgnoreCase))
            {
                await ExecuteNonQueryAsync(conn, $"ALTER DATABASE {qdb} SET OFFLINE WITH ROLLBACK IMMEDIATE;", 120).ConfigureAwait(false);
            }

            // 7. RESTORE chain with NORECOVERY, progress tracked by our session id
            var window = new ProgressWindow { From = 15, To = 15 };
            using var monitorCts = new CancellationTokenSource();
            var monitorTask = MonitorRestoreProgressAsync(request.TargetServer, spid, state, window, monitorCts.Token);

            try
            {
                long totalBytes = Math.Max(1, steps.Sum(s => s.Point.SizeBytes));
                long doneBytes = 0;
                const int rangeFrom = 15, rangeTo = 85;
                int logTotal = steps.Count(s => s.Point.Type == BackupType.Log);
                int logIndex = 0;

                for (int i = 0; i < steps.Count; i++)
                {
                    var step = steps[i];
                    int from = rangeFrom + (int)((rangeTo - rangeFrom) * doneBytes / totalBytes);
                    doneBytes += step.Point.SizeBytes;
                    int to = rangeFrom + (int)((rangeTo - rangeFrom) * doneBytes / totalBytes);

                    string label = step.Point.Type switch
                    {
                        BackupType.Full => "Восстановление полного бэкапа",
                        BackupType.Differential => "Восстановление разностного бэкапа",
                        _ => $"Применение журнала {++logIndex}/{logTotal}"
                    };
                    window.From = from;
                    window.To = Math.Max(from, to);
                    window.Label = label;
                    SetStage(state, $"{label} ({step.Point.Date} {step.Point.Time})...", from);

                    string sql = BuildRestoreStatement(qdb, step, i == 0 ? moves : null);
                    await ExecuteNonQueryAsync(conn, sql, restoreTimeout).ConfigureAwait(false);
                }

                window.From = window.To = 86;
                window.Label = "Перевод базы в рабочее состояние";
                SetStage(state, "Перевод базы в рабочее состояние (RECOVERY)...", 86);
                await ExecuteNonQueryAsync(conn, $"RESTORE DATABASE {qdb} WITH RECOVERY;", restoreTimeout).ConfigureAwait(false);
            }
            finally
            {
                monitorCts.Cancel();
                await monitorTask.ConfigureAwait(false);
            }

            state.EstimatedSecondsRemaining = null;

            // 8. Data is already replaced: post-restore adjustments only produce warnings, never a failed status
            if (request.SetSimpleRecoveryAndShrink || request.RestorePermissions)
            {
                await TryStepAsync(warnings, "Настройка восстановленной базы", async () =>
                {
                    await using var dbConn = new SqlConnection(BuildConnectionString(request.TargetServer, db));
                    await dbConn.OpenAsync().ConfigureAwait(false);

                    if (request.SetSimpleRecoveryAndShrink)
                    {
                        SetStage(state, "Перевод в режим Simple и сжатие лога...", 88);
                        await TryStepAsync(warnings, "Перевод в SIMPLE / сжатие лога", () => SetSimpleAndShrinkLogAsync(dbConn, qdb)).ConfigureAwait(false);
                    }

                    if (request.RestorePermissions)
                    {
                        SetStage(state, "Восстановление владельца и прав пользователей...", 92);
                        await RestorePermissionsAsync(dbConn, qdb, meta, warnings, skippedUsers, restoredUsers).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);
            }

            SetStage(state, "Возврат базы в MULTI_USER...", 96);
            await TryStepAsync(warnings, "Возврат в MULTI_USER", () => ExecuteNonQueryAsync(conn, $@"
IF DATABASEPROPERTYEX({QuoteLiteral(db)}, 'UserAccess') <> 'MULTI_USER'
    ALTER DATABASE {qdb} SET MULTI_USER;", 60)).ConfigureAwait(false);

            // 9. Complete
            sw.Stop();
            state.Warnings = warnings;
            state.PercentComplete = 100;
            // Warnings go to the log and the audit entry only
            state.Stage = $"Успешно восстановлено за {FormatDuration(sw.Elapsed)}";
            state.Status = RestoreOperationStatus.Completed;
            state.EndTime = DateTime.UtcNow;
            state.EstimatedSecondsRemaining = 0;
            state.ElapsedSeconds = (int)sw.Elapsed.TotalSeconds;

            foreach (var w in warnings)
                _logger.LogWarning("Восстановление {TargetDb} на {TargetServer}: {Warning}", db, request.TargetServer, w);
            if (skippedUsers.Count > 0)
                _logger.LogInformation("Восстановление {TargetDb} на {TargetServer}: пропущены пользователи без логина на сервере: {Users}",
                    db, request.TargetServer, string.Join(", ", skippedUsers));

            await WriteAuditAsync(new AuditLogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                Action = "RESTORE_DATABASE",
                Status = "SUCCESS",
                Host = request.TargetServer,
                ServiceName = db,
                DisplayName = $"Восстановление базы {db}",
                ClientIp = clientIp,
                DurationMs = sw.ElapsedMilliseconds,
                ErrorMessage = BuildRestoreAuditMessage($"{request.SourceServer}/{request.SourceDatabase}", steps.Select(s => s.Point).ToList(),
                    filesUsed, FormatDuration(sw.Elapsed), jobsDenied, warnings, restoredUsers)
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Ошибка восстановления {TargetDb} на {TargetServer}", db, request.TargetServer);

            string errorMessage = ex.Message;
            string? dbState = await TryRecoverAfterFailureAsync(masterConnString, db).ConfigureAwait(false);
            if (string.Equals(dbState, "RESTORING", StringComparison.OrdinalIgnoreCase))
                errorMessage += " База оставлена в состоянии RESTORING — повторите восстановление.";

            state.Warnings = warnings;
            state.Status = RestoreOperationStatus.Failed;
            state.ErrorMessage = errorMessage;
            state.Stage = $"Ошибка: {errorMessage}";
            state.EndTime = DateTime.UtcNow;
            state.EstimatedSecondsRemaining = null;
            state.ElapsedSeconds = (int)sw.Elapsed.TotalSeconds;

            await WriteAuditAsync(new AuditLogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                Action = "RESTORE_DATABASE",
                Status = "FAILED",
                Host = request.TargetServer,
                ServiceName = db,
                DisplayName = $"Восстановление базы {db}",
                ClientIp = clientIp,
                DurationMs = sw.ElapsedMilliseconds,
                ErrorMessage = $"Сбой восстановления базы {db}: {errorMessage}"
            }).ConfigureAwait(false);
        }
    }

    private static void SetStage(RestoreOperationState state, string stage, int percent)
    {
        state.Stage = stage;
        state.PercentComplete = percent;
    }

    private async Task<List<RestoreStep>> BuildRestoreStepsAsync(SqlConnection conn, RestoreChainCandidates candidates, List<string> warnings)
    {
        var headerCache = new Dictionary<string, BackupHeader>(StringComparer.Ordinal);

        async Task<BackupHeader> HeaderOf(RestoreTimelinePoint point)
        {
            if (!headerCache.TryGetValue(point.Id, out var header))
            {
                header = await ReadHeaderAsync(conn, point).ConfigureAwait(false);
                headerCache[point.Id] = header;
            }
            return header;
        }

        async Task<List<RestoreStep>> LogsFromAsync(decimal baseLsn, DateTime baseDate)
        {
            var slack = TimeSpan.FromHours(1);
            var logHeaders = new List<(RestoreTimelinePoint, BackupHeader)>();
            foreach (var log in candidates.Logs.Where(l => l.BackupDate > baseDate - slack || ReferenceEquals(l, candidates.Target)))
                logHeaders.Add((log, await HeaderOf(log).ConfigureAwait(false)));

            return SelectLogSequence(baseLsn, logHeaders, candidates.Target)
                .Select(x => new RestoreStep(x.Point, x.Header))
                .ToList();
        }

        var fullStep = new RestoreStep(candidates.Full, await HeaderOf(candidates.Full).ConfigureAwait(false));
        if (candidates.Target.Type == BackupType.Full)
            return [fullStep];

        // Diff base: the latest full, or an earlier one when the latest full was taken with COPY_ONLY
        RestoreStep? diffBase = null;
        RestoreStep? diffStep = null;
        if (candidates.Differential != null)
        {
            var diffHeader = await HeaderOf(candidates.Differential).ConfigureAwait(false);
            foreach (var full in new[] { candidates.Full }.Concat(candidates.EarlierFulls))
            {
                var fullHeader = await HeaderOf(full).ConfigureAwait(false);
                if (IsDifferentialBasedOn(diffHeader, fullHeader))
                {
                    diffBase = new RestoreStep(full, fullHeader);
                    diffStep = new RestoreStep(candidates.Differential, diffHeader);
                    break;
                }
            }

            if (diffStep == null)
            {
                if (ReferenceEquals(candidates.Differential, candidates.Target))
                {
                    throw new InvalidOperationException(
                        $"Для разностного бэкапа {candidates.Target.Date} {candidates.Target.Time} в архиве не найден базовый полный бэкап. Выберите полный бэкап или другую точку.");
                }
                warnings.Add($"Разностный бэкап {candidates.Differential.Date} {candidates.Differential.Time} пропущен: базовый полный бэкап не найден, применены журналы.");
            }
        }

        if (candidates.Target.Type == BackupType.Differential)
            return [diffBase!, diffStep!];

        if (diffStep != null)
        {
            try
            {
                var logs = await LogsFromAsync(diffStep.Header.LastLsn, diffStep.Point.BackupDate).ConfigureAwait(false);
                return [diffBase!, diffStep, .. logs];
            }
            catch (InvalidOperationException ex)
            {
                // E.g. the target log was taken concurrently with the diff and is already covered by it
                warnings.Add($"Цепочка журналов от разностного бэкапа не сошлась ({ex.Message}); использованы полный бэкап и журналы.");
            }
        }

        var logsFromFull = await LogsFromAsync(fullStep.Header.LastLsn, fullStep.Point.BackupDate).ConfigureAwait(false);
        return [fullStep, .. logsFromFull];
    }

    private static string DiskClause(RestoreTimelinePoint point)
        => string.Join(", ", point.FilePaths.Select(p => "DISK = " + QuoteLiteral(p)));

    private static string BuildRestoreStatement(string qdb, RestoreStep step, IReadOnlyList<(string LogicalName, string TargetPath)>? moves)
    {
        string kind = step.Point.Type == BackupType.Log ? "LOG" : "DATABASE";
        var options = new List<string> { $"FILE = {step.Header.Position}" };
        if (moves != null)
        {
            options.Add("REPLACE");
            options.AddRange(moves.Select(m => $"MOVE {QuoteLiteral(m.LogicalName)} TO {QuoteLiteral(m.TargetPath)}"));
        }
        options.Add("NORECOVERY");
        options.Add("STATS = 5");

        return $"RESTORE {kind} {qdb} FROM {DiskClause(step.Point)} WITH {string.Join(", ", options)};";
    }

    private async Task<BackupHeader> ReadHeaderAsync(SqlConnection conn, RestoreTimelinePoint point)
    {
        var headers = new List<BackupHeader>();
        await using (var cmd = new SqlCommand($"RESTORE HEADERONLY FROM {DiskClause(point)};", conn) { CommandTimeout = 300 })
        await using (var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
        {
            int oType = reader.GetOrdinal("BackupType");
            int oPos = reader.GetOrdinal("Position");
            int oFirst = reader.GetOrdinal("FirstLSN");
            int oLast = reader.GetOrdinal("LastLSN");
            int oCheckpoint = reader.GetOrdinal("CheckpointLSN");
            int oDiffBase = reader.GetOrdinal("DifferentialBaseLSN");

            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                headers.Add(new BackupHeader(
                    Convert.ToInt32(reader.GetValue(oPos)),
                    Convert.ToInt32(reader.GetValue(oType)),
                    ReadDecimal(reader, oFirst) ?? 0,
                    ReadDecimal(reader, oLast) ?? 0,
                    ReadDecimal(reader, oCheckpoint) ?? 0,
                    ReadDecimal(reader, oDiffBase)));
            }
        }

        int expected = ExpectedHeaderType(point.Type);
        var header = headers.Where(h => h.BackupType == expected).MaxBy(h => h.Position);
        if (header == null)
        {
            string actual = headers.Count == 0 ? "нет наборов" : string.Join(",", headers.Select(h => h.BackupType).Distinct());
            throw new InvalidOperationException(
                $"Файл {Path.GetFileName(point.FilePaths[0])} не содержит бэкап типа {point.Type} (BackupType в заголовке: {actual}).");
        }
        return header;
    }

    private static decimal? ReadDecimal(SqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : Convert.ToDecimal(reader.GetValue(ordinal));

    private static async Task<List<BackupFileEntry>> ReadFileListAsync(SqlConnection conn, RestoreStep fullStep)
    {
        var files = new List<BackupFileEntry>();
        string sql = $"RESTORE FILELISTONLY FROM {DiskClause(fullStep.Point)} WITH FILE = {fullStep.Header.Position};";
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 300 };
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            string logical = reader["LogicalName"]?.ToString() ?? "";
            if (string.IsNullOrEmpty(logical)) continue;
            files.Add(new BackupFileEntry(logical, reader["Type"]?.ToString() ?? "D", reader["PhysicalName"]?.ToString() ?? ""));
        }

        if (files.Count == 0)
            throw new InvalidOperationException("RESTORE FILELISTONLY не вернул ни одного файла базы.");
        return files;
    }

    private static async Task<TargetMetadata> ReadTargetMetadataAsync(SqlConnection conn, string db)
    {
        const string metaSql = @"
SELECT
    COALESCE(CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS NVARCHAR(512)), N'') AS DefaultData,
    COALESCE(CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS NVARCHAR(512)), N'') AS DefaultLog,
    (SELECT physical_name FROM sys.master_files WHERE database_id = 1 AND file_id = 1) AS MasterData,
    (SELECT physical_name FROM sys.master_files WHERE database_id = 1 AND file_id = 2) AS MasterLog,
    d.state_desc AS StateDesc,
    SUSER_SNAME(d.owner_sid) AS OwnerName
FROM (SELECT 1 AS x) AS s
LEFT JOIN sys.databases AS d ON d.name = @db;

SELECT name, physical_name, type_desc FROM sys.master_files WHERE database_id = DB_ID(@db) ORDER BY file_id;";

        bool exists = false;
        string stateDesc = "";
        string? owner = null, defaultData = null, defaultLog = null;
        var files = new List<ExistingDbFile>();

        await using (var cmd = new SqlCommand(metaSql, conn) { CommandTimeout = 30 })
        {
            cmd.Parameters.Add("@db", System.Data.SqlDbType.NVarChar, 128).Value = db;
            await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);

            if (await reader.ReadAsync().ConfigureAwait(false))
            {
                defaultData = NullIfEmpty(reader["DefaultData"]?.ToString()) ?? Path.GetDirectoryName(reader["MasterData"]?.ToString() ?? "");
                defaultLog = NullIfEmpty(reader["DefaultLog"]?.ToString()) ?? Path.GetDirectoryName(reader["MasterLog"]?.ToString() ?? "");
                if (reader["StateDesc"] is string sd)
                {
                    exists = true;
                    stateDesc = sd;
                    owner = reader["OwnerName"] as string;
                }
            }

            if (await reader.NextResultAsync().ConfigureAwait(false))
            {
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    files.Add(new ExistingDbFile(
                        reader["name"]?.ToString() ?? "",
                        reader["physical_name"]?.ToString() ?? "",
                        reader["type_desc"]?.ToString() ?? ""));
                }
            }
        }

        var users = new List<(string, string?)>();
        var roles = new List<(string, string)>();

        if (exists && stateDesc.Equals("ONLINE", StringComparison.OrdinalIgnoreCase))
        {
            const string usersSql = @"
DECLARE @q NVARCHAR(260) = QUOTENAME(@db);
DECLARE @sql NVARCHAR(MAX) = N'
SELECT dp.name AS UserName, sp.name AS LoginName
FROM ' + @q + N'.sys.database_principals AS dp
LEFT JOIN sys.server_principals AS sp ON sp.sid = dp.sid
WHERE dp.type IN (''S'', ''U'', ''G'') AND dp.principal_id > 4 AND dp.sid IS NOT NULL;

SELECT m.name AS UserName, r.name AS RoleName
FROM ' + @q + N'.sys.database_role_members AS rm
JOIN ' + @q + N'.sys.database_principals AS r ON r.principal_id = rm.role_principal_id
JOIN ' + @q + N'.sys.database_principals AS m ON m.principal_id = rm.member_principal_id
WHERE m.type IN (''S'', ''U'', ''G'') AND m.principal_id > 4;';
EXEC sp_executesql @sql;";

            await using var cmd = new SqlCommand(usersSql, conn) { CommandTimeout = 60 };
            cmd.Parameters.Add("@db", System.Data.SqlDbType.NVarChar, 128).Value = db;
            await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);

            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                string u = reader["UserName"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(u))
                    users.Add((u, reader["LoginName"] as string));
            }

            if (await reader.NextResultAsync().ConfigureAwait(false))
            {
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    string u = reader["UserName"]?.ToString() ?? "";
                    string r = reader["RoleName"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(u) && !string.IsNullOrEmpty(r))
                        roles.Add((u, r));
                }
            }
        }

        return new TargetMetadata(exists, stateDesc, owner, defaultData, defaultLog, files, users, roles);
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static async Task SetSimpleAndShrinkLogAsync(SqlConnection dbConn, string qdb)
    {
        string sql = $@"
ALTER DATABASE {qdb} SET RECOVERY SIMPLE WITH NO_WAIT;
DECLARE @shrink NVARCHAR(MAX) = N'';
SELECT @shrink = @shrink + N'DBCC SHRINKFILE (' + CAST(file_id AS NVARCHAR(10)) + N', 100) WITH NO_INFOMSGS; '
FROM sys.database_files WHERE type = 1;
IF LEN(@shrink) > 0 EXEC (@shrink);";
        await ExecuteNonQueryAsync(dbConn, sql, 1800).ConfigureAwait(false);
    }

    private async Task RestorePermissionsAsync(SqlConnection dbConn, string qdb, TargetMetadata meta, List<string> warnings, List<string> skippedUsers, List<string> restoredUsers)
    {
        // Owner first: the owner's login maps to dbo and must not be recreated as a regular user
        if (!string.IsNullOrWhiteSpace(meta.OwnerName))
        {
            await TryStepAsync(warnings, $"Возврат владельца {meta.OwnerName}",
                () => ExecuteNonQueryAsync(dbConn, $"ALTER AUTHORIZATION ON DATABASE::{qdb} TO {QuoteIdentifier(meta.OwnerName)};", 30)).ConfigureAwait(false);
        }

        const string userSql = @"
DECLARE @sid VARBINARY(85) = SUSER_SID(@login);
IF @sid IS NULL BEGIN SELECT N'NOLOGIN'; RETURN; END;
DECLARE @mapped SYSNAME = (SELECT TOP 1 name FROM sys.database_principals WHERE sid = @sid);
IF @mapped IS NOT NULL BEGIN SELECT N'MAPPED'; RETURN; END;
DECLARE @stmt NVARCHAR(MAX);
IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @user)
    SET @stmt = N'ALTER USER ' + QUOTENAME(@user) + N' WITH LOGIN = ' + QUOTENAME(@login);
ELSE
    SET @stmt = N'CREATE USER ' + QUOTENAME(@user) + N' FOR LOGIN ' + QUOTENAME(@login);
EXEC (@stmt);
SELECT N'OK';";

        foreach (var (user, login) in meta.Users)
        {
            if (string.IsNullOrWhiteSpace(login))
            {
                skippedUsers.Add(user);
                continue;
            }

            try
            {
                await using var cmd = new SqlCommand(userSql, dbConn) { CommandTimeout = 30 };
                cmd.Parameters.Add("@user", System.Data.SqlDbType.NVarChar, 128).Value = user;
                cmd.Parameters.Add("@login", System.Data.SqlDbType.NVarChar, 128).Value = login;
                string? result = (await cmd.ExecuteScalarAsync().ConfigureAwait(false))?.ToString();
                if (result is "OK" or "MAPPED")
                    restoredUsers.Add(user);
                else if (result == "NOLOGIN")
                    warnings.Add($"Пользователь {user}: логин {login} не найден на сервере.");
            }
            catch (Exception ex)
            {
                warnings.Add($"Пользователь {user} ({login}): {ex.Message}");
            }
        }

        const string roleSql = @"
IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @user AND type IN ('S', 'U', 'G'))
   AND EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @role AND type = 'R')
   AND NOT EXISTS (
        SELECT 1 FROM sys.database_role_members rm
        JOIN sys.database_principals r ON r.principal_id = rm.role_principal_id
        JOIN sys.database_principals m ON m.principal_id = rm.member_principal_id
        WHERE r.name = @role AND m.name = @user)
BEGIN
    DECLARE @stmt NVARCHAR(MAX) = N'ALTER ROLE ' + QUOTENAME(@role) + N' ADD MEMBER ' + QUOTENAME(@user);
    EXEC (@stmt);
END";

        foreach (var (user, role) in meta.RoleMembers)
        {
            try
            {
                await using var cmd = new SqlCommand(roleSql, dbConn) { CommandTimeout = 30 };
                cmd.Parameters.Add("@user", System.Data.SqlDbType.NVarChar, 128).Value = user;
                cmd.Parameters.Add("@role", System.Data.SqlDbType.NVarChar, 128).Value = role;
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                warnings.Add($"Роль {role} для {user}: {ex.Message}");
            }
        }
    }

    private async Task<string?> TryRecoverAfterFailureAsync(string masterConnString, string db)
    {
        const string sql = @"
DECLARE @state NVARCHAR(60) = (SELECT state_desc FROM sys.databases WHERE name = @db);
DECLARE @stmt NVARCHAR(MAX);
IF @state = N'OFFLINE'
    SET @stmt = N'ALTER DATABASE ' + QUOTENAME(@db) + N' SET ONLINE';
ELSE IF @state = N'ONLINE' AND DATABASEPROPERTYEX(@db, 'UserAccess') <> 'MULTI_USER'
    SET @stmt = N'ALTER DATABASE ' + QUOTENAME(@db) + N' SET MULTI_USER';
IF @stmt IS NOT NULL EXEC (@stmt);
SELECT state_desc FROM sys.databases WHERE name = @db;";

        try
        {
            await using var conn = new SqlConnection(masterConnString);
            await conn.OpenAsync().ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 60 };
            cmd.Parameters.Add("@db", System.Data.SqlDbType.NVarChar, 128).Value = db;
            return (await cmd.ExecuteScalarAsync().ConfigureAwait(false))?.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось вернуть базу {Db} в рабочее состояние после ошибки восстановления", db);
            return null;
        }
    }

    private async Task TryStepAsync(List<string> warnings, string stepName, Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Шаг '{Step}' завершился ошибкой", stepName);
            warnings.Add($"{stepName}: {ex.Message}");
        }
    }

    private static async Task ExecuteNonQueryAsync(SqlConnection conn, string sql, int timeoutSeconds)
    {
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = timeoutSeconds };
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task<object?> ExecuteScalarAsync(SqlConnection conn, string sql, int timeoutSeconds)
    {
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = timeoutSeconds };
        return await cmd.ExecuteScalarAsync().ConfigureAwait(false);
    }

    private async Task MonitorRestoreProgressAsync(string targetServer, int spid, RestoreOperationState state, ProgressWindow window, CancellationToken ct)
    {
        string connString = BuildConnectionString(targetServer, "master");
        const string pollSql = @"
SELECT percent_complete, estimated_completion_time / 1000 AS est_sec
FROM sys.dm_exec_requests
WHERE session_id = @spid AND command LIKE N'RESTORE%';";

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1500, ct).ConfigureAwait(false);
                await using var conn = new SqlConnection(connString);
                await conn.OpenAsync(ct).ConfigureAwait(false);

                await using var cmd = new SqlCommand(pollSql, conn) { CommandTimeout = 10 };
                cmd.Parameters.Add("@spid", System.Data.SqlDbType.Int).Value = spid;
                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

                if (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    float rawPercent = Convert.ToSingle(reader["percent_complete"]);
                    long estSec = reader["est_sec"] != DBNull.Value ? Convert.ToInt64(reader["est_sec"]) : 0;

                    int from = window.From, to = window.To;
                    state.PercentComplete = Math.Clamp(from + (int)((to - from) * rawPercent / 100f), from, Math.Max(from, to));
                    state.EstimatedSecondsRemaining = estSec > 0 ? (int)Math.Min(estSec, int.MaxValue) : null;
                    state.ElapsedSeconds = (int)(DateTime.UtcNow - state.StartTime).TotalSeconds;
                    state.Stage = $"{window.Label} ({rawPercent:F1}%)";
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Опрос прогресса восстановления на {Server} не удался", targetServer);
            }
        }
    }

    internal string BuildConnectionString(string host, string database = "master")
    {
        string? user = null;
        string? pass = null;

        // 1. Check server-specific credentials for restore
        if (_config.ServerCredentials != null && _config.ServerCredentials.Count > 0)
        {
            var match = _config.ServerCredentials.FirstOrDefault(c =>
                c.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ||
                host.StartsWith(c.Host + ".", StringComparison.OrdinalIgnoreCase) ||
                c.Host.StartsWith(host + ".", StringComparison.OrdinalIgnoreCase));

            if (match != null && !string.IsNullOrWhiteSpace(match.Username))
            {
                user = match.Username;
                pass = match.Password;
            }
        }

        // 2. Default restore user configured specifically for SQL restore
        if (string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(_config.DefaultRestoreUsername))
        {
            user = _config.DefaultRestoreUsername;
            pass = _config.DefaultRestorePassword;
        }

        // 3. Fallback to general DBMS config if restore user not specified
        if (string.IsNullOrWhiteSpace(user))
        {
            user = _dbmsConfig.DefaultSqlUsername;
            pass = _dbmsConfig.DefaultSqlPassword;
        }

        var b = new SqlConnectionStringBuilder
        {
            DataSource = host,
            InitialCatalog = database,
            TrustServerCertificate = true,
            ConnectTimeout = 15,
            Pooling = false,
            ApplicationName = "OneSGetDatabases-Restore"
        };

        if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(pass))
        {
            b.UserID = user;
            b.Password = pass;
            b.IntegratedSecurity = false;
        }
        else
        {
            b.IntegratedSecurity = true;
        }

        return b.ConnectionString;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024 * 1024)
            return $"{(double)bytes / (1024L * 1024 * 1024 * 1024):F1} ТБ";
        if (bytes >= 1024L * 1024 * 1024)
            return $"{(double)bytes / (1024L * 1024 * 1024):F1} ГБ";
        if (bytes >= 1024 * 1024)
            return $"{(double)bytes / (1024 * 1024):F0} МБ";
        return $"{bytes / 1024} КБ";
    }

    private const int AuditListLimit = 3;
    private const int AuditUserLimit = 10;
    private const int AuditWarningMaxLength = 150;

    /// <summary>
    /// Short audit text for a successful restore: the log backups collapse into a count and a time range,
    /// long lists keep the first few items. The full warnings and the users skipped for lack of a login go to the service log.
    /// </summary>
    internal static string BuildRestoreAuditMessage(string source, IReadOnlyList<RestoreTimelinePoint> chain, int filesUsed,
        string duration, bool jobsDenied, IReadOnlyList<string> warnings, IReadOnlyList<string> restoredUsers)
    {
        var parts = chain.Where(p => p.Type != BackupType.Log).Select(p => $"{p.Type} {p.Date} {p.Time}").ToList();
        var logs = chain.Where(p => p.Type == BackupType.Log).ToList();
        if (logs.Count == 1)
            parts.Add($"Log {logs[0].Date} {logs[0].Time}");
        else if (logs.Count > 1)
            parts.Add($"Log ×{logs.Count} ({logs[0].Date} {logs[0].Time} … {logs[^1].Date} {logs[^1].Time})");

        var sb = new System.Text.StringBuilder();
        sb.Append($"Из {source}: {string.Join(" → ", parts)}. Файлов: {filesUsed}, время: {duration}.");
        if (jobsDenied)
            sb.Append(" Регламентные задания заблокированы.");

        if (restoredUsers.Count > 0)
            sb.Append($" Права восстановлены: {ShortList(restoredUsers, ", ", AuditUserLimit)}.");

        if (warnings.Count > 0)
        {
            var shown = warnings.Select(w => w.Length > AuditWarningMaxLength ? w[..AuditWarningMaxLength].TrimEnd() + "…" : w);
            sb.Append($" Предупреждения: {ShortList(shown, " | ", AuditListLimit)}");
        }

        return sb.ToString();
    }

    private static string ShortList(IEnumerable<string> items, string separator, int limit)
    {
        var list = items.ToList();
        string head = string.Join(separator, list.Take(limit));
        return list.Count > limit ? $"{head} и ещё {list.Count - limit}" : head;
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours} ч {ts.Minutes} мин";
        if (ts.TotalMinutes >= 1)
            return $"{(int)ts.TotalMinutes} мин {ts.Seconds} с";
        return $"{ts.Seconds} с";
    }
}
