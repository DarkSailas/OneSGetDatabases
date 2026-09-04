namespace OneSGetDatabases.Core.Models;

public record OneSServiceInfo
{
    public required string Host { get; init; }
    public required string Environment { get; init; }
    public required int ClusterPort { get; init; }
    public required string DisplayName { get; init; } // Human-readable Russian name e.g. "Агент сервера 1С:Предприятия 8.3 (x86-64) (порт 3040)"
    public required string ServiceName { get; init; } // System name e.g. "1C:Enterprise 8.3 Server Agent (x86-64) (port 3040)"
    public required string Status { get; init; } // "Running" or "Stopped"
    public string StartName { get; init; } = "";
    public string ClusterDir { get; init; } = "";
    public string PlatformVersion { get; init; } = "";
    public string RasServiceName { get; init; } = "";
    public int RasPort { get; init; } = 1545;
    public string RasStatus { get; init; } = "Unknown";
    public DateTime LastCheckedAt { get; init; } = DateTime.UtcNow;
}

public record ServiceActionRequest
{
    public required string Host { get; init; }
    public required string ServiceName { get; init; }
    public required string Action { get; init; } // "start", "stop", "restart", "restart-clean-cache"
    public int ClusterPort { get; init; } = 1540;
    public string? RasServiceName { get; init; }
    public string? ClusterDir { get; init; }
}

public record ServiceActionResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public long DurationMs { get; init; }
    public string CurrentStatus { get; init; } = "";
}

public record AuditLogEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public string TimestampLocal => TimestampUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
    public string ClientIp { get; init; } = "";
    public string ClientHostName { get; init; } = "";
    public string Host { get; init; } = "";
    public int ClusterPort { get; init; }
    public string ServiceName { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Action { get; init; } = ""; // "START", "STOP", "RESTART", "RESTART_CLEAN_CACHE"
    public string Status { get; init; } = "SUCCESS"; // "SUCCESS" or "FAILED"
    public string ErrorMessage { get; init; } = "";
    public long DurationMs { get; init; }
}

public record ConsoleAuditEventRequest(string ConsoleName, string Action);

