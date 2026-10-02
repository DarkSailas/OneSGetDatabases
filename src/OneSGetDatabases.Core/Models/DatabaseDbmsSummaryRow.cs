namespace OneSGetDatabases.Core.Models;

public record DatabaseDbmsSummaryRow
{
    public string Environment { get; init; } = "";
    public string Name { get; init; } = "";
    public string Cluster { get; init; } = "";
    public string SqlServer { get; init; } = "";
    public string SqlDbName { get; init; } = "";
    public long TotalSizeBytes { get; init; }
    public double TotalSizeGb => Math.Round(TotalSizeBytes / (1024.0 * 1024.0 * 1024.0), 3);
    public string DataFilesPath { get; init; } = "";
    public string LogFilesPath { get; init; } = "";
}
