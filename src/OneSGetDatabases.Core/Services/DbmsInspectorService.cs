using System.Collections.Concurrent;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using OneSGetDatabases.Core.Helpers;
using OneSGetDatabases.Core.Interfaces;
using OneSGetDatabases.Core.Models;

namespace OneSGetDatabases.Core.Services;

public class DbmsInspectorService : IDbmsInspectorService
{
    private const string ServerFilesCacheFileName = "dbms_files_cache.json";
    private const string DatabaseDetailsCacheFileName = "dbms_details_cache.json";

    private readonly ILogger<DbmsInspectorService> _logger;
    private readonly DbmsConnectionConfig _config;
    private readonly ConcurrentDictionary<string, (DateTime CachedAt, Dictionary<string, List<DbmsFileItem>> Files)> _serverFilesCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (DateTime CachedAt, DbmsDetails Details)> _databaseDetailsCache = new(StringComparer.OrdinalIgnoreCase);

    public DbmsInspectorService(IOptions<DbmsConnectionConfig> config, ILogger<DbmsInspectorService> logger)
    {
        _logger = logger;
        _config = config.Value;

        // Load pre-existing server files cache from disk
        var diskServerFiles = PersistentCacheHelper.LoadFromDisk<Dictionary<string, Dictionary<string, List<DbmsFileItem>>>>(ServerFilesCacheFileName);
        if (diskServerFiles != null)
        {
            foreach (var kvp in diskServerFiles)
            {
                _serverFilesCache[kvp.Key] = (DateTime.UtcNow, kvp.Value);
            }
        }

        // Load pre-existing database details cache from disk
        var diskDetails = PersistentCacheHelper.LoadFromDisk<Dictionary<string, DbmsDetails>>(DatabaseDetailsCacheFileName);
        if (diskDetails != null)
        {
            foreach (var kvp in diskDetails)
            {
                _databaseDetailsCache[kvp.Key] = (DateTime.UtcNow, kvp.Value);
            }
        }
    }

