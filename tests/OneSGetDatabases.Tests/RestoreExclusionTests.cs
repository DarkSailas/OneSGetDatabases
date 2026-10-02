using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OneSGetDatabases.Core.Interfaces;
using OneSGetDatabases.Core.Models;
using OneSGetDatabases.Core.Services;
using Xunit;

namespace OneSGetDatabases.Tests;

public class RestoreExclusionTests
{
    private static SqlRestoreService CreateService(params string[] excluded) => new(
        Options.Create(new SqlRestoreConfig { ExcludedDatabases = [.. excluded] }),
        Options.Create(new DbmsConnectionConfig()),
        new Mock<IAuditLogService>().Object,
        new Mock<IInfobaseJobsService>().Object,
        NullLogger<SqlRestoreService>.Instance);

    [Theory]
    [InlineData("ITIL", true)]
    [InlineData("itil", true)]
    [InlineData("Itilium", false)]
    [InlineData("itilium_dev", false)]
    [InlineData("CIS", true)]
    [InlineData("CIS_Archive", true)]
    [InlineData("GilevRu_test", true)]
    [InlineData("qa", true)]
    [InlineData("qa_portal", false)]
    [InlineData("BUH_Corp", false)]
    public void IsDatabaseExcluded_ExactNamesAndMasks(string db, bool expected)
    {
        var service = CreateService("qa", "ITIL", "GilevRu*", "CIS*");

        service.IsDatabaseExcluded(db).Should().Be(expected);
    }
}
