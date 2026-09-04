using OneSGetDatabases.Core.Models;

namespace OneSGetDatabases.Core.Interfaces;

public record RacResult(string Output, string Error, int ExitCode)
{
    public bool Success => ExitCode == 0;
}

public interface IRacService
{
    string RacPath { get; }
    Task<RacResult> RunRacAsync(string args, int? timeoutSeconds = null, CancellationToken cancellationToken = default);
    Task<string> RunSmartRacAsync(string rasAddress, string mode, string action, string? clusterId = null, string? adminUser = null, string? adminPwd = null, CancellationToken cancellationToken = default);
}

public interface ICimService
{
    ValueTask<(string ServiceUser, string ServiceName, string ClusterPath, string? Error)> GetServiceInfoAsync(string hostName, int port, CancellationToken cancellationToken = default);
}

public interface IActiveDirectoryService
{
    Task RefreshCacheAsync(CancellationToken cancellationToken = default);
    bool HasGroup(string groupName);
    string ResolveAccessGroup(string infobaseName);
    (string RaGroup, string OneCGroup, string V8iFile) ResolveSaInfoGroups(string infobaseName, string platform);
    Task<AdGroupDetails> GetGroupMembersAsync(string groupName, CancellationToken cancellationToken = default);
}

public interface IConsulService
{
    Task<(string ConsulName, string SqlServer)> ResolveServiceAsync(string infobaseName, CancellationToken cancellationToken = default);
}

public interface IDbmsInspectorService
{
    Task<DbmsDetails> InspectDatabaseAsync(string dbServer, string dbName, string? dbmsType = null, CancellationToken cancellationToken = default);
    Task<Dictionary<string, List<DbmsFileItem>>> GetServerAllDatabaseFilesAsync(string dbServer, CancellationToken cancellationToken = default);
}

public interface IConfluencePublisher
{
    Task<bool> PublishAllAsync(IReadOnlyList<InfoBaseItem> devBases, IReadOnlyList<InfoBaseItem> prodBases, CancellationToken cancellationToken = default);
}

public interface INotificationService
{
    Task SendErrorAlertAsync(string subject, IReadOnlyList<string> errors, CancellationToken cancellationToken = default);
}

public interface IDatabaseCacheService
{
    IReadOnlyList<InfoBaseItem> GetAll();
    IReadOnlyList<InfoBaseItem> GetDev();
    IReadOnlyList<InfoBaseItem> GetProd();
    InfoBaseItem? Find(string environment, string cluster, string name);
    void Update(List<InfoBaseItem> allBases);
    DateTime LastScanTime { get; }
    DateTime LastConfluenceSyncTime { get; }
    void MarkConfluenceSyncCompleted();
}

public class ClusterDiscoveryLogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string TimestampLocal => Timestamp.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
    public string Server { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public string Environment { get; set; } = "";
    public string Stage { get; set; } = "";
    public string Level { get; set; } = "Error"; // Error, Warning, Info
    public string Message { get; set; } = "";
    public string Details { get; set; } = "";
}

public interface IInfobaseDiscoveryService
{
    Task<List<InfoBaseItem>> DiscoverAllAsync(CancellationToken cancellationToken = default);
    Task<List<InfoBaseItem>> DiscoverClusterAsync(ClusterConfig cluster, CancellationToken cancellationToken = default);
    IReadOnlyList<ClusterHealthInfo> GetClusterHealthStatuses();
    IReadOnlyList<ClusterDiscoveryLogEntry> GetDiscoveryLogs();
    void RecordDiscoveryLog(string server, string host, int port, string environment, string stage, string level, string message, string details = "");
}

public interface IClusterDiscoveryEngine
{
    ValueTask<IReadOnlyList<ClusterConfig>> DiscoverAllClustersAsync(CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<ClusterConfig>> DiscoverHostClustersAsync(ServerNodeConfig node, CancellationToken cancellationToken = default);
    IReadOnlyList<ClusterDiscoveryLogEntry> GetLastDiscoveryLogs();
}

public interface IAuditLogService
{
    Task LogActionAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditLogEntry>> GetEntriesAsync(int limit = 500, string? search = null, CancellationToken cancellationToken = default);
}

public interface IOneSServiceManager
{
    ValueTask<IReadOnlyList<OneSServiceInfo>> GetAllServicesStatusAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
    Task<ServiceActionResult> ExecuteServiceActionAsync(ServiceActionRequest request, string clientIp, CancellationToken cancellationToken = default);
}


