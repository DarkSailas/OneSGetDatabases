using FluentAssertions;
using OneSGetDatabases.Core.Models;
using OneSGetDatabases.Core.Services;
using Xunit;

namespace OneSGetDatabases.Tests;

public class RestoreChainPlannerTests
{
    private static RestoreTimelinePoint Point(BackupType type, DateTime date, string file) => new()
    {
        Id = $"{type}|{date:O}|{file}",
        Date = date.ToString("yyyy-MM-dd"),
        Time = date.ToString("HH:mm:ss"),
        Type = type,
        SizeBytes = 100,
        SizeDisplay = "100 Б",
        DisplayName = file,
        BackupDate = date,
        FilePaths = [$@"\\backup\srv\db\{file}"]
    };

    private static readonly DateTime D0 = new(2026, 9, 1, 0, 0, 0);

    [Theory]
    [InlineData("db_20260901.bak", null, BackupType.Full)]
    [InlineData("db_20260901.diff", null, BackupType.Differential)]
    [InlineData("db_20260901.trn", null, BackupType.Log)]
    [InlineData("SRV_db_DIFF_20260901_120000.bak", "DIFF", BackupType.Differential)]
    [InlineData("SRV_db_LOG_20260901_120000.trn", "LOG", BackupType.Log)]
    [InlineData("SRV_db_FULL_20260901_120000.bak", "FULL", BackupType.Full)]
    [InlineData("anything.bak", "DIFF", BackupType.Differential)]
    public void ClassifyBackupFile_RecognisesFlatAndOlaLayouts(string file, string? dir, BackupType expected)
    {
        RestoreChainPlanner.ClassifyBackupFile(file, dir).Should().Be(expected);
    }

    [Fact]
    public void BuildCandidates_ForLog_PicksLatestFullAndDiffBeforeTarget()
    {
        var oldFull = Point(BackupType.Full, D0, "f1.bak");
        var full = Point(BackupType.Full, D0.AddDays(1), "f2.bak");
        var diff = Point(BackupType.Differential, D0.AddDays(1).AddHours(12), "d1.diff");
        var log1 = Point(BackupType.Log, D0.AddDays(1).AddHours(13), "l1.trn");
        var target = Point(BackupType.Log, D0.AddDays(1).AddHours(14), "l2.trn");
        var later = Point(BackupType.Log, D0.AddDays(1).AddHours(15), "l3.trn");

        var c = RestoreChainPlanner.BuildCandidates([later, target, log1, diff, full, oldFull], target);

        c.Full.Should().BeSameAs(full);
        c.Differential.Should().BeSameAs(diff);
        c.Logs.Should().Contain([log1, target]).And.NotContain(later);
    }

    [Fact]
    public void BuildCandidates_ForDiff_ExposesEarlierFullsForCopyOnlyCase()
    {
        var full1 = Point(BackupType.Full, D0, "f1.bak");
        var copyOnly = Point(BackupType.Full, D0.AddDays(1), "f2.bak");
        var diff = Point(BackupType.Differential, D0.AddDays(1).AddHours(2), "d.diff");

        var c = RestoreChainPlanner.BuildCandidates([full1, copyOnly, diff], diff);

        c.Full.Should().BeSameAs(copyOnly);
        c.EarlierFulls.Should().Equal(full1);
    }

