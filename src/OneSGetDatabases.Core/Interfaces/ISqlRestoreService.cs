using OneSGetDatabases.Core.Models;

namespace OneSGetDatabases.Core.Interfaces;

public interface ISqlRestoreService
{
    ValueTask<IReadOnlyList<BackupCatalogSource>> GetBackupSourcesAsync(CancellationToken ct = default);
    ValueTask<RestorePlanPreview> GetRestorePlanPreviewAsync(string sourceServer, string sourceDatabase, string pointId, CancellationToken ct = default);
    ValueTask<BackupCatalogDiagnostics> DiagnoseCatalogAsync(string sourceServer, string sourceDatabase, CancellationToken ct = default);
    ValueTask<IReadOnlyList<RestoreTimelinePoint>> GetTimelinePointsAsync(string sourceServer, string sourceDatabase, CancellationToken ct = default);
    ValueTask<RestoreOperationState> StartRestoreAsync(RestoreStartRequest request, string clientIp, CancellationToken ct = default);
    RestoreOperationState? GetOperationStatus(string operationId);
    IReadOnlyList<RestoreOperationState> GetActiveOperations();
    bool IsDatabaseExcluded(string dbName);
    bool IsCatalogExcluded(string catalogName);
    IReadOnlyList<string> GetExcludedDatabases();
    IReadOnlyList<string> GetExcludedCatalogs();
}
