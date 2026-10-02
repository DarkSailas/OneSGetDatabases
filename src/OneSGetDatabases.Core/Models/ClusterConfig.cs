namespace OneSGetDatabases.Core.Models;

public record ClusterConfig
{
    public required string Server { get; init; }
    public int RasPort { get; init; } = 0;
    public string Environment { get; init; } = "PROD"; // "PROD" or "DEV"
    public string ClusterUser { get; init; } = "";
    public string ClusterPassword { get; init; } = "";
    public string Platform { get; set; } = "";

    public string Host => Server.Split(':')[0];
    public int ServerPort => Server.Contains(':') && int.TryParse(Server.Split(':')[1], out var p) ? p : 1540;
    public string RasAddress => RasPort > 0 ? $"{Host}:{RasPort}" : "—";
}
