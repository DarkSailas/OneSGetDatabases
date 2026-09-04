namespace OneSGetDatabases.Core.Models;

public record ConfluenceConfig
{
    public string BaseUrl { get; init; } = "https://space.example.com";
    public string BearerToken { get; init; } = "YOUR_CONFLUENCE_API_TOKEN_HERE";
    public string PageIdDev { get; init; } = "44154018";
    public string PageIdProd { get; init; } = "44154087";
    public string PageIdSaInfo { get; init; } = "54793711";
    public string SpaceKey { get; init; } = "itinfra";
    public string SpaceKeySaInfo { get; init; } = "FAQ";
    public string AncestorIdDevProd { get; init; } = "89329326";
    public string AncestorIdSaInfo { get; init; } = "20382191";
}

public record SchedulerConfig
{
    public int DiscoveryIntervalMinutes { get; init; } = 30;
    public int ConfluenceSyncIntervalHours { get; init; } = 24;
    public string? ConfluenceSyncTime { get; init; } = "04:00"; // Daily at 04:00 AM
    public bool EnableAutoSyncConfluence { get; init; } = true;
    public bool EnableDeepDbmsScanOnDiscovery { get; init; } = false; // Deep scan on-demand or background
}

public record EmailConfig
{
    public string SmtpServer { get; init; } = "mail.example.com";
    public int SmtpPort { get; init; } = 587;
    public string Sender { get; init; } = "postmaster@example.com";
    public string Username { get; init; } = "postmaster@example.corp";
    public string Password { get; init; } = "YourEmailPassword123!";
    public string Recipient { get; init; } = "admin@example.com";
    public bool EnableAlerts { get; init; } = true;
}

public record ActiveDirectoryConfig
{
    public string Domain { get; init; } = "example.corp";
    public string? LdapServer { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public List<string> GroupFilters { get; init; } = ["rdp_1c_*", "1cbases_*"];
    public string V8iBasePath { get; init; } = @"\\fileserver.example.corp\1c_bases";
}

public record ServerCredential
{
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
}

public record DbmsConnectionConfig
{
    public Dictionary<string, ServerCredential> ServerCredentials { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ServerAliases { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string DefaultSqlUsername { get; init; } = "sa";
    public string DefaultSqlPassword { get; init; } = "";
    public string DefaultPgUsername { get; init; } = "postgres";
    public string DefaultPgPassword { get; init; } = "";
    public int ConnectionTimeoutSeconds { get; init; } = 8;
    public bool TrustServerCertificate { get; init; } = true;
}

public record RacConfig
{
    public string RacPath { get; init; } = @"C:\Program Files\1cv8\8.3.25.1445\bin\rac.exe";
    public int TimeoutSeconds { get; init; } = 30;
    public int MaxConcurrency { get; init; } = 16;
}

public record ConsulConfig
{
    public string Url { get; init; } = "http://consul.example.corp:8500";
    public int TimeoutSeconds { get; init; } = 5;
}

public record UiConfig
{
    public bool ShowMetrics { get; init; } = true;
}

public record ServerNodeConfig
{
    public required string Host { get; init; }
    public string Environment { get; init; } = "PROD"; // "PROD" or "DEV"
    public string? ClusterUser { get; init; }
    public string? ClusterPassword { get; init; }
}

public record ClusterDiscoveryConfig
{
    public bool Enabled { get; init; } = true;
    public string DefaultClusterUser { get; init; } = "";
    public string DefaultClusterPassword { get; init; } = "";
    public List<ServerNodeConfig> Servers { get; init; } = [];
}

public record AuditLogConfig
{
    public int RetentionDays { get; init; } = 14;
    public long MaxLogSizeBytes { get; init; } = 1073741824; // 1 GB
    public string LogFilePath { get; init; } = "logs/audit.jsonl";
}