    public async Task<DbmsDetails> InspectDatabaseAsync(
        string dbServer,
        string dbName,
        string? dbmsType = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dbServer) || string.IsNullOrWhiteSpace(dbName) || dbServer.Equals("Неизвестно", StringComparison.OrdinalIgnoreCase))
        {
            return new DbmsDetails
            {
                DbServer = dbServer,
                DatabaseName = dbName,
                Error = "Не указан сервер или имя базы данных"
            };
        }

        string cacheKey = $"{ServerNameHelper.NormalizeServerName(dbServer)}:{dbName.ToLowerInvariant()}";
        if (_databaseDetailsCache.TryGetValue(cacheKey, out var cached) && (DateTime.UtcNow - cached.CachedAt).TotalMinutes < 60 && cached.Details.IsSuccess)
        {
            return cached.Details;
        }

        bool isPostgres = (dbmsType != null && dbmsType.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
                          || dbServer.Contains(":5432")
                          || dbServer.Contains("pg", StringComparison.OrdinalIgnoreCase);

        DbmsDetails result;
        if (isPostgres)
        {
            result = await InspectPostgresAsync(dbServer, dbName, cancellationToken);
        }
        else
        {
            result = await InspectSqlServerAsync(dbServer, dbName, cancellationToken);
        }

        if (result.IsSuccess)
        {
            _databaseDetailsCache[cacheKey] = (DateTime.UtcNow, result);
            var snapshot = _databaseDetailsCache.ToDictionary(k => k.Key, v => v.Value.Details, StringComparer.OrdinalIgnoreCase);
            PersistentCacheHelper.SaveToDisk(DatabaseDetailsCacheFileName, snapshot);
        }

        return result;
    }

    public async Task<Dictionary<string, List<DbmsFileItem>>> GetServerAllDatabaseFilesAsync(
        string dbServer,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dbServer) || dbServer.Equals("Неизвестно", StringComparison.OrdinalIgnoreCase))
        {
            return new Dictionary<string, List<DbmsFileItem>>(StringComparer.OrdinalIgnoreCase);
        }

        string normalizedHost = ServerNameHelper.NormalizeServerName(dbServer);

        // Instant Cache Hit (< 1ms)
        if (_serverFilesCache.TryGetValue(normalizedHost, out var cached) && (DateTime.UtcNow - cached.CachedAt).TotalMinutes < 10)
        {
            return cached.Files;
        }

        bool isPostgres = dbServer.Contains(":5432") || dbServer.Contains("pg", StringComparison.OrdinalIgnoreCase);
        if (isPostgres)
        {
            return await GetPostgresServerAllFilesAsync(dbServer, cancellationToken);
        }

        var result = new Dictionary<string, List<DbmsFileItem>>(StringComparer.OrdinalIgnoreCase);
        SqlConnection? conn = null;

        try
        {
            conn = await CreateSqlServerConnectionAsync(dbServer, cancellationToken);
            if (conn == null)
            {
                // Cache failed/offline attempt for 5 minutes to prevent recurring connection timeouts on filter operations
                _serverFilesCache[normalizedHost] = (DateTime.UtcNow, result);
                return result;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            // Strategy 1: Fast query from sys.master_files using INNER JOIN sys.databases
            const string masterQuery = @"
SELECT 
    d.name AS db_name,
    mf.name AS file_name,
    mf.physical_name,
    mf.type_desc,
    CAST(mf.size AS BIGINT) * 8192 AS size_bytes
FROM sys.master_files mf
INNER JOIN sys.databases d ON mf.database_id = d.database_id
WHERE mf.database_id > 4
ORDER BY d.name, mf.type_desc, mf.name;";

            try
            {
                using var cmd = new SqlCommand(masterQuery, conn);
                using var reader = await cmd.ExecuteReaderAsync(cts.Token);

                while (await reader.ReadAsync(cts.Token))
                {
                    if (reader.IsDBNull(0)) continue;
                    string dbName = reader.GetString(0);
                    string fileName = reader.GetString(1);
                    string physicalPath = reader.GetString(2);
                    string typeDesc = reader.GetString(3);
                    long sizeBytes = reader.GetInt64(4);

                    if (!result.TryGetValue(dbName, out var list))
                    {
                        list = new List<DbmsFileItem>();
                        result[dbName] = list;
                    }

                    list.Add(new DbmsFileItem
                    {
                        FileName = fileName,
                        PhysicalPath = physicalPath,
                        FileType = typeDesc,
                        SizeBytes = sizeBytes
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "sys.master_files query failed on {Server}", dbServer);
            }

            // Strategy 2: If sys.master_files returned 0 rows (restricted permissions on master), query accessible databases via batch
            if (result.Count == 0)
            {
                var accessibleDbs = new List<string>();
                try
                {
                    const string dbListQuery = "SELECT name FROM sys.databases WHERE database_id > 4 AND state = 0;";
                    using var cmd = new SqlCommand(dbListQuery, conn);
                    using var reader = await cmd.ExecuteReaderAsync(cts.Token);
                    while (await reader.ReadAsync(cts.Token))
                    {
                        accessibleDbs.Add(reader.GetString(0));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "sys.databases query failed on {Server}", dbServer);
                }

                foreach (var dbName in accessibleDbs)
                {
                    try
                    {
                        string dbQuery = $@"
SELECT 
    name,
    physical_name,
    type_desc,
    CAST(size AS BIGINT) * 8192 AS size_bytes
FROM [{dbName.Replace("]", "]]")}].sys.database_files;";

                        using var cmd = new SqlCommand(dbQuery, conn);
                        using var reader = await cmd.ExecuteReaderAsync(cts.Token);
                        var list = new List<DbmsFileItem>();

                        while (await reader.ReadAsync(cts.Token))
                        {
                            string fileName = reader.GetString(0);
                            string physicalPath = reader.GetString(1);
                            string typeDesc = reader.GetString(2);
                            long sizeBytes = reader.GetInt64(3);

                            list.Add(new DbmsFileItem
                            {
                                FileName = fileName,
                                PhysicalPath = physicalPath,
                                FileType = typeDesc,
                                SizeBytes = sizeBytes
                            });
                        }

                        if (list.Count > 0)
                        {
                            result[dbName] = list;
                        }
                    }
                    catch
                    {
                        // Database might be restricted
                    }
                }
            }

            _serverFilesCache[normalizedHost] = (DateTime.UtcNow, result);
            if (result.Count > 0)
            {
                var filesSnapshot = _serverFilesCache.Where(kvp => kvp.Value.Files.Count > 0).ToDictionary(k => k.Key, v => v.Value.Files, StringComparer.OrdinalIgnoreCase);
                PersistentCacheHelper.SaveToDisk(ServerFilesCacheFileName, filesSnapshot);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to query database files on {Server}", dbServer);
            _serverFilesCache[normalizedHost] = (DateTime.UtcNow, result);
        }
        finally
        {
            if (conn != null) await conn.DisposeAsync();
        }

        return result;
    }

    private async Task<Dictionary<string, List<DbmsFileItem>>> GetPostgresServerAllFilesAsync(
        string dbServer,
        CancellationToken cancellationToken)
    {
        string normalizedHost = ServerNameHelper.NormalizeServerName(dbServer);
        var result = new Dictionary<string, List<DbmsFileItem>>(StringComparer.OrdinalIgnoreCase);
        NpgsqlConnection? conn = null;

        try
        {
            string host = dbServer.Contains(':') ? dbServer.Split(':')[0] : dbServer;
            int port = dbServer.Contains(':') && int.TryParse(dbServer.Split(':')[1], out int p) ? p : 5432;
            string user = !string.IsNullOrEmpty(_config.DefaultPgUsername) ? _config.DefaultPgUsername : "postgres";
            string pwd = _config.DefaultPgPassword;

            if (_config.ServerCredentials.TryGetValue(dbServer, out var cred) && !string.IsNullOrEmpty(cred.Username))
            {
                user = cred.Username;
                pwd = cred.Password;
            }

            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = host,
                Port = port,
                Database = "postgres",
                Username = user,
                Password = pwd,
                Timeout = 2,
                CommandTimeout = 8,
                ApplicationName = "OneSGetDatabases.Inspector",
                SslMode = SslMode.Prefer
            };

            conn = new NpgsqlConnection(builder.ConnectionString);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            await conn.OpenAsync(cts.Token);

            const string query = @"
SELECT 
    datname,
    pg_database_size(datname) AS size_bytes
FROM pg_database
WHERE datistemplate = false;";

            using var cmd = new NpgsqlCommand(query, conn);
            using var reader = await cmd.ExecuteReaderAsync(cts.Token);
            while (await reader.ReadAsync(cts.Token))
            {
                string dbName = reader.GetString(0);
                long size = !reader.IsDBNull(1) ? reader.GetInt64(1) : 0;

                result[dbName] = new List<DbmsFileItem>
                {
                    new()
                    {
                        FileName = dbName,
                        PhysicalPath = $"PostgreSQL Data Directory / PGDATA / {dbName}",
                        FileType = "DATA",
                        SizeBytes = size
                    }
                };
            }

            _serverFilesCache[normalizedHost] = (DateTime.UtcNow, result);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to query PostgreSQL server-wide sizes on {Server}", dbServer);
            _serverFilesCache[normalizedHost] = (DateTime.UtcNow, result);
        }
        finally
        {
            if (conn != null) await conn.DisposeAsync();
        }

        return result;
    }

    private async Task<DbmsDetails> InspectSqlServerAsync(
        string dbServer,
        string dbName,
        CancellationToken cancellationToken)
    {
        SqlConnection? conn = null;
        try
        {
            conn = await CreateSqlServerConnectionAsync(dbServer, cancellationToken);
            if (conn == null)
            {
                return new DbmsDetails
                {
                    DbServer = dbServer,
                    DatabaseName = dbName,
                    Error = "Не удалось установить соединение с сервером СУБД MS SQL"
                };
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            // 1. Database metadata
            DateTime? createdDate = null;
            string? owner = null;
            string? state = "ONLINE";
            string? recoveryModel = null;
            string? collation = null;
            int? compatLevel = null;

            const string dbQuery = @"
SELECT 
    d.create_date,
    d.state_desc,
    d.recovery_model_desc,
    d.collation_name,
    d.compatibility_level,
    SUSER_SNAME(d.owner_sid) AS owner_name
FROM sys.databases d 
WHERE d.name = @dbName";

            try
            {
                using var cmd = new SqlCommand(dbQuery, conn);
                cmd.Parameters.AddWithValue("@dbName", dbName);
                using var reader = await cmd.ExecuteReaderAsync(cts.Token);
                if (await reader.ReadAsync(cts.Token))
                {
                    if (!reader.IsDBNull(0)) createdDate = reader.GetDateTime(0);
                    if (!reader.IsDBNull(1)) state = reader.GetString(1);
                    if (!reader.IsDBNull(2)) recoveryModel = reader.GetString(2);
                    if (!reader.IsDBNull(3)) collation = reader.GetString(3);
                    if (!reader.IsDBNull(4)) compatLevel = reader.GetByte(4);
                    if (!reader.IsDBNull(5)) owner = reader.GetString(5);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "sys.databases query failed on {Server} for {Db}", dbServer, dbName);
            }

            // 2. Database files (Dual-strategy: sys.master_files, fallback to sys.database_files)
            var files = new List<DbmsFileItem>();
            long totalBytes = 0;

            const string filesMasterQuery = @"
SELECT 
    mf.name,
    mf.physical_name,
    mf.type_desc,
    CAST(mf.size AS BIGINT) * 8192 AS size_bytes
FROM sys.master_files mf
INNER JOIN sys.databases d ON mf.database_id = d.database_id
WHERE d.name = @dbName";

            try
            {
                using var cmd = new SqlCommand(filesMasterQuery, conn);
                cmd.Parameters.AddWithValue("@dbName", dbName);
                using var reader = await cmd.ExecuteReaderAsync(cts.Token);
                while (await reader.ReadAsync(cts.Token))
                {
                    string fName = reader.GetString(0);
                    string physPath = reader.GetString(1);
                    string typeDesc = reader.GetString(2);
                    long sBytes = reader.GetInt64(3);

                    totalBytes += sBytes;
                    files.Add(new DbmsFileItem
                    {
                        FileName = fName,
                        PhysicalPath = physPath,
                        FileType = typeDesc,
                        SizeBytes = sBytes
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "sys.master_files failed on {Server} for {Db}", dbServer, dbName);
            }

            // Fallback: USE [dbName]; SELECT FROM sys.database_files
            if (files.Count == 0)
            {
                try
                {
                    string dbFilesQuery = $@"
SELECT 
    name,
    physical_name,
    type_desc,
    CAST(size AS BIGINT) * 8192 AS size_bytes
FROM [{dbName.Replace("]", "]]")}].sys.database_files;";

                    using var cmd = new SqlCommand(dbFilesQuery, conn);
                    using var reader = await cmd.ExecuteReaderAsync(cts.Token);
                    while (await reader.ReadAsync(cts.Token))
                    {
                        string fName = reader.GetString(0);
                        string physPath = reader.GetString(1);
                        string typeDesc = reader.GetString(2);
                        long sBytes = reader.GetInt64(3);

                        totalBytes += sBytes;
                        files.Add(new DbmsFileItem
                        {
                            FileName = fName,
                            PhysicalPath = physPath,
                            FileType = typeDesc,
                            SizeBytes = sBytes
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "sys.database_files failed on {Server} for {Db}", dbServer, dbName);
                }
            }

            // 3. Database users & roles
            var permissions = new List<DbmsUserPermission>();
            try
            {
                string usersQuery = $@"
SELECT 
    dp.name AS principal_name,
    dp.type_desc,
    ISNULL(r.name, '') AS role_name
FROM [{dbName.Replace("]", "]]")}].sys.database_principals dp
LEFT JOIN [{dbName.Replace("]", "]]")}].sys.database_role_members rm ON dp.principal_id = rm.member_principal_id
LEFT JOIN [{dbName.Replace("]", "]]")}].sys.database_principals r ON rm.role_principal_id = r.principal_id
WHERE dp.type IN ('S', 'U', 'G', 'R') 
  AND dp.name NOT IN ('public', 'guest', 'sys', 'INFORMATION_SCHEMA')
ORDER BY dp.name;";

                using var cmd = new SqlCommand(usersQuery, conn);
                using var reader = await cmd.ExecuteReaderAsync(cts.Token);
                while (await reader.ReadAsync(cts.Token))
                {
                    string pName = reader.GetString(0);
                    string pType = reader.GetString(1);
                    string role = reader.GetString(2);

                    permissions.Add(new DbmsUserPermission
                    {
                        PrincipalName = pName,
                        PrincipalType = pType,
                        RoleOrPermission = !string.IsNullOrEmpty(role) ? role : "Пользователь базы",
                        State = "GRANT"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Users query failed on {Server} for {Db}", dbServer, dbName);
            }

            // 4. Last Backup Date
            DateTime? lastBackup = null;
            try
            {
                const string backupQuery = @"
SELECT TOP 1 backup_finish_date
FROM msdb.dbo.backupset
WHERE database_name = @dbName AND type = 'D'
ORDER BY backup_finish_date DESC";

                using var cmd = new SqlCommand(backupQuery, conn);
                cmd.Parameters.AddWithValue("@dbName", dbName);
                var res = await cmd.ExecuteScalarAsync(cts.Token);
                if (res != null && res != DBNull.Value)
                {
                    lastBackup = Convert.ToDateTime(res);
                }
            }
            catch
            {
                // msdb access might be restricted
            }

            return new DbmsDetails
            {
                DbServer = dbServer,
                DatabaseName = dbName,
                DbmsType = "MSSQL",
                TotalSizeBytes = totalBytes,
                CreatedDate = createdDate,
                LastBackupDate = lastBackup,
                Owner = owner,
                State = state,
                RecoveryModel = recoveryModel,
                Collation = collation,
                CompatibilityLevel = compatLevel,
                Files = files,
                Permissions = permissions
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to inspect MS SQL database {Db} on {Server}", dbName, dbServer);
            return new DbmsDetails
            {
                DbServer = dbServer,
                DatabaseName = dbName,
                DbmsType = "MSSQL",
                Error = ex.Message
            };
        }
        finally
        {
            if (conn != null)
            {
                await conn.DisposeAsync();
            }
        }
    }

    private async Task<SqlConnection?> CreateSqlServerConnectionAsync(string dbServer, CancellationToken cancellationToken)
    {
        int timeout = 4;
        var attempts = new List<Action<SqlConnectionStringBuilder>>();

        string user = _config.DefaultSqlUsername;
        string pwd = _config.DefaultSqlPassword;

        if (_config.ServerCredentials.TryGetValue(dbServer, out var cred) && !string.IsNullOrEmpty(cred.Username))
        {
            user = cred.Username;
            pwd = cred.Password;
        }

        // Attempt 1: Direct SQL Login (monitoring_user)
        if (!string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(pwd))
        {
            attempts.Add(b =>
            {
                b.IntegratedSecurity = false;
                b.UserID = user;
                b.Password = pwd;
            });
        }

        // Attempt 2: Windows Integrated Security
        attempts.Add(b =>
        {
            b.IntegratedSecurity = true;
        });

        string targetHost = dbServer;
        if (_config.ServerAliases.TryGetValue(dbServer, out var alias) && !string.IsNullOrWhiteSpace(alias))
        {
            targetHost = alias;
        }

        foreach (var configure in attempts)
        {
            try
            {
                var builder = new SqlConnectionStringBuilder
                {
                    DataSource = targetHost,
                    InitialCatalog = "master",
                    ConnectTimeout = timeout,
                    TrustServerCertificate = true,
                    MultiSubnetFailover = true,
                    ApplicationName = "OneSGetDatabases.Inspector"
                };

                configure(builder);

                var conn = new SqlConnection(builder.ConnectionString);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(timeout + 1));

                await conn.OpenAsync(cts.Token);
                return conn;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("SQL connection attempt to {Server} failed: {Msg}", dbServer, ex.Message);
            }
        }

        return null;
    }

    private async Task<DbmsDetails> InspectPostgresAsync(
        string dbServer,
        string dbName,
        CancellationToken cancellationToken)
    {
        NpgsqlConnection? conn = null;
        try
        {
            string host = dbServer.Contains(':') ? dbServer.Split(':')[0] : dbServer;
            int port = dbServer.Contains(':') && int.TryParse(dbServer.Split(':')[1], out int p) ? p : 5432;

            string user = !string.IsNullOrEmpty(_config.DefaultPgUsername) ? _config.DefaultPgUsername : "postgres";
            string pwd = _config.DefaultPgPassword;

            if (_config.ServerCredentials.TryGetValue(dbServer, out var cred) && !string.IsNullOrEmpty(cred.Username))
            {
                user = cred.Username;
                pwd = cred.Password;
            }

            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = host,
                Port = port,
                Database = dbName,
                Username = user,
                Password = pwd,
                Timeout = 2,
                CommandTimeout = 8,
                ApplicationName = "OneSGetDatabases.Inspector",
                SslMode = SslMode.Prefer
            };

            conn = new NpgsqlConnection(builder.ConnectionString);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            await conn.OpenAsync(cts.Token);

            long totalBytes = 0;
            string? owner = null;
            string? collation = null;

            const string sizeQuery = @"
SELECT 
    pg_database_size(d.datname) AS size_bytes,
    pg_catalog.pg_get_userbyid(d.datdba) AS owner_name,
    d.datcollate
FROM pg_database d 
WHERE d.datname = @dbName";

            using (var cmd = new NpgsqlCommand(sizeQuery, conn))
            {
                cmd.Parameters.AddWithValue("dbName", dbName);
                using var reader = await cmd.ExecuteReaderAsync(cts.Token);
                if (await reader.ReadAsync(cts.Token))
                {
                    if (!reader.IsDBNull(0)) totalBytes = reader.GetInt64(0);
                    if (!reader.IsDBNull(1)) owner = reader.GetString(1);
                    if (!reader.IsDBNull(2)) collation = reader.GetString(2);
                }
            }

            var files = new List<DbmsFileItem>
            {
                new()
                {
                    FileName = dbName,
                    PhysicalPath = $"PostgreSQL Data Directory / PGDATA / {dbName}",
                    FileType = "DATA",
                    SizeBytes = totalBytes
                }
            };

            var permissions = new List<DbmsUserPermission>();
            try
            {
                const string usersQuery = @"
SELECT 
    r.rolname,
    CASE WHEN r.rolsuper THEN 'Superuser' ELSE 'User' END,
    'CONNECT' AS permission
FROM pg_roles r
WHERE r.rolname NOT LIKE 'pg_%'
ORDER BY r.rolname";

                using var cmd = new NpgsqlCommand(usersQuery, conn);
                using var reader = await cmd.ExecuteReaderAsync(cts.Token);
                while (await reader.ReadAsync(cts.Token))
                {
                    permissions.Add(new DbmsUserPermission
                    {
                        PrincipalName = reader.GetString(0),
                        PrincipalType = reader.GetString(1),
                        RoleOrPermission = reader.GetString(2),
                        State = "GRANT"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to query PG roles for {Db}", dbName);
            }

            return new DbmsDetails
            {
                DbServer = dbServer,
                DatabaseName = dbName,
                DbmsType = "PostgreSQL",
                TotalSizeBytes = totalBytes,
                Owner = owner,
                State = "ONLINE",
                Collation = collation,
                Files = files,
                Permissions = permissions
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to inspect PostgreSQL database {Db} on {Server}", dbName, dbServer);
            return new DbmsDetails
            {
                DbServer = dbServer,
                DatabaseName = dbName,
                DbmsType = "PostgreSQL",
                Error = ex.Message
            };
        }
        finally
        {
            if (conn != null)
            {
                await conn.DisposeAsync();
            }
        }
    }
}
