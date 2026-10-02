using OneSGetDatabases.Core.Models;

namespace OneSGetDatabases.Core.Services;

/// <summary>Заголовок набора бэкапа из RESTORE HEADERONLY.</summary>
internal sealed record BackupHeader(
    int Position,
    int BackupType,
    decimal FirstLsn,
    decimal LastLsn,
    decimal CheckpointLsn,
    decimal? DifferentialBaseLsn)
{
    public const int TypeFull = 1;
    public const int TypeLog = 2;
    public const int TypeDifferential = 5;
}

/// <summary>Кандидаты цепочки восстановления, подобранные по датам файлов (до проверки LSN).</summary>
internal sealed record RestoreChainCandidates(
    RestoreTimelinePoint Full,
    RestoreTimelinePoint? Differential,
    IReadOnlyList<RestoreTimelinePoint> Logs,
    RestoreTimelinePoint Target)
{
    /// <summary>Более ранние полные бэкапы (новые первыми): база для diff, если последний full снят с COPY_ONLY.</summary>
    public IReadOnlyList<RestoreTimelinePoint> EarlierFulls { get; init; } = [];
}

internal sealed record BackupFileEntry(string LogicalName, string Type, string PhysicalName);

internal sealed record ExistingDbFile(string LogicalName, string PhysicalName, string TypeDesc);

internal static class RestoreChainPlanner
{
    // Log backup may finish slightly earlier than a concurrent full/diff backup, so LSN check decides, not the timestamp.
    private static readonly TimeSpan LogCandidateSlack = TimeSpan.FromHours(1);

    public static BackupType ClassifyBackupFile(string fileName, string? parentDirectoryName)
    {
        string ext = Path.GetExtension(fileName);
        if (ext.Equals(".trn", StringComparison.OrdinalIgnoreCase) || ext.Equals(".log", StringComparison.OrdinalIgnoreCase))
            return BackupType.Log;
        if (ext.Equals(".diff", StringComparison.OrdinalIgnoreCase) || ext.Equals(".dif", StringComparison.OrdinalIgnoreCase))
            return BackupType.Differential;

        // Ola Hallengren style: DIFF/LOG may also use .bak; recognise by folder or name token
        string dir = parentDirectoryName ?? "";
        if (dir.Equals("DIFF", StringComparison.OrdinalIgnoreCase) || fileName.Contains("_DIFF_", StringComparison.OrdinalIgnoreCase))
            return BackupType.Differential;
        if (dir.Equals("LOG", StringComparison.OrdinalIgnoreCase) || fileName.Contains("_LOG_", StringComparison.OrdinalIgnoreCase))
            return BackupType.Log;

        return BackupType.Full;
    }

    public static RestoreChainCandidates BuildCandidates(IReadOnlyList<RestoreTimelinePoint> points, RestoreTimelinePoint target)
    {
        if (target.Type == BackupType.Full)
            return new RestoreChainCandidates(target, null, [], target);

        var ordered = points.OrderBy(p => p.BackupDate).ToList();

        var full = ordered.LastOrDefault(p => p.Type == BackupType.Full && p.BackupDate < target.BackupDate)
            ?? throw new InvalidOperationException(
                $"В архиве нет полного бэкапа, предшествующего выбранной точке {target.BackupDate:dd.MM.yyyy HH:mm:ss}. Восстановление разностного бэкапа или журнала без полного невозможно.");

        const int maxEarlierFulls = 5;
        var earlierFulls = ordered
            .Where(p => p.Type == BackupType.Full && p.BackupDate < full.BackupDate)
            .OrderByDescending(p => p.BackupDate)
            .Take(maxEarlierFulls)
            .ToList();

        if (target.Type == BackupType.Differential)
            return new RestoreChainCandidates(full, target, [], target) { EarlierFulls = earlierFulls };

        var diff = ordered.LastOrDefault(p => p.Type == BackupType.Differential
                                              && p.BackupDate > full.BackupDate
                                              && p.BackupDate <= target.BackupDate);

        var logs = ordered
            .Where(p => p.Type == BackupType.Log
                        && p.BackupDate > full.BackupDate - LogCandidateSlack
                        && p.BackupDate <= target.BackupDate)
            .ToList();

        if (!logs.Contains(target))
            logs.Add(target);

        return new RestoreChainCandidates(full, diff, logs, target) { EarlierFulls = earlierFulls };
    }

    /// <summary>
    /// Отбирает журналы, продолжающие цепочку от baseLastLsn (LastLSN полного или разностного бэкапа),
    /// и проверяет отсутствие разрывов. Бросает исключение, если целевой журнал недостижим.
    /// </summary>
    public static IReadOnlyList<(RestoreTimelinePoint Point, BackupHeader Header)> SelectLogSequence(
        decimal baseLastLsn,
        IEnumerable<(RestoreTimelinePoint Point, BackupHeader Header)> logs,
        RestoreTimelinePoint target)
    {
        var result = new List<(RestoreTimelinePoint, BackupHeader)>();
        decimal currentLsn = baseLastLsn;

        foreach (var item in logs.OrderBy(l => l.Header.FirstLsn).ThenBy(l => l.Header.LastLsn))
        {
            if (item.Header.LastLsn <= currentLsn)
                continue; // already covered by base backup or previous log

            if (item.Header.FirstLsn > currentLsn)
            {
                throw new InvalidOperationException(
                    $"Разрыв цепочки журналов транзакций: ожидался журнал, содержащий LSN {currentLsn}, следующий файл начинается с LSN {item.Header.FirstLsn} ({Path.GetFileName(item.Point.FilePaths.FirstOrDefault() ?? "")}).");
            }

            result.Add(item);
            currentLsn = item.Header.LastLsn;

            if (ReferenceEquals(item.Point, target))
                return result;
        }

        throw new InvalidOperationException(
            "Выбранный журнал транзакций не продолжает цепочку от полного/разностного бэкапа (LSN не совпадают). Выберите другую точку.");
    }

