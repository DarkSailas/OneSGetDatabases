using FluentAssertions;
using OneSGetDatabases.Core.Models;
using OneSGetDatabases.Core.Services;
using Xunit;

namespace OneSGetDatabases.Tests;

public class RestoreAuditMessageTests
{
    private static RestoreTimelinePoint Point(BackupType type, string date, string time) => new()
    {
        Id = $"{type}{date}{time}", Date = date, Time = time, Type = type,
        SizeBytes = 1, SizeDisplay = "1 КБ", DisplayName = type.ToString()
    };

    [Fact]
    public void LogBackups_CollapseIntoCountAndRange()
    {
        var chain = new List<RestoreTimelinePoint>
        {
            Point(BackupType.Full, "2026-09-26", "23:34:38"),
            Point(BackupType.Differential, "2026-10-01", "22:02:31"),
        };
        for (int h = 0; h < 24; h++)
            chain.Add(Point(BackupType.Log, "2026-10-02", $"{h:00}:07:05"));

        string msg = SqlRestoreService.BuildRestoreAuditMessage("SRV07/HRM", chain, 27, "2 мин 44 с", true, [], []);

        msg.Should().Be("Из SRV07/HRM: Full 2026-09-26 23:34:38 → Differential 2026-10-01 22:02:31 → Log ×24 (2026-10-02 00:07:05 … 2026-10-02 23:07:05). "
                        + "Файлов: 27, время: 2 мин 44 с. Регламентные задания заблокированы.");
    }

    [Fact]
    public void RestoredUsers_AreListed()
    {
        var chain = new List<RestoreTimelinePoint> { Point(BackupType.Full, "2026-09-26", "23:34:38") };
        string[] users = [@"CORP\sql_db_access_own", @"CORP\sql_db_access_rw", "app_user"];

        string msg = SqlRestoreService.BuildRestoreAuditMessage("SRV/DB", chain, 1, "5 с", false, [], users);

        msg.Should().EndWith(@"Права восстановлены: CORP\sql_db_access_own, CORP\sql_db_access_rw, app_user.");
        msg.Should().NotContain("Регламентные");
    }

    [Fact]
    public void RestoredUsers_LongListIsCut()
    {
        var chain = new List<RestoreTimelinePoint> { Point(BackupType.Full, "2026-09-26", "23:34:38") };
        var users = Enumerable.Range(1, 12).Select(i => $"u{i}").ToList();

        string msg = SqlRestoreService.BuildRestoreAuditMessage("SRV/DB", chain, 1, "5 с", false, [], users);

        msg.Should().EndWith("Права восстановлены: u1, u2, u3, u4, u5, u6, u7, u8, u9, u10 и ещё 2.");
    }

    [Fact]
    public void NoRestoredUsers_NoUserSection()
    {
        var chain = new List<RestoreTimelinePoint> { Point(BackupType.Full, "2026-09-26", "23:34:38") };

        SqlRestoreService.BuildRestoreAuditMessage("SRV/DB", chain, 1, "5 с", false, [], [])
            .Should().NotContain("Права").And.NotContain("Пропущены");
    }

    [Fact]
    public void Warnings_AreLimitedAndTruncated()
    {
        var chain = new List<RestoreTimelinePoint> { Point(BackupType.Full, "2026-09-26", "23:34:38") };
        string[] warnings = [new string('x', 400), "w2", "w3", "w4", "w5"];

        string msg = SqlRestoreService.BuildRestoreAuditMessage("SRV/DB", chain, 1, "5 с", false, warnings, []);

        msg.Should().Contain(new string('x', 150) + "… | w2 | w3 и ещё 2");
        msg.Should().NotContain(new string('x', 151));
    }
}
