using System.Text.Json;
using System.Text.Json.Serialization;

namespace LabSchedule.Core;

/// <summary>User preferences from the settings page, stored in %LocalAppData%\LabSchedule\settings.json.</summary>
public sealed class AppSettings
{
    public const string ThemeSystem = "system";
    public const string ThemeLight = "light";
    public const string ThemeDark = "dark";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>"system", "light" or "dark".</summary>
    public string Theme { get; set; } = ThemeSystem;

    /// <summary>Initial state of "联动后续安排" when the program starts.</summary>
    public bool CascadeByDefault { get; set; } = true;

    /// <summary>Folder for default_schedule.json and generated files; null means Documents\实验排班输出.</summary>
    public string? OutputDir { get; set; }

    /// <summary>Look for a newer release on GitHub when the program starts.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Version that last ran, so the first start after an update can say so.</summary>
    public string? LastRunVersion { get; set; }

    /// <summary>Overrides where settings are kept (used by tests so they never touch the real settings).</summary>
    public const string FileEnvironmentVariable = "LAB_SCHEDULE_SETTINGS_FILE";

    public static string DefaultFilePath =>
        Environment.GetEnvironmentVariable(FileEnvironmentVariable) is { Length: > 0 } overridePath
            ? overridePath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LabSchedule", "settings.json");

    /// <summary>Reads settings; a missing or unreadable file gives the defaults.</summary>
    public static AppSettings Load(string path)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
            if (settings.Theme is not (ThemeSystem or ThemeLight or ThemeDark)) settings.Theme = ThemeSystem;
            if (string.IsNullOrWhiteSpace(settings.OutputDir)) settings.OutputDir = null;
            return settings;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(string path)
    {
        ScheduleStore.EnsureParentDir(path);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}
