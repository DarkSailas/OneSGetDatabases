namespace OneSGetDatabases.Core.Models;

public record DbmsFileItem
{
    public string FileName { get; init; } = "";
    public string PhysicalPath { get; init; } = "";
    public string FileType { get; init; } = "ROWS"; // ROWS, LOG, FILESTREAM, TABLESPACE
    public long SizeBytes { get; init; }
    public double SizeMb => Math.Round(SizeBytes / (1024.0 * 1024.0), 2);
    public double SizeGb => Math.Round(SizeBytes / (1024.0 * 1024.0 * 1024.0), 2);
    public string? FileGroup { get; init; }
    public long? UsedSizeBytes { get; init; }
    public double? UsedSizeMb => UsedSizeBytes.HasValue ? Math.Round(UsedSizeBytes.Value / (1024.0 * 1024.0), 2) : null;
}

public record DbmsUserPermission
{
    public string PrincipalName { get; init; } = "";
    public string PrincipalType { get; init; } = "SQL_USER"; // SQL_USER, WINDOWS_USER, ROLE, etc.
    public string RoleOrPermission { get; init; } = "";
    public string State { get; init; } = "GRANT";
}

public record DbmsDetails
{
    public string DbServer { get; init; } = "";
    public string DatabaseName { get; init; } = "";
    public string DbmsType { get; init; } = "MSSQL"; // MSSQL, PostgreSQL, etc.
    public DateTime? CreatedDate { get; init; }
    public string? Owner { get; init; }
    public string? State { get; init; } = "ONLINE";
    public string? RecoveryModel { get; init; }
    public string? Collation { get; init; }
    public int? CompatibilityLevel { get; init; }
    public long TotalSizeBytes { get; init; }
    public double TotalSizeMb => Math.Round(TotalSizeBytes / (1024.0 * 1024.0), 2);
    public double TotalSizeGb => Math.Round(TotalSizeBytes / (1024.0 * 1024.0 * 1024.0), 2);
    public List<DbmsFileItem> Files { get; init; } = [];
    public List<DbmsUserPermission> Permissions { get; init; } = [];
    public DateTime? LastBackupDate { get; init; }
    public string? Error { get; init; }
    public bool IsSuccess => string.IsNullOrEmpty(Error);
}
