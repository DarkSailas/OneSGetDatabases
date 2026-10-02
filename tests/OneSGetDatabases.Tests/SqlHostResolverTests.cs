using OneSGetDatabases.Core.Helpers;
using Xunit;

namespace OneSGetDatabases.Tests;

public class SqlHostResolverTests
{
    private static Task<string?> Consul(string name, CancellationToken _) => Task.FromResult(name.ToLowerInvariant() switch
    {
        "sql-1c-erp" => "sql-srv01.example.corp",
        "sql-1c-byip" => "10.0.0.5",
        _ => (string?)null
    });

    private static Task<string?> Dns(string host, CancellationToken _) => Task.FromResult(host.ToLowerInvariant() switch
    {
        "sql-1c-retail.example.org" => "sql-srv02.example.corp",
        "sql-srv12" => "sql-srv12.example.corp",
        "10.0.0.5" => "sql-srv05.example.corp",
        _ => (string?)null
    });

    [Theory]
    [InlineData("sql-1c-ERP.service.consul", "sql-srv01")]
    [InlineData("sql-1c-byip.service.consul", "sql-srv05")]
    [InlineData("SQL-1C-Retail.example.org", "sql-srv02")]
    [InlineData("sql-srv12", "sql-srv12")]
    [InlineData("SQL-1C-Retail.example.org\\INST1", "sql-srv02\\inst1")]
    [InlineData("tcp:sql-srv12,1433", "sql-srv12,1433")]
    [InlineData("sql-1c-missing.service.consul", "")]
    [InlineData("unknown-host", "")]
    [InlineData("10.0.0.7", "")]
    [InlineData("Неизвестно", "")]
    [InlineData("", "")]
    public async Task ResolveAsync_ReturnsMachineName(string server, string expected)
    {
        Assert.Equal(expected, await SqlHostResolver.ResolveAsync(server, Consul, Dns));
    }

    [Theory]
    [InlineData("host\\INST", "host", "\\INST")]
    [InlineData("host,1433", "host", ",1433")]
    [InlineData("pg01:5432", "pg01", ":5432")]
    [InlineData("tcp:host", "host", "")]
    [InlineData("host", "host", "")]
    public void SplitServer_SeparatesInstanceAndPort(string server, string host, string suffix)
    {
        Assert.Equal((host, suffix), SqlHostResolver.SplitServer(server));
    }
}
