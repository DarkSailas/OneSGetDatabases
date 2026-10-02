using FluentAssertions;
using OneSGetDatabases.Core.Models;
using OneSGetDatabases.Core.Services;
using Xunit;

namespace OneSGetDatabases.Tests;

public class DatabaseCacheServiceTests
{
    [Fact]
    public void Cache_ShouldCorrectlySeparateDevAndProd()
    {
        var cache = new DatabaseCacheService();
        var items = new List<InfoBaseItem>
        {
            new() { Name = "dev_base_1", Environment = "DEV", Cluster = "dev:1540" },
            new() { Name = "dev_base_2", Environment = "DEV", Cluster = "dev:1540" },
            new() { Name = "prod_base_1", Environment = "PROD", Cluster = "prod:1540" }
        };

        cache.Update(items);

        cache.GetAll().Should().HaveCount(3);
        cache.GetDev().Should().HaveCount(2);
        cache.GetProd().Should().HaveCount(1);

        var found = cache.Find("DEV", "dev:1540", "dev_base_1");
        found.Should().NotBeNull();
        found!.Name.Should().Be("dev_base_1");
    }
}
