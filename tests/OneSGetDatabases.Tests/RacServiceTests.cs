using FluentAssertions;
using OneSGetDatabases.Core.Interfaces;
using OneSGetDatabases.Core.Services;
using Xunit;

namespace OneSGetDatabases.Tests;

public class RacServiceTests
{
    [Theory]
    [InlineData("agent", "version", true)]
    [InlineData("cluster", "list", true)]
    [InlineData("cluster", "info --cluster=x", false)]
    [InlineData("infobase", "summary list", false)]
    public void IsAgentLevelCommand_DetectsCommandsWithoutClusterAuth(string mode, string action, bool expected)
    {
        RacService.IsAgentLevelCommand(mode, action).Should().Be(expected);
    }

    [Fact]
    public void MoreInformative_KeepsRealReasonOverParseError()
    {
        var auth = new RacResult("", "Администратор центрального сервера не аутентифицирован", 255);
        var parse = new RacResult("", "Ошибка разбора параметра: --cluster-user", 255);

        RacService.MoreInformative(auth, parse).Should().BeSameAs(auth);
        RacService.MoreInformative(parse, auth).Should().BeSameAs(auth);
        RacService.MoreInformative(null, parse).Should().BeSameAs(parse);
    }
}
