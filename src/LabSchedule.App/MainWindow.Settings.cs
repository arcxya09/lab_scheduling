using LabSchedule.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace LabSchedule;

/// <summary>The settings page (theme, cascade default, output folder, updates, about).</summary>
public sealed partial class MainWindow
{
    private bool IsSettingsOpen => SettingsPanel.Visibility == Visibility.Visible;

    private void InitializeSettingsPage()
    {
        _rendering++;
        try
        {
            ThemeSetting.SelectedIndex = _settings.Theme switch
            {
                AppSettings.ThemeLight => 1,
                AppSettings.ThemeDark => 2,
                _ => 0
            };
            CascadeSetting.IsOn = _settings.CascadeByDefault;
            UpdateCheckSetting.IsOn = _settings.CheckForUpdates;
        }
        finally
        {
            _rendering--;
        }
        var version = Updater.CurrentVersion.ToString();
        CurrentVersionText.Text = $"当前版本 {version}";
        AboutText.Text = $"版本 {version} · {(Updater.IsInstalledCopy ? "安装版" : "便携版")} · 原生 WinUI 3";
        RenderOutputDir();
    }

    private void SaveSettings()
    {
        try
        {
            _settings.Save(AppSettings.DefaultFilePath);
        }
        catch (Exception error)
        {
            SetStatus($"设置保存失败：{error.Message}", StatusKind.Error, 9000);
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void SettingsBackButton_Click(object sender, RoutedEventArgs e) => CloseSettings();

    private void OpenSettings()
    {
        CommitPendingEdit();
        RenderOutputDir();
        CommandArea.Visibility = Visibility.Collapsed;
        UpdateBar.Visibility = Visibility.Collapsed;
        Workbench.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Visible;
        SettingsBackButton.Focus(FocusState.Programmatic);
    }

    private void CloseSettings()
    {
        SettingsPanel.Visibility = Visibility.Collapsed;
        CommandArea.Visibility = Visibility.Visible;
        UpdateBar.Visibility = Visibility.Visible;
        Workbench.Visibility = Visibility.Visible;
        SettingsButton.Focus(FocusState.Programmatic);
    }

    // ---- appearance ------------------------------------------------------------------------------

    private void ApplyTheme(string theme)
    {
        RootGrid.RequestedTheme = theme switch
        {
            AppSettings.ThemeLight => ElementTheme.Light,
            AppSettings.ThemeDark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    private void ThemeSetting_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_rendering > 0 || ThemeSetting.SelectedItem is not ComboBoxItem { Tag: string theme } || theme == _settings.Theme) return;
        _settings.Theme = theme;
        SaveSettings();
        ApplyTheme(theme);
    }

    // ---- editing -----------------------------------------------------------------------------------

    private void CascadeSetting_Toggled(object sender, RoutedEventArgs e)
    {
        if (_rendering > 0 || CascadeSetting.IsOn == _settings.CascadeByDefault) return;
        _settings.CascadeByDefault = CascadeSetting.IsOn;
        SaveSettings();
    }

    // ---- output folder -----------------------------------------------------------------------------

    private void RenderOutputDir()
    {
        var custom = !string.IsNullOrWhiteSpace(_settings.OutputDir);
        var outputs = ScheduleStore.DefaultPaths(_schedule);
        OutputDirText.Text = ScheduleStore.DefaultOutputDir + (custom ? "" : "（默认）");
        OutputFilesText.Text =
            $"保存 JSON：{Path.GetFileName(outputs.JsonPath)}（启动时自动加载）\n" +
            $"生成 PDF：{Path.GetFileName(outputs.PdfPath)}、{Path.GetFileName(outputs.HtmlPath)} 和生成日志（按排班年份命名，已有同名文件会被覆盖）";
        ResetOutputDirButton.IsEnabled = custom;
    }

    private async void ChangeOutputDirButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, _hwnd);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        ChangeOutputDir(folder.Path);
    }

    private void ResetOutputDirButton_Click(object sender, RoutedEventArgs e) => ChangeOutputDir(null);

    private void OpenOutputDirButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(ScheduleStore.DefaultOutputDir);
            NativeMethods.ShowInFolder(ScheduleStore.DefaultOutputDir);
        }
        catch (Exception error)
        {
            SetStatus(error.Message, StatusKind.Error);
        }
    }

    /// <summary>Switches the output folder used for saving, generating and "打开输出目录".</summary>
    private void ChangeOutputDir(string? folder)
    {
        _settings.OutputDir = string.IsNullOrWhiteSpace(folder) ? null : folder;
        ScheduleStore.OutputDirSetting = _settings.OutputDir;
        SaveSettings();

        var after = ScheduleStore.DefaultPaths(_schedule);
        RenderOutputDir();
        SetStatus(File.Exists(after.JsonPath)
            ? $"默认输出文件夹已改为：{ScheduleStore.DefaultOutputDir}"
            : $"默认输出文件夹已改为：{ScheduleStore.DefaultOutputDir}。请点“保存 JSON”把当前排班保存到新位置", StatusKind.Ok, 9000);
    }

    // ---- updates -----------------------------------------------------------------------------------

    private void UpdateCheckSetting_Toggled(object sender, RoutedEventArgs e)
    {
        if (_rendering > 0 || UpdateCheckSetting.IsOn == _settings.CheckForUpdates) return;
        _settings.CheckForUpdates = UpdateCheckSetting.IsOn;
        SaveSettings();
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        UpdateStatusText.Text = "正在检查…";
        try
        {
            var release = await CheckForUpdateAsync();
            UpdateStatusText.Text = release is null
                ? $"已是最新版本（{DateTime.Now:HH:mm} 检查）"
                : $"发现新版本 {release.Version}，可在主界面顶部的提示条中更新";
        }
        catch (Exception error)
        {
            UpdateStatusText.Text = $"检查失败：{Updater.Describe(error)}";
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }
}
