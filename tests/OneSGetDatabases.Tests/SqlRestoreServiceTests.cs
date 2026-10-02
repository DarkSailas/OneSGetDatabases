using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OneSGetDatabases.Core.Interfaces;
using OneSGetDatabases.Core.Models;
using OneSGetDatabases.Core.Services;
using Xunit;

namespace OneSGetDatabases.Tests;

public class SqlRestoreServiceTests
{
    private readonly Mock<IAuditLogService> _auditLogMock = new();
    private readonly Mock<IInfobaseJobsService> _jobsMock = new();
    private readonly Mock<ILogger<SqlRestoreService>> _loggerMock = new();

    [Fact]
    public async Task StartRestoreAsync_RejectsProdTargetServer_ThrowsInvalidOperation()
    {
        // Arrange
        var config = Options.Create(new SqlRestoreConfig
        {
            AllowedTargetServers = ["dev-s-sql01", "dev-s-sql02"]
        });
        var dbmsConfig = Options.Create(new DbmsConnectionConfig());

        var service = new SqlRestoreService(config, dbmsConfig, _auditLogMock.Object, _jobsMock.Object, _loggerMock.Object);

        var request = new RestoreStartRequest
        {
            TargetServer = "prod-sql01-prod",
            TargetDatabase = "buh_copy",
            SourceServer = "prod-sql01",
            SourceDatabase = "buh_corp",
            PointId = "dummy"
        };

        // Act
        Func<Task> act = async () => await service.StartRestoreAsync(request, "127.0.0.1");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*PROD*");
    }

    [Fact]
    public async Task StartRestoreAsync_RejectsServerNotInAllowedList_ThrowsInvalidOperation()
    {
        // Arrange
        var config = Options.Create(new SqlRestoreConfig
        {
            AllowedTargetServers = ["dev-s-sql01"]
        });
        var dbmsConfig = Options.Create(new DbmsConnectionConfig());

        var service = new SqlRestoreService(config, dbmsConfig, _auditLogMock.Object, _jobsMock.Object, _loggerMock.Object);

        var request = new RestoreStartRequest
        {
            TargetServer = "random-server-09",
            TargetDatabase = "test_db",
            SourceServer = "source-sql",
            SourceDatabase = "source_db",
            PointId = "dummy"
        };

        // Act
        Func<Task> act = async () => await service.StartRestoreAsync(request, "127.0.0.1");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*белом списке*");
    }

    [Fact]
    public async Task GetTimelinePointsAsync_WhenDirectoryDoesNotExist_ReturnsEmptyList()
    {
        // Arrange
        var config = Options.Create(new SqlRestoreConfig
        {
            BackupArchiveRoot = @"C:\NonExistentDirectory_12345"
        });
        var dbmsConfig = Options.Create(new DbmsConnectionConfig());

        var service = new SqlRestoreService(config, dbmsConfig, _auditLogMock.Object, _jobsMock.Object, _loggerMock.Object);

        // Act
        var result = await service.GetTimelinePointsAsync("anyServer", "anyDb");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void BuildConnectionString_UsesDedicatedRestoreCredentials_WhenConfigured()
    {
        // Arrange
        var config = Options.Create(new SqlRestoreConfig
        {
            DefaultRestoreUsername = "sql_restore",
            DefaultRestorePassword = "TestRestorePassword!1"
        });
        var dbmsConfig = Options.Create(new DbmsConnectionConfig
        {
            DefaultSqlUsername = "app_service_user",
            DefaultSqlPassword = "app_service_pass"
        });

        var service = new SqlRestoreService(config, dbmsConfig, _auditLogMock.Object, _jobsMock.Object, _loggerMock.Object);

        // Act
        string connStr = service.BuildConnectionString("dev-s-sql01", "master");
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connStr);

        // Assert
        builder.UserID.Should().Be("sql_restore");
        builder.Password.Should().Be("TestRestorePassword!1");
        builder.IntegratedSecurity.Should().BeFalse();
        builder.InitialCatalog.Should().Be("master");
    }

    [Fact]
    public void BuildConnectionString_UsesServerSpecificCredentials_WhenMatched()
    {
        // Arrange
        var config = Options.Create(new SqlRestoreConfig
        {
            DefaultRestoreUsername = "sql_restore",
            DefaultRestorePassword = "TestRestorePassword!1",
            ServerCredentials =
            [
                new SqlRestoreServerCredential
                {
                    Host = "dev-s-sql02",
                    Username = "special_sql_restore",
                    Password = "special_secret_pass"
                }
            ]
        });
        var dbmsConfig = Options.Create(new DbmsConnectionConfig());

        var service = new SqlRestoreService(config, dbmsConfig, _auditLogMock.Object, _jobsMock.Object, _loggerMock.Object);

        // Act
        string connStr = service.BuildConnectionString("dev-s-sql02", "master");
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connStr);

