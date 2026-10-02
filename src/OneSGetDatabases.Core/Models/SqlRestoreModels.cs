using System.Text.Json.Serialization;

namespace OneSGetDatabases.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BackupType
{
    Full,
    Differential,
    Log
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RestoreOperationStatus
{
    Queued,
    Running,
    Completed,
    Failed
}

public record BackupCatalogSource
{
    public required string Server { get; init; }
    public required string Database { get; init; }
    public required string DisplayName { get; init; }
    public required string FullPath { get; init; }
}

/// <summary>Шаг предварительного плана восстановления (для журналов — сводка по всем файлам журналов).</summary>
public record RestorePlanStep
{
    public required BackupType Type { get; init; }
    public required string From { get; init; }
    public required string To { get; init; }
    public int BackupCount { get; init; } = 1;
    public int FileCount { get; init; }
    public long SizeBytes { get; init; }
    public required string SizeDisplay { get; init; }
}

/// <summary>
/// Предварительный план: какие бэкапы будут применены для выбранной точки. Строится по датам файлов;
/// окончательная проверка цепочки по LSN выполняется при запуске восстановления.
/// </summary>
public record RestorePlanPreview
{
    public string? TargetMoment { get; init; }
    public List<RestorePlanStep> Steps { get; init; } = [];
    public int TotalFiles { get; init; }
    public long TotalBytes { get; init; }
    public string TotalDisplay { get; init; } = "";
    public string? Error { get; init; }
}

/// <summary>Что лежит в каталоге базы архива бэкапов: для объяснения, почему не найдено точек восстановления.</summary>
public record BackupCatalogDiagnostics
{
    public required string Path { get; init; }
    public required string Account { get; init; }
    public bool Exists { get; init; }
    public int TotalFiles { get; init; }
    public int SearchDepth { get; init; }
    public Dictionary<string, int> Extensions { get; init; } = [];
    public List<string> SubDirectories { get; init; } = [];
    public List<string> AccessErrors { get; init; } = [];
    public List<string> SampleFiles { get; init; } = [];
    public string? Error { get; init; }
}

public record BackupFileInfo
{
    public required string FileName { get; init; }
    public required string FullPath { get; init; }
    public required DateTime BackupDate { get; init; }
    public required BackupType Type { get; init; }
    public required long SizeBytes { get; init; }
    public required string ServerName { get; init; }
    public required string DatabaseName { get; init; }
}

public record RestoreTimelinePoint
{
    public required string Id { get; init; }
    public required string Date { get; init; }
    public required string Time { get; init; }
    public required BackupType Type { get; init; }
    public required long SizeBytes { get; init; }
    public required string SizeDisplay { get; init; }
    public required string DisplayName { get; init; }
    public DateTime BackupDate { get; init; }
    public List<string> FilePaths { get; init; } = [];
}

public record RestoreStartRequest
{
    public required string TargetServer { get; init; }
    public required string TargetDatabase { get; init; }
    public required string SourceServer { get; init; }
    public required string SourceDatabase { get; init; }
    public required string PointId { get; init; }
    public bool DenyScheduledJobsIn1C { get; init; } = true;
    public bool SetSimpleRecoveryAndShrink { get; init; } = true;
    public bool RestorePermissions { get; init; } = true;
    public string? ClusterHost { get; init; }
    public int? ClusterPort { get; init; }
    public string? InfobaseName { get; init; }
}

public record RestoreOperationState
{
    public required string OperationId { get; init; }
    public required string TargetServer { get; init; }
    public required string TargetDatabase { get; init; }
    public required string SourceServer { get; init; }
    public required string SourceDatabase { get; init; }
    public required string InfobaseName { get; init; }
    public RestoreOperationStatus Status { get; set; } = RestoreOperationStatus.Queued;
    public int PercentComplete { get; set; }
    public string Stage { get; set; } = "Инициализация...";
    public int? EstimatedSecondsRemaining { get; set; }
    public int ElapsedSeconds { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartTime { get; init; } = DateTime.UtcNow;
    public DateTime? EndTime { get; set; }
    public List<string> Warnings { get; set; } = [];
}

public class SqlRestoreServerCredential
{
    public string Host { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class SqlRestoreConfig
{
    public string BackupArchiveRoot { get; set; } = @"\\backup_sql\sql_archive$";
    public List<string> AllowedTargetServers { get; set; } = [];
    /// <summary>Домены, в которых FQDN сервера из белого списка считается тем же сервером (dev-s-sql01.domain.int).</summary>
    public List<string> AllowedDomainSuffixes { get; set; } = [];
    public string DefaultRestoreUsername { get; set; } = "sql_restore";
    public string DefaultRestorePassword { get; set; } = "";
    public List<string> ExcludedCatalogs { get; set; } = [];
    public List<string> ExcludedDatabases { get; set; } = [];
    public List<SqlRestoreServerCredential> ServerCredentials { get; set; } = [];
    public int CommandTimeoutMinutes { get; set; } = 180;
    public int CacheDurationMinutes { get; set; } = 15;

    /// <summary>
    /// Администраторы информационной базы 1С для блокировки регламентных заданий через rac (infobase update).
    /// Перебираются по порядку, последним пробуется администратор кластера (ClusterUser сервера или ClusterDiscovery:DefaultClusterUser).
    /// </summary>
    public List<InfobaseAdminCredential> InfobaseAdminCredentials { get; set; } = [];
}

public class InfobaseAdminCredential
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    /// <summary>Синоним Password для совместимости с форматом других конфигураций. Значение не шифруется.</summary>
    public string EncryptedPassword { get; set; } = "";

    public string EffectivePassword => !string.IsNullOrEmpty(Password) ? Password : EncryptedPassword;
}

