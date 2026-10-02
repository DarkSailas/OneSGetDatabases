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
        string unc = OneSServiceManager.ToUncPath("app-srv06", @"D:\1C\srvinfo_3040");

        // Assert
        unc.Should().Be(@"\\app-srv06\D$\1C\srvinfo_3040");
    }

    [Fact]
    public void ToUncPath_PreservesExistingUncPath()
    {
        // Act
        string unc = OneSServiceManager.ToUncPath("app-srv06", @"\\remote\share\srvinfo");

        // Assert
        unc.Should().Be(@"\\remote\share\srvinfo");
    }

    [Fact]
    public void ToUncPath_ConvertsQuotedLocalDrivePathToAdminShare()
    {
        // Act
        string unc = OneSServiceManager.ToUncPath("app-srv06", @"""D:\1C\srvinfo_3040""");

        // Assert
        unc.Should().Be(@"\\app-srv06\D$\1C\srvinfo_3040");
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
        var cleaned = OneSServiceManager.CleanSnccntxDirectories(srvInfoDir);

        // Assert:
        cleaned.Deleted.Should().Be(2);
        cleaned.Failed.Should().Be(0);

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
            Host = "app-srv06",
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
            Host = "app-srv07",
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

    [Fact]
    public void CleanSnccntxDirectories_NestedCacheFolderIsNotCountedAsFailure()
    {
        var outer = Path.Combine(_tempDir, "srvinfo", "snccntxA");
        Directory.CreateDirectory(Path.Combine(outer, "snccntxB"));

        var cleaned = OneSServiceManager.CleanSnccntxDirectories(Path.Combine(_tempDir, "srvinfo"));

        cleaned.Deleted.Should().Be(1);
        cleaned.Failed.Should().Be(0);
        Directory.Exists(outer).Should().BeFalse();
    }

    [Theory]
    // rphost/rmngr of the cluster on 1540 (regport 1541)
    [InlineData(@"""C:\Program Files\1cv8\8.3.27.1936\bin\rphost.exe"" -range 1560:1591 -reghost app06 -regport 1541 -pid x", 1540, "", true)]
    [InlineData(@"""C:\Program Files\1cv8\8.3.27.1936\bin\rmngr.exe"" -port 1541 -host app06 -range 1560:1591", 1540, "", true)]
    [InlineData(@"rphost.exe -reghost app06:1541 -pid x", 1540, "", true)]
    // Other clusters on the same host must survive
    [InlineData(@"rphost.exe -range 2560:2591 -reghost app06 -regport 2541", 1540, "", false)]
    [InlineData(@"rphost.exe -range 1560:1591 -reghost app06 -regport 15410", 1540, "", false)]
    [InlineData(@"rmngr.exe -port 15411 -host app06", 1541, "", false)]
    [InlineData(@"rphost.exe -range 1560:1541 -reghost app06 -regport 3541", 1540, "", false)]
    // Directory match is bounded: a sibling folder with a longer name is a different cluster
    [InlineData(@"rmngr.exe -d ""D:\1C_Server_Accounting"" -port 9999", 3040, @"D:\1C_Server_Accounting", true)]
    [InlineData(@"rmngr.exe -d ""D:\1C_Server_Accounting_IT"" -port 9999", 3040, @"D:\1C_Server_Accounting", false)]
    [InlineData(@"rmngr.exe -d D:\1C_Server_Accounting\ -port 9999", 3040, @"D:\1C_Server_Accounting\", true)]
    public void IsClusterWorkerCommandLine_MatchesOnlyThisCluster(string cmdLine, int clusterPort, string clusterDir, bool expected)
    {
        OneSServiceManager.IsClusterWorkerCommandLine(cmdLine, clusterPort, clusterDir).Should().Be(expected);
    }

    [Theory]
    [InlineData(@"D:\1C_Server_2040", true)]
    [InlineData(@"E:\1C Server\srvinfo", true)]
    [InlineData(@"D:\x"" & del /q C:\*", false)]
    [InlineData(@"D:\x & calc", false)]
    [InlineData(@"D:\x|y", false)]
    [InlineData(@"D:\%TEMP%", false)]
    [InlineData(@"relative\path", false)]
    [InlineData(@"C:\", false)]
    [InlineData(@"C:\.", false)]
    [InlineData(@"C:\...", false)]
    [InlineData(@"C:\a\.. ", false)]
    [InlineData(@"C:\a\..", false)]
    [InlineData("", false)]
    public void IsSafeClusterDir_RejectsShellMetacharactersAndRoots(string dir, bool expected)
    {
        OneSServiceManager.IsSafeClusterDir(dir).Should().Be(expected);
    }

    [Fact]
    public void CleanSnccntxDirectories_MissingDirectoryReturnsEmptyResult()
    {
        var root = Path.Combine(_tempDir, "srvinfo_missing");

        var cleaned = OneSServiceManager.CleanSnccntxDirectories(root);

        cleaned.Should().Be(default(OneSServiceManager.SnccntxCleanResult));
    }

    private static OneSServiceInfo Agent(string host, int port, string dir) => new()
    {
        Host = host, Environment = "PROD", ClusterPort = port, Status = "Running",
        DisplayName = $"agent {port}", ServiceName = $"1C_{port}", ClusterDir = dir
    };

    [Fact]
    public void ScopeClusterDir_SharedSrvinfoNarrowsToOwnRegistryFolder()
    {
        var a = Agent("app06", 1540, @"D:\srvinfo");
        var b = Agent("app06", 1640, @"D:\srvinfo\");
        var other = Agent("app07", 1740, @"D:\srvinfo");

        OneSServiceManager.ScopeClusterDir(a, [a, b, other]).ClusterDir.Should().Be(@"D:\srvinfo\reg_1541");
        OneSServiceManager.ScopeClusterDir(a, [a, other]).ClusterDir.Should().Be(@"D:\srvinfo");
    }

    [Fact]
    public async Task ExecuteServiceAction_RejectsServiceNotFoundByDiscovery()
    {
        var audit = new AuditLogService(
            Options.Create(new AuditLogConfig { LogFilePath = Path.Combine(_tempDir, "audit.jsonl") }),
            NullLogger<AuditLogService>.Instance);
        var manager = new OneSServiceManager(
            Options.Create(new ClusterDiscoveryConfig()), audit, NullLogger<OneSServiceManager>.Instance);

        var result = await manager.ExecuteServiceActionAsync(new ServiceActionRequest
        {
            Host = "app06",
            ServiceName = "1C_1540",
            Action = "restart-clean-cache"
        }, "10.0.0.1");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("не найдена");
        var entries = await audit.GetEntriesAsync(10);
        entries.Should().ContainSingle(e => e.Status == "FAILED" && e.Action == "RESTART_CLEAN_CACHE");
    }

    [Theory]
    [InlineData("::ffff:10.150.251.78", "10.150.251.78")]
    [InlineData("::ffff:192.168.1.50", "192.168.1.50")]
    [InlineData("::1", "127.0.0.1")]
    [InlineData("", "127.0.0.1")]
    [InlineData(null, "127.0.0.1")]
    [InlineData("   ", "127.0.0.1")]
    [InlineData("10.150.251.78", "10.150.251.78")]
    [InlineData("192.168.0.1", "192.168.0.1")]
    public void NormalizeIp_CorrectlyStripsIpv6PrefixAndHandlesEdgeCases(string? input, string expected)
    {
        string result = AuditLogService.NormalizeIp(input);
        result.Should().Be(expected);
    }
}
