using LabSchedule.Core;

namespace LabSchedule.Core.Tests;

/// <summary>The assertions from the Electron build's scripts/test-core.js, ported.</summary>
public class CoreTests
{
    private static Schedule Sample() => ScheduleRules.NormalizeJson(SampleSchedule.CreateRaw());

    private static ScheduleItem Item(string id, string type, string name, string start, string end) =>
        new() { Id = id, Type = type, Name = name, StartDate = start, EndDate = end };

    [Fact]
    public void SampleIsValidAndRendersExpectedContent()
    {
        var schedule = Sample();
        Assert.Empty(ScheduleRules.Validate(schedule));

        var html = OutputHtml.Build(schedule);
        Assert.Contains("实验列表", html);
        Assert.Contains("12 项实验", html);
        Assert.DoesNotContain("项安排", html);
        Assert.Contains("番茄种植观察 · 张三 · 2天", html);
        Assert.DoesNotContain("番茄种植观察 / 张三", html);
        Assert.Contains("date-line", html);
        Assert.Contains("date-fill", html);
        Assert.Contains("<th>时长</th>", html);
        Assert.Contains("2 天", html);
        Assert.DoesNotContain("维护安排固定为灰色", html);
        Assert.DoesNotContain("repeating-linear-gradient", html);

        var result = OutputHtml.BuildResult(schedule);
        Assert.Empty(result.Warnings);
        Assert.Contains("2026年度示例实验安排", result.Html);
    }

    [Fact]
    public void MaintenanceCanBeHiddenFromTheList()
    {
        var schedule = Sample();
        schedule.Settings.ShowMaintenanceInList = false;
        Assert.DoesNotContain("<td>MAINT001</td>", OutputHtml.Build(schedule));
    }

    [Fact]
    public void ItemsAreSortedByDate()
    {
        var schedule = new Schedule
        {
            Year = 2026,
            Title = "排序测试",
            Items = [Item("B", ItemTypes.Experiment, "B", "2026-02-01", "2026-02-02"), Item("A", ItemTypes.Experiment, "A", "2026-01-01", "2026-01-02")]
        };
        var normalized = ScheduleRules.Normalize(schedule);
        Assert.Equal(["A", "B"], normalized.Items.Select(item => item.Id));
        Assert.Equal(["A", "B"], ScheduleRules.SortItemsByDate(Enumerable.Reverse(normalized.Items)).Select(item => item.Id));
    }

    [Fact]
    public void ItemsAreNumberedByType()
    {
        var numbered = ScheduleRules.AutoNumberItems(
        [
            Item("old-2", ItemTypes.Experiment, "B", "2026-02-01", "2026-02-02"),
            Item("old-m", ItemTypes.Maintenance, "M", "2026-01-15", "2026-01-16"),
            Item("old-1", ItemTypes.Experiment, "A", "2026-01-01", "2026-01-02")
        ]);
        Assert.Equal(["EXP001", "MAINT001", "EXP002"], numbered.Select(item => item.Id));

        var prepared = ScheduleRules.PrepareSchedule(new Schedule
        {
            Year = 2026,
            Title = "输出编号",
            Items = [Item("manual", ItemTypes.Experiment, "A", "2026-01-01", "2026-01-02")]
        });
        Assert.Equal("EXP001", prepared.Items[0].Id);
    }

    [Fact]
    public void RenumberInPlaceKeepsInstances()
    {
        var late = Item("x", ItemTypes.Experiment, "late", "2026-03-01", "2026-03-02");
        var early = Item("y", ItemTypes.Maintenance, "early", "2026-01-01", "2026-01-02");
        var items = new List<ScheduleItem> { late, early };
        ScheduleRules.RenumberInPlace(items);
        Assert.Same(early, items[0]);
        Assert.Equal("MAINT001", early.Id);
        Assert.Equal("EXP001", late.Id);
    }

    [Fact]
    public void InvalidValuesAreReported()
    {
        var invalidType = Sample();
        invalidType.Items[0].Type = "repair";
        Assert.Contains(ScheduleRules.Validate(invalidType), issue => issue.Contains("类型无效"));

        var invalidColor = Sample();
        invalidColor.Items[0].Color = "orange";
        Assert.Contains(ScheduleRules.Validate(invalidColor), issue => issue.Contains("#RRGGBB"));

        var invalidDate = Sample();
        invalidDate.Items[0].StartDate = "2026-02-30";
        Assert.Contains(ScheduleRules.Validate(invalidDate), issue => issue.Contains("开始日期无效"));

        var broken = Sample();
        broken.Items[0].EndDate = "2025-01-01";
        Assert.Contains(ScheduleRules.Validate(broken), issue => issue.Contains("开始日期晚于结束日期"));
    }