    public static bool IsDifferentialBasedOn(BackupHeader diff, BackupHeader full)
        => diff.DifferentialBaseLsn is null || diff.DifferentialBaseLsn == full.CheckpointLsn;

    public static int ExpectedHeaderType(BackupType type) => type switch
    {
        BackupType.Full => BackupHeader.TypeFull,
        BackupType.Differential => BackupHeader.TypeDifferential,
        _ => BackupHeader.TypeLog
    };

    /// <summary>
    /// Строит MOVE-сопоставление файлов бэкапа: сначала по логическому имени на существующие файлы целевой базы,
    /// затем по типу файла, иначе — новый файл в каталоге по умолчанию.
    /// </summary>
    public static IReadOnlyList<(string LogicalName, string TargetPath)> PlanFileMoves(
        IReadOnlyList<BackupFileEntry> backupFiles,
        IReadOnlyList<ExistingDbFile> existingFiles,
        string? defaultDataPath,
        string? defaultLogPath,
        string targetDatabase)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<(string, string)>();
        bool primaryAssigned = false;

        foreach (var bf in backupFiles)
        {
            string category = BackupTypeCategory(bf.Type);

            var match = existingFiles.FirstOrDefault(e =>
                            !used.Contains(e.PhysicalName)
                            && ExistingTypeCategory(e.TypeDesc) == category
                            && e.LogicalName.Equals(bf.LogicalName, StringComparison.OrdinalIgnoreCase))
                        ?? existingFiles.FirstOrDefault(e =>
                            !used.Contains(e.PhysicalName)
                            && ExistingTypeCategory(e.TypeDesc) == category);

            string targetPath;
            if (match != null && !string.IsNullOrWhiteSpace(match.PhysicalName))
            {
                used.Add(match.PhysicalName);
                targetPath = match.PhysicalName;
            }
            else
            {
                string? dir = category == "LOG" ? defaultLogPath : defaultDataPath;
                if (string.IsNullOrWhiteSpace(dir))
                    dir = category == "LOG" ? defaultDataPath : defaultLogPath;
                if (string.IsNullOrWhiteSpace(dir))
                {
                    throw new InvalidOperationException(
                        $"Не удалось определить каталог для файла '{bf.LogicalName}': у экземпляра SQL Server не заданы пути по умолчанию, а в целевой базе нет подходящего файла.");
                }

                string ext = category switch
                {
                    "LOG" => ".ldf",
                    "DATA" when !primaryAssigned => ".mdf",
                    "DATA" => ".ndf",
                    _ => ""
                };
                targetPath = Path.Combine(dir, $"{SanitizeFileName(targetDatabase)}_{SanitizeFileName(bf.LogicalName)}{ext}");
            }

            if (category == "DATA")
                primaryAssigned = true;

            if (!targets.Add(targetPath))
                throw new InvalidOperationException($"Два файла бэкапа сопоставлены одному физическому файлу '{targetPath}'.");

            result.Add((bf.LogicalName, targetPath));
        }

        return result;
    }

    private static string BackupTypeCategory(string type) => type.ToUpperInvariant() switch
    {
        "L" => "LOG",
        "S" => "FILESTREAM",
        "F" => "FULLTEXT",
        _ => "DATA"
    };

    private static string ExistingTypeCategory(string typeDesc) => typeDesc.ToUpperInvariant() switch
    {
        "LOG" => "LOG",
        "FILESTREAM" => "FILESTREAM",
        "FULLTEXT" => "FULLTEXT",
        _ => "DATA"
    };

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }

    /// <summary>
    /// Сервер разрешён, если имя хоста совпадает с записью белого списка точно, либо является её FQDN
    /// в одном из доверенных доменов. Произвольный суффикс (dev-s-sql01.evil.tld) не принимается,
    /// иначе учётные данные sql_restore ушли бы на чужой хост.
    /// </summary>
    public static bool IsAllowedTargetServer(string targetServer, IEnumerable<string> allowedServers, IEnumerable<string>? trustedDomainSuffixes = null)
    {
        string target = targetServer.Trim().ToLowerInvariant();
        // "host\instance" or "host,port" -> host part
        int sep = target.IndexOfAny(['\\', ',']);
        string host = sep >= 0 ? target[..sep] : target;

        var suffixes = (trustedDomainSuffixes ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().TrimStart('.').ToLowerInvariant())
            .ToList();

        foreach (var raw in allowedServers)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string allowed = raw.Trim().ToLowerInvariant();

            if (target == allowed || host == allowed)
                return true;
            // Short name in request, FQDN in the whitelist
            if (allowed.StartsWith(host + ".", StringComparison.Ordinal) && !host.Contains('.'))
                return true;
            // FQDN in request within a trusted domain
            if (suffixes.Any(s => host == $"{allowed}.{s}"))
                return true;
        }

        return false;
    }

    public static bool IsSafePathSegment(string? segment)
        => !string.IsNullOrWhiteSpace(segment)
           && segment != "." && segment != ".."
           && segment.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    public static string QuoteIdentifier(string name) => "[" + name.Replace("]", "]]") + "]";

    public static string QuoteLiteral(string value) => "N'" + value.Replace("'", "''") + "'";
}
