using FluentAssertions;
using OneSGetDatabases.Core.Models;
using OneSGetDatabases.Core.Services;
using Xunit;

namespace OneSGetDatabases.Tests;

public class IisPublicationServiceTests
{
    private const string AppHostXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <configuration>
          <system.applicationHost>
            <sites>
              <site name="Default Web Site" id="1">
                <application path="/" applicationPool="DefaultAppPool">
                  <virtualDirectory path="/" physicalPath="%SystemDrive%\inetpub\wwwroot" />
                  <virtualDirectory path="/Legacy" physicalPath="D:\pub\Legacy" />
                </application>
                <application path="/HRMCorp_Test" applicationPool="1C">
                  <virtualDirectory path="/" physicalPath="C:\inetpub\wwwroot\HRMCorp_Test" />
                </application>
                <bindings>
                  <binding protocol="http" bindingInformation="*:8080:" />
                </bindings>
              </site>
            </sites>
          </system.applicationHost>
        </configuration>
        """;

    [Fact]
    public void ParseApplicationHost_ReturnsApplicationsAndVirtualDirectories()
    {
        var apps = IisPublicationService.ParseApplicationHost(AppHostXml);

        apps.Select(a => a.AppPath).Should().Equal("/", "/Legacy", "/HRMCorp_Test");
        apps[2].PhysicalPath.Should().Be(@"C:\inetpub\wwwroot\HRMCorp_Test");
        apps[2].Bindings.Should().ContainSingle();
        apps[0].AppPool.Should().Be("DefaultAppPool");
        apps[2].AppPool.Should().Be("1C");
    }

    [Fact]
    public void ParseVrd_ExtractsServersAndRef()
    {
        const string vrd = """
            <?xml version="1.0" encoding="UTF-8"?>
            <point xmlns="http://v8.1c.ru/8.2/virtual-resource-system" base="/HRMCorp_Test"
                   ib="Srvr=&quot;app-dev01:5541,app-dev02:5541&quot;;Ref=&quot;HRMCorp_Test&quot;;" enable="false" />
            """;

        var info = IisPublicationService.ParseVrd(vrd);

        info.Should().NotBeNull();
        info!.Ref.Should().Be("HRMCorp_Test");
        info.Servers.Should().Equal("app-dev01:5541", "app-dev02:5541");
    }

    [Fact]
    public void ParseVrd_FileInfobase_IsIgnored()
    {
        const string vrd = """<point base="/demo" ib="File=&quot;C:\bases\demo&quot;;" />""";
        IisPublicationService.ParseVrd(vrd).Should().BeNull();
    }

    [Theory]
    [InlineData(@"%SystemDrive%\inetpub\wwwroot\X", @"\\iis01\C$\inetpub\wwwroot\X")]
    [InlineData(@"D:\pub\X", @"\\iis01\D$\pub\X")]
    [InlineData(@"\\files\pub\X", @"\\files\pub\X")]
    public void ToAdminSharePath_ConvertsLocalPaths(string local, string expected)
    {
        IisPublicationService.ToAdminSharePath("iis01", IisPublicationService.ExpandIisVariables(local)).Should().Be(expected);
    }

    [Theory]
    [InlineData("/HRMCorp25", "HRMCorp25", true)]
    [InlineData("/hrmcorp25", "HRMCorp25", true)]
    [InlineData("/HRMCorp25/", "HRMCorp25", true)]
    [InlineData("/site/HRMCorp25", "HRMCorp25", true)]
    [InlineData("/HRMCorp25_", "HRMCorp25", false)]
    [InlineData("/DocMng_Web", "DocMng", false)]
    [InlineData("/DocMngCorp", "DocMng", false)]
    [InlineData("/", "DocMng", false)]
    public void IsNamedAfterBase_OnlyPublicationNamedLikeTheBase(string appPath, string baseName, bool expected)
    {
        IisPublicationService.IsNamedAfterBase(appPath, baseName).Should().Be(expected);
    }

    [Fact]
    public void BuildUrl_UsesConfiguredBaseUrlOrSiteBinding()
    {
        var app = IisPublicationService.ParseApplicationHost(AppHostXml)[2];

        IisPublicationService.BuildUrl("http://app-dev.service.consul/", app, "iis01.corp.local")
            .Should().Be("http://app-dev.service.consul/HRMCorp_Test");
        IisPublicationService.BuildUrl(null, app, "iis01.corp.local")
            .Should().Be("http://iis01.corp.local:8080/HRMCorp_Test");
    }

    private static InfoBaseItem Base(string name, string cluster) => new() { Name = name, Cluster = cluster };

    private static IisPublication Pub(string refName, string url, params string[] servers) => new()
    {
        Host = "iis01", SiteName = "Default Web Site", AppPath = "/" + refName, Url = url,
        IbRef = refName, IbServers = [.. servers]
    };

    [Fact]
    public void PickMainPublication_PrefersConsulAddress()
    {
        var direct = Pub("MultiFront", "http://app05.corp.local/MultiFront");
        var consul = Pub("MultiFront", "http://app-multifront.service.consul/MultiFront");

        IisPublicationService.PickMainPublication([direct, consul]).Should().BeSameAs(consul);
        IisPublicationService.PickMainPublication([direct]).Should().BeSameAs(direct);
        IisPublicationService.PickMainPublication([]).Should().BeNull();
    }

    [Theory]
    [InlineData("app-srv17", "app-srv17:1540", "", true)]
    [InlineData("APP-SRV17.example.corp", "app-srv17:1540", "", true)]
    [InlineData("app-srv03", "app-srv17:1540", "", false)]
    [InlineData("app-srv03", "app-srv17:1540", "app-srv03", true)]
    [InlineData("app-srv03", "app-srv17:1540", "app-srv05", false)]
    public void IsOnBaseServer_OnlyClusterOrConsulServer(string iisHost, string cluster, string consulHost, bool expected)
    {
        var item = new InfoBaseItem { Name = "Revision", Cluster = cluster, ConsulHost = consulHost };
        var pub = Pub("Revision", "http://" + iisHost + "/Revision") with { Host = iisHost };

        IisPublicationService.IsOnBaseServer(pub, item).Should().Be(expected);
    }

    [Fact]
    public void PickMainPublication_PrefersExactNameCase()
    {
        var lower = Pub("multifront", "http://app05.corp.local/multifront");
        var exact = Pub("MultiFront", "http://app06.corp.local/MultiFront");

        IisPublicationService.PickMainPublication([lower, exact], "MultiFront").Should().BeSameAs(exact);
    }

    [Fact]
    public void MatchPublications_ByNameAndHost_NarrowedByPort()
    {
        var onDev5540 = Base("HRMCorp_Test", "app-dev01:5540");
        var onDev1540 = Base("HRMCorp_Test", "app-dev01:1540");
        var other = Base("Other", "app-dev01:5540");

        var map = IisPublicationService.MatchPublications(
            [onDev5540, onDev1540, other],
            [Pub("HRMCorp_Test", "http://app-dev.service.consul/HRMCorp_Test", "app-dev01.corp.local:5541")]);

        map.Should().ContainKey(onDev5540);
        map.Should().NotContainKey(onDev1540);
        map.Should().NotContainKey(other);
    }

    [Fact]
    public void MatchPublications_AliasHost_MatchesUniqueNameOnly()
    {
        var unique = Base("Unique_Base", "app-prod01:1540");
        var dupA = Base("Dup", "app-prod01:1540");
        var dupB = Base("Dup", "app-prod02:1540");

        var map = IisPublicationService.MatchPublications(
            [unique, dupA, dupB],
            [
                Pub("Unique_Base", "http://x/Unique_Base", "app.service.consul"),
                Pub("Dup", "http://x/Dup", "app.service.consul")
            ]);

        map[unique].Select(p => p.Url).Should().Equal("http://x/Unique_Base");
        map.Should().NotContainKey(dupA);
        map.Should().NotContainKey(dupB);
    }

    [Theory]
    [InlineData("http://iis01/UPP_Talant", "app-upp.service.consul", "iis01.corp.local", null, "http://app-upp.service.consul/UPP_Talant")]
    [InlineData("http://iis01:8080/upp", "app-upp.service.consul", "iis01", null, "http://app-upp.service.consul:8080/upp")]
    [InlineData("http://app-dev.service.consul/upp", "app-upp.service.consul", "iis01", "http://app-dev.service.consul", "http://app-upp.service.consul/upp")]
    [InlineData("http://iis01/upp", "app-upp.service.consul", "iis02", null, "http://iis01/upp")]
    [InlineData("http://iis01/upp", "Отсутствует", "", null, "http://iis01/upp")]
    [InlineData("http://portal.corp/upp", "app-upp.service.consul", "iis01", null, "http://portal.corp/upp")]
    [InlineData("http://app-dev.service.consul/UTD_Test", "app-UTD_Test.service.consul", "iis01", "http://app-dev.service.consul", "http://app-dev.service.consul/UTD_Test")]
    [InlineData("http://iis01/UTD_Test", "app-UTD_Test.service.consul", "iis01", null, "http://iis01/UTD_Test")]
    public void WithConsulUrl_UsesBaseConsulNameOnlyForSameServer(string url, string consul, string consulHost, string? baseUrl, string expected)
    {
        var item = new InfoBaseItem { Name = "upp", Consul = consul, ConsulHost = consulHost };
        var pub = Pub("upp", url);

        IisPublicationService.WithConsulUrl(pub, item, baseUrl).Url.Should().Be(expected);
    }
}
