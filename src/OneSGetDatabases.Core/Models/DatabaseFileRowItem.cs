namespace OneSGetDatabases.Core.Models;

public record DatabaseFileRowItem
{
    public string Environment { get; init; } = "";
    public string Name { get; init; } = "";
    public string Cluster { get; init; } = "";
    public string SqlServer { get; init; } = "";
    public string SqlDbName { get; init; } = "";
    public string FileName { get; init; } = "";
    public string FileType { get; init; } = "";
    public long SizeBytes { get; init; }
    public double SizeMb => Math.Round(SizeBytes / (1024.0 * 1024.0), 2);
    public double SizeGb => Math.Round(SizeBytes / (1024.0 * 1024.0 * 1024.0), 3);
    public string PhysicalPath { get; init; } = "";
}
