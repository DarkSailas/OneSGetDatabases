namespace OneSGetDatabases.Core.Models;

public record InfoBaseItem
{
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public string UUID { get; init; } = "";
    public string AccessGroup { get; set; } = "Отсутствует";
    public string Cluster { get; init; } = "";
    public string ServerIP { get; set; } = "Неизвестно";
    public string ClusterUUID { get; init; } = "";
    public string Platform { get; set; } = "Unknown";
    public string Consul { get; set; } = "Отсутствует";
    public string SQL { get; set; } = "Неизвестно";
    public string SQLDbName { get; set; } = "Неизвестно";
    public string ServiceUser { get; set; } = "Неизвестно";
    public string ServiceName { get; set; } = "Неизвестно";
    public string ClusterPath { get; set; } = "Неизвестно";
    public string Environment { get; init; } = "PROD"; // PROD or DEV
    public string V8iFile { get; set; } = "Неизвестно";
    public string RaGroup { get; set; } = "Неизвестно";
    public string OneCGroup { get; set; } = "Неизвестно";
    public DateTime LastScannedAt { get; set; } = DateTime.UtcNow;
    public DbmsDetails? DbmsDetails { get; set; }

    // Unique identification across clusters
    public string UniqueKey => $"{Environment}_{Cluster}_{Name}";
}