    [Fact]
    public void SelectLogSequence_WhenTargetCoveredByBase_Throws()
    {
        var target = Point(BackupType.Log, D0.AddHours(1), "l.trn");
        var act = () => RestoreChainPlanner.SelectLogSequence(500m,
            [(target, new BackupHeader(1, 2, 100, 400, 0, null))], target);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void BuildCandidates_ForDiffWithoutFull_Throws()
    {
        var diff = Point(BackupType.Differential, D0, "d.diff");
        var act = () => RestoreChainPlanner.BuildCandidates([diff], diff);
        act.Should().Throw<InvalidOperationException>().WithMessage("*нет полного бэкапа*");
    }

    [Fact]
    public void BuildCandidates_ForFull_ReturnsOnlyFull()
    {
        var full = Point(BackupType.Full, D0, "f.bak");
        var c = RestoreChainPlanner.BuildCandidates([full], full);
        c.Full.Should().BeSameAs(full);
        c.Differential.Should().BeNull();
        c.Logs.Should().BeEmpty();
    }

    [Fact]
    public void SelectLogSequence_SkipsCoveredLogsAndStopsAtTarget()
    {
        var early = Point(BackupType.Log, D0, "l0.trn");
        var l1 = Point(BackupType.Log, D0.AddHours(1), "l1.trn");
        var target = Point(BackupType.Log, D0.AddHours(2), "l2.trn");
        var after = Point(BackupType.Log, D0.AddHours(3), "l3.trn");

        var seq = RestoreChainPlanner.SelectLogSequence(150m,
        [
            (after, new BackupHeader(1, 2, 300, 400, 0, null)),
            (early, new BackupHeader(1, 2, 50, 100, 0, null)),
            (target, new BackupHeader(1, 2, 200, 300, 0, null)),
            (l1, new BackupHeader(1, 2, 100, 200, 0, null))
        ], target);

        seq.Select(s => s.Point).Should().Equal(l1, target);
    }

    [Fact]
    public void SelectLogSequence_WithGap_Throws()
    {
        var l1 = Point(BackupType.Log, D0.AddHours(1), "l1.trn");
        var target = Point(BackupType.Log, D0.AddHours(2), "l2.trn");

        var act = () => RestoreChainPlanner.SelectLogSequence(150m,
        [
            (l1, new BackupHeader(1, 2, 100, 200, 0, null)),
            (target, new BackupHeader(1, 2, 250, 300, 0, null))
        ], target);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Разрыв цепочки*");
    }

    [Fact]
    public void PlanFileMoves_ReusesExistingFilesByNameThenByType_AndCreatesMissing()
    {
        var moves = RestoreChainPlanner.PlanFileMoves(
            [
                new BackupFileEntry("prod_data", "D", @"E:\prod\prod.mdf"),
                new BackupFileEntry("prod_log", "L", @"F:\prod\prod.ldf"),
                new BackupFileEntry("prod_data2", "D", @"E:\prod\prod2.ndf")
            ],
            [
                new ExistingDbFile("dev_data", @"D:\data\dev.mdf", "ROWS"),
                new ExistingDbFile("dev_log", @"L:\log\dev.ldf", "LOG")
            ],
            @"D:\default", @"L:\default", "dev");

        moves.Should().Equal(
            ("prod_data", @"D:\data\dev.mdf"),
            ("prod_log", @"L:\log\dev.ldf"),
            ("prod_data2", @"D:\default\dev_prod_data2.ndf"));
    }

    [Fact]
    public void PlanFileMoves_WithoutAnyDirectory_Throws()
    {
        var act = () => RestoreChainPlanner.PlanFileMoves(
            [new BackupFileEntry("data", "D", @"E:\x.mdf")], [], null, null, "db");
        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("dev-s-sql01", true)]
    [InlineData("DEV-S-SQL01.corp.local", true)]
    [InlineData(@"dev-s-sql01\INST", true)]
    [InlineData("dev-s-sql01,1433", true)]
    [InlineData("dev-s-sql01.attacker.tld", false)]
    [InlineData("dev", false)]
    [InlineData("dev-s-sql011", false)]
    [InlineData("other-sql", false)]
    public void IsAllowedTargetServer_MatchesHostExactlyOrByTrustedFqdn(string target, bool expected)
    {
        RestoreChainPlanner.IsAllowedTargetServer(target, ["dev-s-sql01"], ["corp.local"]).Should().Be(expected);
    }

    [Fact]
    public void IsAllowedTargetServer_ShortNameMatchesFqdnInWhitelist()
    {
        RestoreChainPlanner.IsAllowedTargetServer("dev-s-sql01", ["dev-s-sql01.corp.local"]).Should().BeTrue();
    }

    [Theory]
    [InlineData("db", true)]
    [InlineData("..", false)]
    [InlineData(@"a\b", false)]
    [InlineData("", false)]
    public void IsSafePathSegment_RejectsTraversal(string segment, bool expected)
    {
        RestoreChainPlanner.IsSafePathSegment(segment).Should().Be(expected);
    }

    [Fact]
    public void QuoteIdentifier_EscapesClosingBracket()
    {
        RestoreChainPlanner.QuoteIdentifier("a]b").Should().Be("[a]]b]");
    }
}
