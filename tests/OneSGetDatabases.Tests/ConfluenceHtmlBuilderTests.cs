using FluentAssertions;
using OneSGetDatabases.Core.Models;
using OneSGetDatabases.Core.Services;
using Xunit;

namespace OneSGetDatabases.Tests;

public class ConfluenceHtmlBuilderTests
{
    [Fact]
    public void BuildMainTableHtml_ShouldGenerateValidHtmlTableWithEscaping()
    {
        var list = new List<InfoBaseItem>
        {
            new()
            {
                Name = "test_base",
                Description = "Тестовая база <1C>",
                AccessGroup = "rdp_1c_test_base",
                Cluster = "app-prod01.example.corp:1540",
                ServerIP = "10.10.10.1",
                Platform = "8.3.25.1445",
                Consul = "app-test.service.consul",
                SQL = "sql-prod01.example.corp",
                SQLDbName = "test_base_db",
                ServiceUser = "domain\\srv1c",
                ServiceName = "1C:Enterprise 8.3 Server Agent",
                ClusterPath = "D:\\1c_cluster"
            }
        };

        string html = ConfluencePublisher.BuildMainTableHtml(list);

        html.Should().StartWith("<table><tr><th>№</th><th>База</th>");
        html.Should().Contain("<td>test_base</td>");
        html.Should().Contain("<td>Тестовая база &lt;1C&gt;</td>");
        html.Should().Contain("<td>rdp_1c_test_base</td>");
        html.Should().Contain("<td>sql-prod01.example.corp</td>");
        html.Should().EndWith("</table>");
    }

    [Fact]
    public void BuildSaInfoTableHtml_ShouldGenerateValidSaInfoTable()
    {
        var list = new List<InfoBaseItem>
        {
            new()
            {
                Name = "prod_base",
                Description = "Основная база",
                V8iFile = "prod_base.v8i",
                RaGroup = "rdp_1c_prod_base",
                OneCGroup = "1cbases83_prod_base",
                Platform = "8.3.25.1445"
            }
        };

        string html = ConfluencePublisher.BuildSaInfoTableHtml(list);

        html.Should().StartWith("<table><tr><th>№</th><th>База</th><th>Наименование базы</th>");
        html.Should().Contain("<td>prod_base</td>");
        html.Should().Contain("<td>prod_base.v8i</td>");
        html.Should().Contain("<td>rdp_1c_prod_base</td>");
        html.Should().Contain("<td>1cbases83_prod_base</td>");
        html.Should().EndWith("</table>");
    }
}