        // Assert
        builder.UserID.Should().Be("special_sql_restore");
        builder.Password.Should().Be("special_secret_pass");
        builder.IntegratedSecurity.Should().BeFalse();
    }

    [Fact]
    public void BuildConnectionString_FallsBackToIntegratedSecurity_WhenNoCredentialsConfigured()
    {
        // Arrange
        var config = Options.Create(new SqlRestoreConfig
        {
            DefaultRestoreUsername = "",
            DefaultRestorePassword = ""
        });
        var dbmsConfig = Options.Create(new DbmsConnectionConfig
        {
            DefaultSqlUsername = "",
            DefaultSqlPassword = ""
        });

        var service = new SqlRestoreService(config, dbmsConfig, _auditLogMock.Object, _jobsMock.Object, _loggerMock.Object);

        // Act
        string connStr = service.BuildConnectionString("dev-s-sql01", "master");
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connStr);

        // Assert
        builder.IntegratedSecurity.Should().BeTrue();
    }

    [Fact]
    public async Task GetBackupSourcesAsync_FiltersOutExcludedCatalogsAndSystemDatabases()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), "SqlRestoreTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "SQL_MANZANA", "erp_prod"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "PROD-SQL-CDWH", "cdwh_prod"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "PROD-SQL08", "master"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "PROD-SQL08", "SUSDB"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "PROD-SQL08", "DBASQLPerformance"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "PROD-SQL08", "buh_corp", "FULL"));
        File.WriteAllText(Path.Combine(tempRoot, "PROD-SQL08", "buh_corp", "FULL", "buh_corp_FULL.bak"), "x");
        Directory.CreateDirectory(Path.Combine(tempRoot, "PROD-SQL11", "hrm_corp"));
        File.WriteAllText(Path.Combine(tempRoot, "PROD-SQL11", "hrm_corp", "hrm_corp.trn"), "x");
        // Catalogs without backups must not be offered as a restore source
        Directory.CreateDirectory(Path.Combine(tempRoot, "PROD-SQL11", "empty_db", "LOG"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "PROD-SQL11", "notes_only"));
        File.WriteAllText(Path.Combine(tempRoot, "PROD-SQL11", "notes_only", "readme.txt"), "x");

        try
        {
            var config = Options.Create(new SqlRestoreConfig
            {
                BackupArchiveRoot = tempRoot,
                ExcludedCatalogs = ["SQL_MANZANA", "PROD-SQL-CDWH"],
                ExcludedDatabases = ["master", "model", "msdb", "tempdb", "distribution", "SUSDB", "DBADB", "DBASQLPerformance"]
            });
            var dbmsConfig = Options.Create(new DbmsConnectionConfig());

            var service = new SqlRestoreService(config, dbmsConfig, _auditLogMock.Object, _jobsMock.Object, _loggerMock.Object);

            // Act
            var sources = await service.GetBackupSourcesAsync();

            // Assert
            sources.Should().HaveCount(2);
            sources.Select(s => s.DisplayName).Should().BeEquivalentTo([
                "PROD-SQL08 / buh_corp",
                "PROD-SQL11 / hrm_corp"
            ]);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                try { Directory.Delete(tempRoot, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task StartRestoreAsync_RejectsSystemDatabase_ThrowsInvalidOperation()
    {
        // Arrange
        var config = Options.Create(new SqlRestoreConfig
        {
            AllowedTargetServers = ["dev-s-sql01"]
        });
        var dbmsConfig = Options.Create(new DbmsConnectionConfig());

        var service = new SqlRestoreService(config, dbmsConfig, _auditLogMock.Object, _jobsMock.Object, _loggerMock.Object);

        var request = new RestoreStartRequest
        {
            TargetServer = "dev-s-sql01",
            TargetDatabase = "master",
            SourceServer = "prod-sql01",
            SourceDatabase = "master",
            PointId = "dummy"
        };

        // Act
        Func<Task> act = async () => await service.StartRestoreAsync(request, "127.0.0.1");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*системной базы данных*");
    }

    [Theory]
    [InlineData("TestBase*", "TestBase", true)]
    [InlineData("TestBase*", "TestBase_DEV", true)]
    [InlineData("TestBase*", "testbase123", true)]
    [InlineData("TestBase*", "OtherBase", false)]
    [InlineData("tSQLt*", "tSQLt", true)]
    [InlineData("tSQLt*", "tsqlt_framework", true)]
    [InlineData("tSQLt*", "sql_server", false)]
    [InlineData("*_test", "corp_test", true)]
    [InlineData("*_test", "corp_test_dev", false)]
    [InlineData("master", "MASTER", true)]
    [InlineData("ReportServer*", "ReportServerTempDB", true)]
    public void MatchesPattern_ShouldCorrectlyMatchWildcards(string pattern, string value, bool expected)
    {
        SqlRestoreService.MatchesPattern(pattern, value).Should().Be(expected);
    }

    [Fact]
    public void IsDatabaseExcluded_WithWildcardMasks_CorrectlyIdentifiesExcludedDatabases()
    {
        // Arrange
        var config = Options.Create(new SqlRestoreConfig
        {
            ExcludedDatabases = ["TestBase*", "tSQLt*", "ReportServer*"]
        });
        var dbmsConfig = Options.Create(new DbmsConnectionConfig());

        var service = new SqlRestoreService(config, dbmsConfig, _auditLogMock.Object, _jobsMock.Object, _loggerMock.Object);

        // Act & Assert
        service.IsDatabaseExcluded("TestBase_2026").Should().BeTrue();
        service.IsDatabaseExcluded("tsqlt_runner").Should().BeTrue();
        service.IsDatabaseExcluded("ReportServerTempDB").Should().BeTrue();
        service.IsDatabaseExcluded("master").Should().BeTrue(); // default system DB
        service.IsDatabaseExcluded("buh_production").Should().BeFalse();
        service.IsDatabaseExcluded("hrm_corp").Should().BeFalse();
    }

    [Fact]
    public async Task StartRestoreAsync_RejectsDatabaseMatchingWildcardMask_ThrowsInvalidOperation()
    {
        // Arrange
        var config = Options.Create(new SqlRestoreConfig
        {
            AllowedTargetServers = ["dev-s-sql01"],
            ExcludedDatabases = ["TestBase*", "tSQLt*"]
        });
        var dbmsConfig = Options.Create(new DbmsConnectionConfig());

        var service = new SqlRestoreService(config, dbmsConfig, _auditLogMock.Object, _jobsMock.Object, _loggerMock.Object);

        var request = new RestoreStartRequest
        {
            TargetServer = "dev-s-sql01",
            TargetDatabase = "TestBase_DevCopy",
            SourceServer = "prod-sql01",
            SourceDatabase = "buh_source",
            PointId = "dummy"
        };

        // Act
        Func<Task> act = async () => await service.StartRestoreAsync(request, "127.0.0.1");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*системной базы данных*");
    }

    [Fact]
    public async Task GetTimelinePointsAsync_FindsNestedOlaStyleFolders()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "SqlRestoreTests_" + Guid.NewGuid().ToString("N"));
        string db = Path.Combine(tempRoot, "SRV01", "buh_corp");
        Directory.CreateDirectory(Path.Combine(db, "FULL", "2026-09"));
        Directory.CreateDirectory(Path.Combine(db, "LOG"));
        File.WriteAllText(Path.Combine(db, "FULL", "2026-09", "SRV01_buh_corp_FULL_20260901_010000.bak"), "x");
        File.WriteAllText(Path.Combine(db, "LOG", "SRV01_buh_corp_LOG_20260901_020000.trn"), "x");
        File.SetLastWriteTime(Path.Combine(db, "LOG", "SRV01_buh_corp_LOG_20260901_020000.trn"), DateTime.Now.AddHours(1));

        try
        {
            var service = new SqlRestoreService(
                Options.Create(new SqlRestoreConfig { BackupArchiveRoot = tempRoot }),
                Options.Create(new DbmsConnectionConfig()),
                _auditLogMock.Object, _jobsMock.Object, _loggerMock.Object);

            var points = await service.GetTimelinePointsAsync("SRV01", "buh_corp");

            points.Select(p => p.Type).Should().BeEquivalentTo([BackupType.Full, BackupType.Log]);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task DiagnoseCatalogAsync_ReportsForeignExtensionsAndSubfolders()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "SqlRestoreTests_" + Guid.NewGuid().ToString("N"));
        string db = Path.Combine(tempRoot, "SRV01", "buh_corp");
        Directory.CreateDirectory(Path.Combine(db, "archive"));
        File.WriteAllText(Path.Combine(db, "archive", "buh_corp_20260901.bak.7z"), "x");

        try
        {
            var service = new SqlRestoreService(
                Options.Create(new SqlRestoreConfig { BackupArchiveRoot = tempRoot }),
                Options.Create(new DbmsConnectionConfig()),
                _auditLogMock.Object, _jobsMock.Object, _loggerMock.Object);

            var points = await service.GetTimelinePointsAsync("SRV01", "buh_corp");
            var diag = await service.DiagnoseCatalogAsync("SRV01", "buh_corp");

            points.Should().BeEmpty();
            diag.Exists.Should().BeTrue();
            diag.TotalFiles.Should().Be(1);
            diag.Extensions.Should().ContainKey(".7z");
            diag.SubDirectories.Should().Contain("archive");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(@"C:\a\db", @"C:\a\db", null)]
    [InlineData(@"C:\a\db", @"C:\a\db\FULL", "FULL")]
    [InlineData(@"C:\a\db", @"C:\a\db\log\2026-09", "log")]
    [InlineData(@"C:\a\db", @"C:\a\db\2026\DIFF", "DIFF")]
    public void FindTypeFolder_FindsTypeFolderAnywhereOnPath(string dbPath, string dir, string? expected)
    {
        SqlRestoreService.FindTypeFolder(dbPath, dir).Should().Be(expected);
    }

    [Fact]
    public async Task GetRestorePlanPreviewAsync_ForLogPoint_ListsFullDiffAndLogsAfterDiff()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "SqlRestoreTests_" + Guid.NewGuid().ToString("N"));
        string db = Path.Combine(tempRoot, "SRV01", "buh_corp");
        Directory.CreateDirectory(db);
        var t0 = new DateTime(2026, 9, 28, 1, 0, 0);
        void Make(string name, DateTime when)
        {
            string path = Path.Combine(db, name);
            File.WriteAllText(path, "x");
            File.SetLastWriteTime(path, when);
        }
        Make("buh_FULL.bak", t0);
        Make("buh_log1.trn", t0.AddHours(2));
        Make("buh_DIFF.diff", t0.AddHours(12));
        Make("buh_log2.trn", t0.AddHours(13));
        Make("buh_log3.trn", t0.AddHours(14));
        Make("buh_log4.trn", t0.AddHours(15));

        try
        {
            var service = new SqlRestoreService(
                Options.Create(new SqlRestoreConfig { BackupArchiveRoot = tempRoot }),
                Options.Create(new DbmsConnectionConfig()),
                _auditLogMock.Object, _jobsMock.Object, _loggerMock.Object);

            var points = await service.GetTimelinePointsAsync("SRV01", "buh_corp");
            var target = points.Single(p => p.FilePaths[0].EndsWith("buh_log3.trn"));

            var plan = await service.GetRestorePlanPreviewAsync("SRV01", "buh_corp", target.Id);

            plan.Error.Should().BeNull();
            plan.Steps.Select(s => s.Type).Should().Equal(BackupType.Full, BackupType.Differential, BackupType.Log);
            plan.Steps[2].BackupCount.Should().Be(2); // log2 and log3: after the diff, up to the target
            plan.TotalFiles.Should().Be(4);
            plan.TargetMoment.Should().Be(t0.AddHours(14).ToString("dd.MM.yyyy HH:mm:ss"));
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task GetRestorePlanPreviewAsync_ForLogWithoutFull_ReturnsError()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "SqlRestoreTests_" + Guid.NewGuid().ToString("N"));
        string db = Path.Combine(tempRoot, "SRV01", "buh_corp");
        Directory.CreateDirectory(db);
        File.WriteAllText(Path.Combine(db, "buh_log.trn"), "x");

        try
        {
            var service = new SqlRestoreService(
                Options.Create(new SqlRestoreConfig { BackupArchiveRoot = tempRoot }),
                Options.Create(new DbmsConnectionConfig()),
                _auditLogMock.Object, _jobsMock.Object, _loggerMock.Object);

            var points = await service.GetTimelinePointsAsync("SRV01", "buh_corp");
            var plan = await service.GetRestorePlanPreviewAsync("SRV01", "buh_corp", points[0].Id);

            plan.Error.Should().Contain("нет полного бэкапа");
            plan.Steps.Should().BeEmpty();
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }
}
