using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OneSGetDatabases.Core.Interfaces;
using OneSGetDatabases.Core.Models;
using OneSGetDatabases.Core.Services;
using Xunit;

namespace OneSGetDatabases.Tests;

public class InfobaseJobsServiceTests
{
    private readonly Mock<IDatabaseCacheService> _cache = new();
    private readonly Mock<IClusterDiscoveryEngine> _engine = new();
    private readonly Mock<IRacService> _rac = new();
    private string _racArgs = "";

    public InfobaseJobsServiceTests()
    {
        _cache.Setup(c => c.GetDev()).Returns(
        [
            new InfoBaseItem
            {
                Name = "buh_dev",
                UUID = "ib-uuid",
                ClusterUUID = "cl-uuid",
                Cluster = "dev-app01:1541",
                SQL = "dev-sql01",
                SQLDbName = "buh_dev",
                Environment = "DEV"
            }
        ]);
        _rac.Setup(r => r.RunRacAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback<string, int?, CancellationToken>((args, _, _) => _racArgs = args)
            .ReturnsAsync(new RacResult("", "", 0));
    }

    private InfobaseJobsService CreateService(SqlRestoreConfig restoreConfig) => new(
        _cache.Object,
        _engine.Object,
        _rac.Object,
        Options.Create(new ClusterDiscoveryConfig { DefaultClusterUser = "cluster_admin", DefaultClusterPassword = "cl_pwd" }),
        Options.Create(new List<ClusterConfig> { new() { Server = "dev-app01:1541", RasPort = 1545, Environment = "DEV" } }),
        Options.Create(restoreConfig),
        NullLogger<InfobaseJobsService>.Instance);

    [Fact]
    public async Task DenyScheduledJobs_WithoutInfobaseAdminInSettings_UsesClusterAdmin()
    {
        var result = await CreateService(new SqlRestoreConfig()).DenyScheduledJobsAsync("dev-sql01", "buh_dev", null, null);

        result.Success.Should().BeTrue();
        _racArgs.Should().Contain("dev-app01:1545 infobase update")
            .And.Contain("--cluster=cl-uuid")
            .And.Contain("--infobase=ib-uuid")
            .And.Contain("--infobase-user=\"cluster_admin\"")
            .And.Contain("--infobase-pwd=\"cl_pwd\"")
            .And.Contain("--scheduled-jobs-deny=on");
    }

    [Fact]
    public async Task DenyScheduledJobs_WithInfobaseAdminInSettings_UsesIt()
    {
        var config = new SqlRestoreConfig
        {
            InfobaseAdminCredentials = [new InfobaseAdminCredential { Username = "ib_admin", Password = "ib\\pwd\\" }]
        };

        await CreateService(config).DenyScheduledJobsAsync("dev-sql01", "buh_dev", null, null);

        _racArgs.Should().Contain("--infobase-user=\"ib_admin\"")
            .And.Contain("--infobase-pwd=\"ib\\pwd\\\\\"")
            .And.Contain("--cluster-user=\"cluster_admin\"");
    }

    [Fact]
    public async Task DenyScheduledJobs_TriesCredentialsInOrderThenClusterAdmin()
    {
        var calls = new List<string>();
        _rac.Setup(r => r.RunRacAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string args, int? _, CancellationToken _) =>
            {
                calls.Add(args);
                // Only the cluster admin is an infobase administrator in this scenario
                return args.Contains("--infobase-user=\"cluster_admin\"")
                    ? new RacResult("", "", 0)
                    : new RacResult("", "Недостаточно прав пользователя на информационную базу", 255);
            });
        var config = new SqlRestoreConfig
        {
            InfobaseAdminCredentials =
            [
                new InfobaseAdminCredential { Username = "infobase_admin1", EncryptedPassword = "pwd1" },
                new InfobaseAdminCredential { Username = "sql_restore", EncryptedPassword = "pwd2" }
            ]
        };

        var result = await CreateService(config).DenyScheduledJobsAsync("dev-sql01", "buh_dev", null, null);

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("cluster_admin");
        calls.Should().HaveCount(3);
        calls[0].Should().Contain("--infobase-user=\"infobase_admin1\"").And.Contain("--infobase-pwd=\"pwd1\"");
        calls[1].Should().Contain("--infobase-user=\"sql_restore\"").And.Contain("--infobase-pwd=\"pwd2\"");
    }

    [Fact]
    public async Task DenyScheduledJobs_WhenClusterAdminRejected_RetriesWithoutClusterCredentials()
    {
        var calls = new List<string>();
        _rac.Setup(r => r.RunRacAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string args, int? _, CancellationToken _) =>
            {
                calls.Add(args);
                // rac.exe reports errors with exit code -1 (255)
                return args.Contains("--cluster-user=")
                    ? new RacResult("", "Ошибка операции администрирования\r\nАдминистратор кластера не аутентифицирован", -1)
                    : new RacResult("", "", 0);
            });
        var config = new SqlRestoreConfig
        {
            InfobaseAdminCredentials = [new InfobaseAdminCredential { Username = "infobase_admin1", EncryptedPassword = "pwd1" }]
        };

        var result = await CreateService(config).DenyScheduledJobsAsync("dev-sql01", "buh_dev", null, null);

        result.Success.Should().BeTrue();
        calls.Should().HaveCount(2);
        calls[1].Should().NotContain("--cluster-user=").And.Contain("--infobase-user=\"infobase_admin1\"");
    }

    [Fact]
    public async Task DenyScheduledJobs_WhenRasUnreachable_StopsAfterFirstAttempt()
    {
        int attempts = 0;
        _rac.Setup(r => r.RunRacAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { attempts++; return new RacResult("", "RAC timeout exceeded", -2); });
        var config = new SqlRestoreConfig
        {
            InfobaseAdminCredentials = [new InfobaseAdminCredential { Username = "a", Password = "p" }]
        };

        var result = await CreateService(config).DenyScheduledJobsAsync("dev-sql01", "buh_dev", null, null);

        result.Success.Should().BeFalse();
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task DenyScheduledJobs_WhenRacFails_MasksPasswordsAndHintsSettings()
    {
        _rac.Setup(r => r.RunRacAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RacResult("", "Недостаточно прав пользователя cl_pwd ib_secret", 1));
        var config = new SqlRestoreConfig
        {
            InfobaseAdminCredentials = [new InfobaseAdminCredential { Username = "ib", EncryptedPassword = "ib_secret" }]
        };

        var result = await CreateService(config).DenyScheduledJobsAsync("dev-sql01", "buh_dev", null, null);

        result.Success.Should().BeFalse();
        result.Message.Should().NotContain("cl_pwd").And.NotContain("ib_secret").And.Contain("SqlRestore:InfobaseAdminCredentials");
    }
}
