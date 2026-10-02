namespace OneSGetDatabases.Core.Interfaces;

public record InfobaseJobsResult(bool Success, string Message);

public interface IInfobaseJobsService
{
    /// <summary>
    /// Включает «Блокировку регламентных заданий» у информационной базы 1С, опубликованной на указанной базе СУБД.
    /// </summary>
    Task<InfobaseJobsResult> DenyScheduledJobsAsync(
        string sqlServer,
        string sqlDatabase,
        string? infobaseName,
        string? clusterHost,
        CancellationToken cancellationToken = default);
}
