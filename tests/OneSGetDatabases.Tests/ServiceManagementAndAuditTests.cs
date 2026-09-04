using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OneSGetDatabases.Core.Models;
using OneSGetDatabases.Core.Services;
using Xunit;

namespace OneSGetDatabases.Tests;

public class ServiceManagementAndAuditTests : IDisposable
{
    private readonly string _tempDir;

    public ServiceManagementAndAuditTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "OneSTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public void ToUncPath_ConvertsLocalDrivePathToAdminShare()
    {
        // Act
        string unc = OneSServiceManager.ToUncPath("app-node01", @"D:\1C\srvinfo_3040");

        // Assert
        unc.Should().Be(@"\\app-node01\D$\1C\srvinfo_3040");
    }

    [Fact]
    public void ToUncPath_PreservesExistingUncPath()
    {
        // Act
        string unc = OneSServiceManager.ToUncPath("app-node01", @"\\remote\share\srvinfo");

        // Assert
        unc.Should().Be(@"\\remote\share\srvinfo");
    }

    [Fact]
    public void CleanSnccntxDirectories_OnlyDeletesSnccntxFoldersAndPreservesConfigFiles()
    {
        // Arrange: Create simulated srvinfo directory structure
        var srvInfoDir = Path.Combine(_tempDir, "srvinfo_3040");
        Directory.CreateDirectory(srvInfoDir);

        // Critical cluster files that must NEVER be deleted
        string rootClusterLst = Path.Combine(srvInfoDir, "1CV8Clst.lst");
        File.WriteAllText(rootClusterLst, "cluster_root_data");

        string workingServersLst = Path.Combine(srvInfoDir, "1cv8ws.lst");
        File.WriteAllText(workingServersLst, "working_servers_data");

        var regDir = Path.Combine(srvInfoDir, "reg_1541");
        Directory.CreateDirectory(regDir);
        string regClusterLst = Path.Combine(regDir, "1CV8Clst.lst");
        File.WriteAllText(regClusterLst, "cluster_reg_data");

        // Infobase directory with legitimate files
        var ibDir = Path.Combine(regDir, "a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        Directory.CreateDirectory(ibDir);
        string ibLst = Path.Combine(ibDir, "1CV8Clst.lst");
        File.WriteAllText(ibLst, "ib_config_data");

        // Session cache directories that SHOULD be cleaned
        var cache1 = Path.Combine(srvInfoDir, "snccntx12345");
        Directory.CreateDirectory(cache1);
        File.WriteAllText(Path.Combine(cache1, "temp_cache.tmp"), "cache_data");

        var cache2 = Path.Combine(ibDir, "snccntx98765");
        Directory.CreateDirectory(cache2);
        File.WriteAllText(Path.Combine(cache2, "temp_session.tmp"), "session_data");

        // Act: Execute safe clean
        int cleanedCount = OneSServiceManager.CleanSnccntxDirectories(srvInfoDir);

        // Assert:
        cleanedCount.Should().Be(2);

        // Cache folders must be gone
        Directory.Exists(cache1).Should().BeFalse();
        Directory.Exists(cache2).Should().BeFalse();

        // All critical cluster and infobase config files must remain intact!
        File.Exists(rootClusterLst).Should().BeTrue();
        File.ReadAllText(rootClusterLst).Should().Be("cluster_root_data");

        File.Exists(workingServersLst).Should().BeTrue();
        File.ReadAllText(workingServersLst).Should().Be("working_servers_data");

        File.Exists(regClusterLst).Should().BeTrue();
        File.ReadAllText(regClusterLst).Should().Be("cluster_reg_data");

        File.Exists(ibLst).Should().BeTrue();
        File.ReadAllText(ibLst).Should().Be("ib_config_data");
    }

    [Fact]
    public async Task AuditLogService_RecordsAndRetrievesEntries()
    {
        // Arrange
        string logPath = Path.Combine(_tempDir, "audit.jsonl");
        var config = Options.Create(new AuditLogConfig
        {
            RetentionDays = 14,
            MaxLogSizeBytes = 1048576,
            LogFilePath = logPath
        });

        var service = new AuditLogService(config, NullLogger<AuditLogService>.Instance);

        var entry1 = new AuditLogEntry
        {
            ClientIp = "192.168.1.50",
            Host = "app-prod01.example.corp",
            ClusterPort = 3040,
            ServiceName = "1C:Enterprise 8.3 Server Agent (x86-64) (port 3040)",
            DisplayName = "Агент сервера 1С:Предприятия 8.3 (x86-64) (порт 3040)",
            Action = "RESTART",
            Status = "SUCCESS",
            DurationMs = 2450
        };

        var entry2 = new AuditLogEntry
        {
            ClientIp = "192.168.1.51",
            Host = "app-prod02.example.corp",
            ClusterPort = 1540,
            ServiceName = "1C_1540",
            DisplayName = "Агент сервера 1С (порт 1540)",
            Action = "RESTART_CLEAN_CACHE",
            Status = "SUCCESS",
            DurationMs = 3820
        };

        // Act
        await service.LogActionAsync(entry1);
        await service.LogActionAsync(entry2);

        var retrieved = await service.GetEntriesAsync(10);

        // Assert
        retrieved.Should().HaveCount(2);
        retrieved[0].Action.Should().Be("RESTART_CLEAN_CACHE"); // Descending order by timestamp
        retrieved[1].Action.Should().Be("RESTART");

        // Verify file contains 2 json lines
        File.Exists(logPath).Should().BeTrue();
        var lines = await File.ReadAllLinesAsync(logPath);
        lines.Should().HaveCount(2);
    }
}
