using FluentAssertions;
using OneSGetDatabases.Core.Services;
using Xunit;

namespace OneSGetDatabases.Tests;

public class RacParserTests
{
    [Fact]
    public void ParseClusters_ShouldExtractClusters_WhenValidRacOutput()
    {
        string sample = @"
cluster                  : 8e87ad04-f5ab-4db2-9ae5-257a05ebf367
host                     : app-prod01.example.corp
port                     : 1541
name                     : ""Main Cluster""
expiration-timeout       : 0
lifetime-limit           : 0

cluster                  : 12345678-aaaa-bbbb-cccc-123456789abc
host                     : app-prod01.example.corp
port                     : 2541
name                     : ""Test Cluster""
";

        var clusters = RacParser.ParseClusters(sample);

        clusters.Should().HaveCount(2);
        clusters[0].UUID.Should().Be("8e87ad04-f5ab-4db2-9ae5-257a05ebf367");
        clusters[0].Name.Should().Be("Main Cluster");
        clusters[1].UUID.Should().Be("12345678-aaaa-bbbb-cccc-123456789abc");
        clusters[1].Name.Should().Be("Test Cluster");
    }

    [Fact]
    public void ParsePlatformVersion_ShouldExtractVersion()
    {
        string sample = @"
8.3.25.1445
";
        string version = RacParser.ParsePlatformVersion(sample);
        version.Should().Be("8.3.25.1445");
    }

    [Fact]
    public void ParseInfobases_ShouldExtractBases()
    {
        string sample = @"
infobase                 : aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee
name                     : buh_corp
descr                    : ""Бухгалтерия КОРП""

infobase                 : 11111111-2222-3333-4444-555555555555
name                     : zup_main
descr                    : ""Зарплата и управление персоналом""
";

        var bases = RacParser.ParseInfobases(sample);

        bases.Should().HaveCount(2);
        bases[0].UUID.Should().Be("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        bases[0].Name.Should().Be("buh_corp");
        bases[0].Description.Should().Be("Бухгалтерия КОРП");
        bases[1].Name.Should().Be("zup_main");
    }

    [Fact]
    public void ParseInfobaseDetails_ShouldExtractDbServerAndName()
    {
        string sample = @"
infobase                 : aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee
name                     : buh_corp
descr                    : ""Бухгалтерия КОРП""
dbms                     : MSSQLServer
db-server                : sql-prod01.example.corp
db-name                  : buh_corp_db
db-user                  : sa
";

        var (dbServer, dbName, dbms) = RacParser.ParseInfobaseDetails(sample);

        dbServer.Should().Be("sql-prod01.example.corp");
        dbName.Should().Be("buh_corp_db");
        dbms.Should().Be("MSSQLServer");
    }
}
