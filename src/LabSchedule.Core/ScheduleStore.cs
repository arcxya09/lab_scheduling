using System.Text;

namespace LabSchedule.Core;

public sealed record InitialLoad(Schedule Schedule, OutputPaths Paths, bool FromDefaultJson, string Warning);

/// <summary>Outcome of a file operation; <see cref="Error"/> is the first validation issue or exception text.</summary>
public sealed record StoreResult<T>(T? Value, string? Error)
{
    public bool Ok => Error is null;

    public static StoreResult<T> Success(T value) => new(value, null);

    public static StoreResult<T> Failure(string error) => new(default, error);
}

/// <summary>File handling that used to live in the Electron main process.</summary>
public static class ScheduleStore
{
    public static string AppVersion { get; set; } = "3.1.0";

    /// <summary>Overrides the output folder (used by tests so they never touch Documents).</summary>
    public const string OutputDirEnvironmentVariable = "LAB_SCHEDULE_OUTPUT_DIR";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The folder chosen on the settings page; null for the standard Documents folder.</summary>
    public static string? OutputDirSetting { get; set; }

    public static string StandardOutputDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "实验排班输出");

    public static string DefaultOutputDir
    {
        get
        {
            var overrideDir = Environment.GetEnvironmentVariable(OutputDirEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(overrideDir)) return overrideDir;
            return string.IsNullOrWhiteSpace(OutputDirSetting) ? StandardOutputDir : OutputDirSetting;
        }
    }

    public static string DefaultJsonPath => Path.Combine(DefaultOutputDir, "default_schedule.json");

    public static OutputPaths DefaultPaths(Schedule schedule)
    {
        var dir = DefaultOutputDir;
        return new OutputPaths
        {
            PdfPath = Path.Combine(dir, $"schedule_{schedule.Year}.pdf"),
            HtmlPath = Path.Combine(dir, $"schedule_{schedule.Year}.html"),
            JsonPath = DefaultJsonPath
        };
    }

    public static InitialLoad LoadInitial()
    {
        try
        {
            var raw = ScheduleJson.Parse(File.ReadAllText(DefaultJsonPath, Encoding.UTF8));
            var issues = ScheduleRules.Validate(raw);
            if (issues.Count == 0)
            {
                var schedule = ScheduleRules.PrepareSchedule(raw);
                return new InitialLoad(schedule, DefaultPaths(schedule), true, "");
            }
            return Sample($"默认 JSON 校验失败，已加载示例数据：{issues[0]}");
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return Sample("");
        }
        catch (Exception error)
        {
            return Sample($"默认 JSON 读取失败，已加载示例数据：{error.Message}");
        }
    }

    private static InitialLoad Sample(string warning)
    {
        var schedule = ScheduleRules.PrepareSchedule(SampleSchedule.CreateRaw());
        return new InitialLoad(schedule, DefaultPaths(schedule), false, warning);
    }

    public static StoreResult<Schedule> Import(string jsonPath)
    {
        try
        {
            var raw = ScheduleJson.Parse(File.ReadAllText(jsonPath, Encoding.UTF8));
            var issues = ScheduleRules.Validate(raw);
            return issues.Count > 0
                ? StoreResult<Schedule>.Failure(issues[0])
                : StoreResult<Schedule>.Success(ScheduleRules.PrepareSchedule(raw));
        }
        catch (Exception error)
        {
            return StoreResult<Schedule>.Failure($"导入失败：{error.Message}");
        }
    }

    /// <summary>Validates, renumbers and writes the schedule; returns the path written.</summary>
    public static StoreResult<string> WriteJson(Schedule schedule, string jsonPath)
    {
        var issues = ScheduleRules.Validate(schedule);
        if (issues.Count > 0) return StoreResult<string>.Failure(issues[0]);
        var prepared = ScheduleRules.PrepareSchedule(schedule);
        EnsureParentDir(jsonPath);
        File.WriteAllText(jsonPath, ScheduleJson.ToFileText(prepared), Utf8NoBom);
        return StoreResult<string>.Success(jsonPath);
    }

    public static StoreResult<string> SaveDefault(Schedule schedule) => WriteJson(schedule, DefaultJsonPath);

    public static void WriteText(string path, string text)
    {
        EnsureParentDir(path);
        File.WriteAllText(path, text, Utf8NoBom);
    }

    public static void EnsureParentDir(string filePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    public static string ReplaceExtension(string filePath, string extension) =>
        Path.Combine(Path.GetDirectoryName(filePath) ?? "", Path.GetFileNameWithoutExtension(filePath) + extension);

    public static string RenderLogPath(string pdfPath) =>
        Path.Combine(Path.GetDirectoryName(pdfPath) ?? "", $"{Path.GetFileNameWithoutExtension(pdfPath)}_render_log.txt");

    public static string BuildRenderLog(string pdfPath, string htmlPath, Schedule schedule, IReadOnlyList<string> warnings, DateTime generatedAtUtc)
    {
        var lines = new List<string>
        {
            "Experiment Schedule Render Log",
            $"App version: {AppVersion}",
            $"Generated at: {generatedAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture)}",
            $"Title: {schedule.Title}",
            $"Year: {schedule.Year}",
            $"PDF: {pdfPath}",
            $"HTML: {htmlPath}",
            $"Page: {schedule.Settings.PageSize} {schedule.Settings.Orientation}",
            $"Items rendered: {schedule.Items.Count}",
            "",
            "Warnings:"
        };
        if (warnings.Count > 0) lines.AddRange(warnings.Select(warning => $"- {warning}"));
        else lines.Add("- none");
        return string.Join("\n", lines) + "\n";
    }
}