    [Fact]
    public void CrossYearItemsAreClippedOrIgnored()
    {
        var schedule = ScheduleRules.Normalize(new Schedule
        {
            Year = 2026,
            Title = "跨年测试",
            Items =
            [
                Item("CROSS", ItemTypes.Experiment, "跨年实验", "2025-12-20", "2026-01-10"),
                Item("OUT", ItemTypes.Experiment, "外部实验", "2025-01-01", "2025-01-02")
            ]
        });
        var (items, warnings) = ScheduleRules.ClipItemsToYear(schedule.Items, 2026);
        Assert.Equal(["跨年实验"], items.Select(item => item.Name));
        Assert.Equal("2026-01-01", items[0].StartDate);
        Assert.Contains(warnings, warning => warning.Contains("clipped"));
        Assert.Contains(warnings, warning => warning.Contains("ignored"));

        var (output, _) = ScheduleRules.PrepareOutputSchedule(schedule);
        Assert.Single(output.Items);
        Assert.Equal("EXP001", output.Items[0].Id);
    }

    [Fact]
    public void DateHelpersFollowJavaScriptRules()
    {
        Assert.Null(ScheduleRules.ParseDate("2026-02-29"));
        Assert.NotNull(ScheduleRules.ParseDate("2028-02-29"));
        Assert.Null(ScheduleRules.ParseDate("0050-01-01"));
        Assert.Null(ScheduleRules.ParseDate("2026-1-01"));
        Assert.Equal("2027-01-01", ScheduleRules.AddDaysIso("2026-12-31", 1));
        Assert.Equal("bad", ScheduleRules.AddDaysIso("bad", 3));
        Assert.Equal(-31, ScheduleRules.DiffDays("2026-02-01", "2026-01-01"));
        Assert.Equal(0, ScheduleRules.DiffDays("", "2026-01-01"));
        Assert.Equal(29, ScheduleRules.DaysInMonth(2028, 2));
        Assert.True(ScheduleRules.IsDark("#4E79A7"));
        Assert.False(ScheduleRules.IsDark("#EDC948"));
    }

    [Fact]
    public void RenderLogMatchesElectronLayout()
    {
        var schedule = ScheduleRules.PrepareSchedule(Sample());
        var log = ScheduleStore.BuildRenderLog("C:\\out\\a.pdf", "C:\\out\\a.html", schedule, [], new DateTime(2026, 10, 27, 12, 0, 0, 123, DateTimeKind.Utc));
        Assert.Equal(
            "Experiment Schedule Render Log\nApp version: 3.0.0\nGenerated at: 2026-10-27T12:00:00.123Z\nTitle: 2026年度示例实验安排\nYear: 2026\n" +
            "PDF: C:\\out\\a.pdf\nHTML: C:\\out\\a.html\nPage: A4 landscape\nItems rendered: 13\n\nWarnings:\n- none\n",
            log);
        Assert.Equal("C:\\out\\a_render_log.txt", ScheduleStore.RenderLogPath("C:\\out\\a.pdf"));
        Assert.Equal("C:\\out\\a.html", ScheduleStore.ReplaceExtension("C:\\out\\a.pdf", ".html"));
    }

    [Fact]
    public void FilesRoundTripThroughTheStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lab-schedule-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "nested", "out.json");
            var written = ScheduleStore.WriteJson(Sample(), path);
            Assert.True(written.Ok);
            var imported = ScheduleStore.Import(path);
            Assert.True(imported.Ok);
            Assert.Equal(13, imported.Value!.Items.Count);
            Assert.Equal("#F28E2B", imported.Value.Items[0].Color);

            File.WriteAllText(path, "{ not json");
            Assert.StartsWith("导入失败", ScheduleStore.Import(path).Error);

            var bad = Sample();
            bad.Title = "  ";
            Assert.Equal("标题不能为空", ScheduleStore.WriteJson(bad, path).Error);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
