using FluentAssertions;
using OneSGetDatabases.Core.Models;
using OneSGetDatabases.Core.Services;
using Xunit;

namespace OneSGetDatabases.Tests;

public class ClusterDiscoveryEngineTests
{
    [Fact]
    public void ParseRagentCommandLine_WithCustomPortAndQuotedDir_ExtractsAllFields()
    {
        // Arrange
        string cmd = @"""C:\Program Files\1cv8\8.3.25.1445\bin\ragent.exe"" -srvc -agent -regport 3041 -port 3040 -range 3060:3091 -d ""D:\1C\srvinfo_3040""";

        // Act
        var result = ClusterDiscoveryEngine.ParseRagentCommandLine("1C_3040", cmd);

        // Assert
        result.Name.Should().Be("1C_3040");
        result.Port.Should().Be(3040);
        result.Version.Should().Be("8.3.25.1445");
        result.ClusterDir.Should().Be(@"D:\1C\srvinfo_3040");
    }

    [Fact]
    public void ParseRagentCommandLine_WithoutExplicitPort_DefaultsTo1540()
    {
        // Arrange
        string cmd = @"""C:\Program Files\1cv8\8.3.24.1342\bin\ragent.exe"" -srvc -agent -regport 1541 -range 1560:1591 -d D:\1C\srvinfo";

        // Act
        var result = ClusterDiscoveryEngine.ParseRagentCommandLine("1C_Default", cmd);

        // Assert
        result.Port.Should().Be(1540);
        result.Version.Should().Be("8.3.24.1342");
        result.ClusterDir.Should().Be(@"D:\1C\srvinfo");
    }

    [Theory]
    [InlineData(@"""C:\Program Files\1cv8\8.3.25.1445\bin\ras.exe"" cluster --port 9004 localhost:3040", 9004, 3040)]
    [InlineData(@"""C:\Program Files\1cv8\8.3.25.1445\bin\ras.exe"" cluster --service --port 9007 127.0.0.1:4540", 9007, 4540)]
    [InlineData(@"""C:\Program Files\1cv8\8.3.25.1445\bin\ras.exe"" cluster --port 9010 app-prod01:6040", 9010, 6040)]
    [InlineData(@"""C:\Program Files\1cv8\8.3.25.1445\bin\ras.exe"" cluster localhost:1540", 1545, 1540)]
    [InlineData(@"""C:\Program Files\1cv8\8.3.25.1445\bin\ras.exe"" cluster --service 2540", 1545, 2540)]
    public void ParseRasCommandLine_ParsesVariousCommandLineFormats(string cmd, int expectedRasPort, int expectedTargetPort)
    {
        // Act
        var result = ClusterDiscoveryEngine.ParseRasCommandLine("RAS_Svc", cmd);

        // Assert
        result.RasPort.Should().Be(expectedRasPort);
        result.TargetPort.Should().Be(expectedTargetPort);
    }

    [Fact]
    public void MatchClusters_PairsAgentsAndRasServicesCorrectly()
    {
        // Arrange
        var node = new ServerNodeConfig
        {
            Host = "app-prod01",
            Environment = "PROD"
        };

        var agents = new[]
        {
            new DiscoveredRagentService("1C_3040", 3040, "cmd", "8.3.25.1445", "D:\\srv3040"),
            new DiscoveredRagentService("1C_4540", 4540, "cmd", "8.3.25.1445", "D:\\srv4540"),
            new DiscoveredRagentService("1C_6040", 6040, "cmd", "8.3.25.1445", "D:\\srv6040"),
            new DiscoveredRagentService("1C_7040", 7040, "cmd", "8.3.25.1445", "D:\\srv7040"),
        };

        var rasServices = new[]
        {
            new DiscoveredRasService("RAS_3040", 9004, 3040, "cmd"),
            new DiscoveredRasService("RAS_4540", 9007, 4540, "cmd"),
            new DiscoveredRasService("RAS_6040", 9010, 6040, "cmd"),
            // 7040 has no matching RAS service
        };

        // Act
        var clusters = ClusterDiscoveryEngine.MatchClusters(node, "default_user", "default_pwd", agents, rasServices);

        // Assert
        clusters.Should().HaveCount(4);

        var c3040 = clusters.Single(c => c.ServerPort == 3040);
        c3040.Server.Should().Be("app-prod01:3040");
        c3040.RasPort.Should().Be(9004);
        c3040.RasAddress.Should().Be("app-prod01:9004");
        c3040.ClusterUser.Should().Be("default_user");

        var c4540 = clusters.Single(c => c.ServerPort == 4540);
        c4540.RasPort.Should().Be(9007);

        var c6040 = clusters.Single(c => c.ServerPort == 6040);
        c6040.RasPort.Should().Be(9010);

        var c7040 = clusters.Single(c => c.ServerPort == 7040);
        c7040.RasPort.Should().Be(0);
        c7040.RasAddress.Should().Be("—");
    }

    [Fact]
    public void MatchClusters_HonorsNodeSpecificCredentials()
    {
        // Arrange
        var node = new ServerNodeConfig
        {
            Host = "custom-node",
            Environment = "DEV",
            ClusterUser = "custom_user",
            ClusterPassword = "custom_pwd"
        };

        var agents = new[]
        {
            new DiscoveredRagentService("1C_1540", 1540, "cmd", "8.3.25.1445", "D:\\srv1540")
        };

        var rasServices = new[]
        {
            new DiscoveredRasService("RAS_9001", 9001, 1540, "cmd")
        };

        // Act
        var clusters = ClusterDiscoveryEngine.MatchClusters(node, "global_user", "global_pwd", agents, rasServices);

        // Assert
        clusters.Should().ContainSingle();
        clusters[0].ClusterUser.Should().Be("custom_user");
        clusters[0].ClusterPassword.Should().Be("custom_pwd");
        clusters[0].Environment.Should().Be("DEV");
    }
}
