using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OneSGetDatabases.Core.Models;
using OneSGetDatabases.Core.Services;
using Xunit;

namespace OneSGetDatabases.Tests;

public class ActiveDirectoryServiceTests
{
    private static ActiveDirectoryService CreateService()
        => new(Options.Create(new ActiveDirectoryConfig()), NullLogger<ActiveDirectoryService>.Instance);

    [Theory]
    [InlineData("rdp_1c_Buh_Corp", true)]
    [InlineData("1cbases83_Buh_Corp", true)]
    [InlineData("1c_admins", false)]
    [InlineData("spb_1c_users", false)]
    [InlineData("Domain Admins", false)]
    [InlineData("rdp_users", false)]
    [InlineData("", false)]
    public void IsManagedGroup_AllowsOnly1CAccessGroups(string group, bool expected)
    {
        CreateService().IsManagedGroup(group).Should().Be(expected);
    }

    [Fact]
    public async Task AddMemberAsync_ForNonManagedGroup_IsForbiddenWithoutTouchingAd()
    {
        var result = await CreateService().AddMemberAsync("Domain Admins", "someone");

        result.Success.Should().BeFalse();
        result.Forbidden.Should().BeTrue();
    }

    [Theory]
    [InlineData("user*")]
    [InlineData("a(b)")]
    [InlineData("")]
    public async Task RemoveMemberAsync_WithInvalidLogin_FailsValidation(string sam)
    {
        var result = await CreateService().RemoveMemberAsync("rdp_1c_Test", sam);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Некорректный логин");
    }
}
