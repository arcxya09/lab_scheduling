using LabSchedule.Core;

namespace LabSchedule.Core.Tests;

public class UpdateAndSettingsTests
{
    private const string ReleaseJson = """
        {
          "tag_name": "v3.2.0",
          "name": "实验排班生成器 3.2.0",
          "body": "说明",
          "html_url": "https://github.com/arcxya09/lab_scheduling/releases/tag/v3.2.0",
          "draft": false,
          "prerelease": false,
          "assets": [
            { "name": "LabSchedule-3.2.0-win-x64-portable.zip", "size": 67744539, "digest": "sha256:ABCDEF", "browser_download_url": "https://example.test/portable.zip" },
            { "name": "LabSchedule-3.2.0-win-x64-setup.msi", "size": 52975889, "browser_download_url": "https://example.test/setup.msi" }
          ]
        }
        """;

    [Fact]
    public void ParsesTheLatestRelease()
    {
        var release = UpdateFeed.Parse(ReleaseJson)!;
        Assert.Equal("v3.2.0", release.Tag);
        Assert.Equal(new Version(3, 2, 0), release.Version);
        Assert.Equal(2, release.Assets.Count);
        Assert.Equal("abcdef", release.Assets[0].Sha256);
        Assert.Null(release.Assets[1].Sha256);
        Assert.True(UpdateFeed.IsNewer(release, new Version(3, 1, 0, 0)));
        Assert.False(UpdateFeed.IsNewer(release, new Version(3, 2, 0)));
        Assert.False(UpdateFeed.IsNewer(release, new Version(3, 10, 0)));
    }

    [Fact]
    public void PicksTheInstallerOrThePortableZip()
    {
        var release = UpdateFeed.Parse(ReleaseJson)!;
        Assert.Equal("https://example.test/setup.msi", UpdateFeed.PickAsset(release, installed: true)!.DownloadUrl);
        Assert.Equal("https://example.test/portable.zip", UpdateFeed.PickAsset(release, installed: false)!.DownloadUrl);
        Assert.Null(UpdateFeed.PickAsset(release, installed: true, architecture: "arm64"));
    }

    [Fact]
    public void IgnoresDraftsPrereleasesAndOddTags()
    {
        Assert.Null(UpdateFeed.Parse(ReleaseJson.Replace("\"draft\": false", "\"draft\": true")));
        Assert.Null(UpdateFeed.Parse(ReleaseJson.Replace("\"prerelease\": false", "\"prerelease\": true")));
        Assert.Null(UpdateFeed.Parse(ReleaseJson.Replace("v3.2.0\",\n  \"name\"", "nightly\",\n  \"name\"")));
        Assert.Equal(new Version(3, 2, 0), UpdateFeed.ParseVersion("v3.2"));
        Assert.Equal(new Version(3, 2, 1), UpdateFeed.ParseVersion("V3.2.1-beta"));
        Assert.Null(UpdateFeed.ParseVersion("latest"));
    }

    [Fact]
    public void SettingsRoundTripAndFallBackToDefaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lab-schedule-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "settings.json");
            var defaults = AppSettings.Load(path);
            Assert.Equal((AppSettings.ThemeSystem, true, null, true), (defaults.Theme, defaults.CascadeByDefault, defaults.OutputDir, defaults.CheckForUpdates));

            new AppSettings { Theme = AppSettings.ThemeDark, CascadeByDefault = false, OutputDir = @"D:\排班", CheckForUpdates = false }.Save(path);
            var loaded = AppSettings.Load(path);
            Assert.Equal((AppSettings.ThemeDark, false, @"D:\排班", false), (loaded.Theme, loaded.CascadeByDefault, loaded.OutputDir, loaded.CheckForUpdates));

            File.WriteAllText(path, "{ \"theme\": \"purple\", \"outputDir\": \"  \" }");
            var cleaned = AppSettings.Load(path);
            Assert.Equal((AppSettings.ThemeSystem, null as string), (cleaned.Theme, cleaned.OutputDir));

            File.WriteAllText(path, "not json");
            Assert.Equal(AppSettings.ThemeSystem, AppSettings.Load(path).Theme);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void OutputFolderSettingChangesDefaultPaths()
    {
        var previous = Environment.GetEnvironmentVariable(ScheduleStore.OutputDirEnvironmentVariable);
        Environment.SetEnvironmentVariable(ScheduleStore.OutputDirEnvironmentVariable, null);
        try
        {
            ScheduleStore.OutputDirSetting = @"D:\排班输出";
            Assert.Equal(@"D:\排班输出\default_schedule.json", ScheduleStore.DefaultJsonPath);
            ScheduleStore.OutputDirSetting = null;
            Assert.Equal(ScheduleStore.StandardOutputDir, ScheduleStore.DefaultOutputDir);
        }
        finally
        {
            ScheduleStore.OutputDirSetting = null;
            Environment.SetEnvironmentVariable(ScheduleStore.OutputDirEnvironmentVariable, previous);
        }
    }
}
