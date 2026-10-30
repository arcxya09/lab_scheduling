using LabSchedule.Core;

namespace LabSchedule.Core.Tests;

/// <summary>Per-item attachment folders: naming, JSON round trip and the file operations.</summary>
public sealed class AttachmentTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lab-schedule-tests", Guid.NewGuid().ToString("N"));

    public AttachmentTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static Schedule Sample() => ScheduleRules.NormalizeState(ScheduleRules.PrepareSchedule(SampleSchedule.CreateRaw()));

    private string WriteSource(string name, string text = "示例内容")
    {
        var folder = Path.Combine(_dir, "source");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void FolderIsWrittenToJsonOnlyWhenSet()
    {
        var schedule = Sample();
        var before = ScheduleJson.ToFileText(schedule);
        Assert.DoesNotContain("attachment_folder", before);

        schedule.Items[0].AttachmentFolder = "2026-06-01 示例实验";
        var text = ScheduleJson.ToFileText(schedule);
        var node = ScheduleJson.Parse(text)!["items"]![0]!.AsObject();
        Assert.Equal(("attachment_folder", "2026-06-01 示例实验"), (node.Last().Key, node.Last().Value!.GetValue<string>()));
        Assert.Single(text.Split('\n'), line => line.Contains("attachment_folder"));

        var loaded = ScheduleRules.PrepareSchedule(ScheduleJson.Parse(text));
        Assert.Equal("2026-06-01 示例实验", loaded.Items[0].AttachmentFolder);
        Assert.Equal("2026-06-01 示例实验", ScheduleRules.NormalizeState(loaded).Items[0].AttachmentFolder);
        Assert.Equal(text, ScheduleJson.ToFileText(loaded));
    }

    [Fact]
    public void SuggestedFolderUsesStartDateAndNameAndAvoidsOtherItemsFolders()
    {
        var schedule = Sample();
        var item = schedule.Items[0];
        item.Name = "样品:测试/A?";
        item.StartDate = "2026-03-02";
        Assert.Equal("2026-03-02 样品_测试_A_", Attachments.SuggestFolderName(schedule, item));

        schedule.Items[1].AttachmentFolder = "2026-03-02 样品_测试_A_";
        schedule.Items[2].AttachmentFolder = "2026-03-02 样品_测试_A_ (2)";
        Assert.Equal("2026-03-02 样品_测试_A_ (3)", Attachments.SuggestFolderName(schedule, item));

        // The item's own folder name doesn't count as taken.
        item.AttachmentFolder = "2026-03-02 样品_测试_A_ (2)";
        schedule.Items[2].AttachmentFolder = "";
        Assert.Equal("2026-03-02 样品_测试_A_ (2)", Attachments.SuggestFolderName(schedule, item));
    }

    [Fact]
    public void SuggestedFolderFallsBackWhenNameOrDateIsMissing()
    {
        var schedule = Sample();
        var item = schedule.Items[0];
        item.Name = "  ...  ";
        item.StartDate = "";
        Assert.Equal(item.Id, Attachments.SuggestFolderName(schedule, item));
        item.Name = new string('长', 120);
        item.StartDate = "2026-01-05";
        var name = Attachments.SuggestFolderName(schedule, item);
        Assert.Equal(80, name.Length);
        Assert.StartsWith("2026-01-05 长", name);
        item.Name = "CON";
        Assert.Equal("2026-01-05 CON", Attachments.SuggestFolderName(schedule, item));
        item.StartDate = "";
        Assert.Equal("_CON", Attachments.SuggestFolderName(schedule, item));
    }

    [Fact]
    public void OnlyPlainFolderNamesAreUsed()
    {
        var item = new ScheduleItem();
        foreach (var unsafeName in new[] { "", " ", "..", ".", "a\\b", "a/b", "C:", "..\\..\\Windows", "NUL", "x." })
        {
            item.AttachmentFolder = unsafeName;
            Assert.Null(Attachments.FolderPath(_dir, item));
        }
        item.AttachmentFolder = "2026-06-01 示例实验";
        Assert.Equal(Path.Combine(_dir, "2026-06-01 示例实验"), Attachments.FolderPath(_dir, item));
    }

    [Fact]
    public void CopyNumbersDuplicateNamesAndSkipsFilesAlreadyThere()
    {
        var target = Path.Combine(_dir, "附件", "2026-06-01 示例实验");
        var application = WriteSource("实验申请书.pdf");
        var notice = WriteSource("安全告知书.docx");

        var first = Attachments.Copy(target, [application, notice]);
        Assert.Equal(["实验申请书.pdf", "安全告知书.docx"], first.Added);
        Assert.Equal((0, 0), (first.Renamed, first.Skipped));

        var second = Attachments.Copy(target, [application, Path.Combine(target, "安全告知书.docx"), Path.Combine(_dir, "missing.txt")]);
        Assert.Equal(["实验申请书 (2).pdf"], second.Added);
        Assert.Equal((1, 2), (second.Renamed, second.Skipped));
        Assert.Equal(3, Attachments.Count(target));
    }

    [Fact]
    public void CopyTakesWholeFoldersButNotTheTargetItself()
    {
        var records = Path.Combine(_dir, "实验记录");
        Directory.CreateDirectory(Path.Combine(records, "第一天"));
        File.WriteAllText(Path.Combine(records, "第一天", "记录.txt"), "1");
        File.WriteAllText(Path.Combine(records, "汇总.txt"), "2");
        var target = Path.Combine(_dir, "附件", "示例");

        var result = Attachments.Copy(target, [records, target, _dir]);
        Assert.Equal(["实验记录"], result.Added);
        Assert.Equal(2, result.Skipped);
        Assert.True(File.Exists(Path.Combine(target, "实验记录", "第一天", "记录.txt")));
        Assert.True(File.Exists(Path.Combine(target, "实验记录", "汇总.txt")));

        var entries = Attachments.List(target);
        Assert.Single(entries);
        Assert.True(entries[0].IsFolder);
    }

    [Fact]
    public void ListShowsFoldersFirstAndHidesHiddenFiles()
    {
        var target = Path.Combine(_dir, "附件", "示例");
        Directory.CreateDirectory(Path.Combine(target, "照片"));
        File.WriteAllText(Path.Combine(target, "b.txt"), "12345");
        File.WriteAllText(Path.Combine(target, "a.txt"), "1");
        var hidden = Path.Combine(target, "~$a.docx");
        File.WriteAllText(hidden, "");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var entries = Attachments.List(target);
        Assert.Equal(["照片", "a.txt", "b.txt"], entries.Select(entry => entry.Name));
        Assert.Equal(5, entries[2].Size);
        Assert.Empty(Attachments.List(Path.Combine(_dir, "missing")));
        Assert.Empty(Attachments.List(null));
        Assert.Equal(0, Attachments.Count(null));
    }

    [Fact]
    public void RenameKeepsTheExtensionAndRejectsBadOrTakenNames()
    {
        var target = Path.Combine(_dir, "附件", "示例");
        Attachments.Copy(target, [WriteSource("scan001.pdf"), WriteSource("安全告知书.pdf")]);
        var entry = Attachments.List(target).Single(item => item.Name == "scan001.pdf");

        Assert.Equal(("实验申请书.pdf", (string?)null), Attachments.RenameTarget(entry, " 实验申请书 "));
        Assert.Equal("名称不能为空", Attachments.RenameTarget(entry, "  ").Error);
        Assert.NotNull(Attachments.RenameTarget(entry, "a/b").Error);
        Assert.NotNull(Attachments.RenameTarget(entry, "COM1").Error);
        Assert.Equal("已有名为“安全告知书.pdf”的附件", Attachments.RenameTarget(entry, "安全告知书").Error);
        Assert.Null(Attachments.RenameTarget(entry, "SCAN001").Error);

        Attachments.Rename(entry, "实验申请书.pdf");
        Assert.Equal(["安全告知书.pdf", "实验申请书.pdf"], Attachments.List(target).Select(item => item.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void FoldersCanBeRenamedIncludingAChangeOfCase()
    {
        var target = Path.Combine(_dir, "附件", "示例");
        Directory.CreateDirectory(Path.Combine(target, "records"));
        var entry = Attachments.List(target).Single();
        var (name, error) = Attachments.RenameTarget(entry, "Records");
        Assert.Null(error);
        Attachments.Rename(entry, name!);
        Assert.Equal(["Records"], Attachments.List(target).Select(item => item.Name));
    }

    [Fact]
    public void SizesReadLikeExplorer()
    {
        Assert.Equal("0 B", Attachments.FormatSize(0));
        Assert.Equal("1023 B", Attachments.FormatSize(1023));
        Assert.Equal("1 KB", Attachments.FormatSize(1024));
        Assert.Equal("1.5 KB", Attachments.FormatSize(1536));
        Assert.Equal("12 MB", Attachments.FormatSize(12L * 1024 * 1024 + 300));
        Assert.Equal("2.3 GB", Attachments.FormatSize((long)(2.3 * 1024 * 1024 * 1024)));
    }
}
