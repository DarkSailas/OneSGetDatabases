using System.Net;
using FluentAssertions;
using OneSGetDatabases.Web.Controllers;
using Xunit;

namespace OneSGetDatabases.Tests;

public class ClientAddressTests
{
    [Theory]
    [InlineData("10.1.2.3", "8.8.8.8", "10.1.2.3")]            // remote caller cannot spoof the header
    [InlineData("127.0.0.1", "10.5.6.7, 10.0.0.1", "10.5.6.7")] // local proxy: first hop
    [InlineData("::1", "10.5.6.7", "10.5.6.7")]
    [InlineData("127.0.0.1", "not-an-ip", "127.0.0.1")]
    [InlineData("::ffff:10.1.2.3", "", "10.1.2.3")]
    [InlineData("::1", "", "127.0.0.1")]
    public void Resolve_TrustsForwardedForOnlyFromLoopback(string remote, string forwarded, string expected)
    {
        ClientAddress.Resolve(IPAddress.Parse(remote), forwarded).Should().Be(expected);
    }

    [Fact]
    public void Resolve_NoRemoteAddressFallsBackToLoopback()
    {
        ClientAddress.Resolve(null, "10.5.6.7").Should().Be("127.0.0.1");
    }
}
