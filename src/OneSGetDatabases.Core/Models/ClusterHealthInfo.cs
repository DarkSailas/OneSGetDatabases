namespace OneSGetDatabases.Core.Models;

public enum ClusterStatus
{
    Online,
    Empty,
    Offline,
    AuthError,
    Error
}

public class ClusterHealthInfo
{
    public string Server { get; set; } = "";
    public string Host { get; set; } = "";
    public int ServerPort { get; set; }
    public int RasPort { get; set; }
    public string RasAddress { get; set; } = "";
    public string Environment { get; set; } = "PROD";
    public ClusterStatus Status { get; set; } = ClusterStatus.Offline;
    public bool IsRasPortOpen { get; set; }
    public string? PlatformVersion { get; set; }
    public int DatabasesCount { get; set; }
    public string? CimStatus { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime LastCheckedAt { get; set; } = DateTime.UtcNow;
}
