using LabSchedule.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LabSchedule;

/// <summary>Update check on start-up, the "发现新版本" bar, and the download/install hand-over.</summary>
public sealed partial class MainWindow
{
    private ReleaseInfo? _availableUpdate;
    private CancellationTokenSource? _downloadCancellation;

    private void AnnounceUpdateAndCheck()
    {
        var current = Updater.CurrentVersion;
        if (UpdateFeed.ParseVersion(_settings.LastRunVersion) is { } last && last < current)
        {
            SetStatus($"已更新到 {current}", StatusKind.Ok, 8000);
        }
        if (_settings.LastRunVersion != current.ToString())
        {
            _settings.LastRunVersion = current.ToString();
            SaveSettings();
        }
        Updater.CleanupDownloadsInBackground();

        if (_settings.CheckForUpdates && Updater.AutomaticChecksAllowed)
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await CheckForUpdateAsync();
                }
                catch (Exception)
                {
                    // A failed background check stays silent; "检查更新" on the settings page reports errors.
                }
            });
        }
    }

    /// <summary>Asks GitHub for the latest release; shows the update bar and returns it when newer.</summary>
    private async Task<ReleaseInfo?> CheckForUpdateAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var release = await Updater.FetchLatestAsync(timeout.Token);
        if (release is null || !UpdateFeed.IsNewer(release, Updater.CurrentVersion)) return null;
        ShowUpdateAvailable(release);
        return release;
    }

    private void ShowUpdateAvailable(ReleaseInfo release)
    {
        _availableUpdate = release;
        UpdateBar.Severity = InfoBarSeverity.Informational;
        UpdateBar.Title = $"发现新版本 {release.Version}";
        UpdateBar.Message = $"当前版本 {Updater.CurrentVersion}。更新会下载{(Updater.IsInstalledCopy ? "安装包" : "便携版")}并自动重启程序。";
        UpdateActionButton.Content = "立即更新";
        UpdateActionButton.IsEnabled = true;
        UpdateProgress.Visibility = Visibility.Collapsed;
        UpdateNotesLink.Visibility = string.IsNullOrEmpty(release.PageUrl) ? Visibility.Collapsed : Visibility.Visible;
        if (!string.IsNullOrEmpty(release.PageUrl)) UpdateNotesLink.NavigateUri = new Uri(release.PageUrl);
        UpdateBar.IsOpen = true;
    }

    private void UpdateBar_CloseButtonClick(InfoBar sender, object args) => _downloadCancellation?.Cancel();

    private async void UpdateActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_downloadCancellation is not null)
        {
            _downloadCancellation.Cancel();
            return;
        }
        var release = _availableUpdate;
        if (release is null) return;

        var installed = Updater.IsInstalledCopy;
        var asset = UpdateFeed.PickAsset(release, installed);
        if (asset is null)
        {
            ShowUpdateError($"新版本没有提供{(installed ? "安装版" : "便携版")}文件，请到 GitHub 页面手动下载。");
            return;
        }
        if (!installed && !Updater.CanWriteAppFolder())
        {
            ShowUpdateError("程序所在的文件夹不能写入，无法自动更新。请到 GitHub 页面手动下载新版本。");
            return;
        }
        if (!await ConfirmUnsavedBeforeUpdateAsync()) return;

        _downloadCancellation = new CancellationTokenSource();
        UpdateActionButton.Content = "取消";
        UpdateProgress.Value = 0;
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateBar.Message = "正在下载…";
        var progress = new Progress<double>(percent =>
        {
            UpdateProgress.Value = percent;
            UpdateBar.Message = $"正在下载… {percent:0}%";
        });
        try
        {
            var package = await Updater.DownloadAsync(release, asset, progress, _downloadCancellation.Token);
            UpdateBar.Message = "下载完成，正在关闭程序并安装更新…";
            UpdateActionButton.IsEnabled = false;
            Updater.StartInstall(package, installed);
            Application.Current.Exit();
        }
        catch (OperationCanceledException)
        {
            ShowUpdateAvailable(release);
            UpdateBar.Message = "已取消下载。";
        }
        catch (Exception error)
        {
            ShowUpdateError($"更新失败：{Updater.Describe(error)}");
        }
        finally
        {
            _downloadCancellation?.Dispose();
            _downloadCancellation = null;
        }
    }

    private void ShowUpdateError(string message)
    {
        if (_availableUpdate is { } release) ShowUpdateAvailable(release);
        UpdateBar.Severity = InfoBarSeverity.Error;
        UpdateBar.Message = message;
        UpdateActionButton.Content = "重试";
        UpdateBar.IsOpen = true;
    }

    /// <summary>Offers to save first when the schedule has unsaved changes; false means "cancel".</summary>
    private async Task<bool> ConfirmUnsavedBeforeUpdateAsync()
    {
        CommitPendingEdit();
        if (_hasSavedDefault && _savedFingerprint == Fingerprint()) return true;
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            RequestedTheme = RootGrid.ActualTheme,
            Title = "有未保存的更改",
            Content = "更新会重启程序。要先把当前排班保存到默认 JSON 吗？",
            PrimaryButtonText = "保存并更新",
            SecondaryButtonText = "不保存，直接更新",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        var choice = await dialog.ShowAsync();
        if (choice == ContentDialogResult.None) return false;
        if (choice == ContentDialogResult.Primary)
        {
            SaveButton_Click(this, new RoutedEventArgs());
            return _hasSavedDefault && _savedFingerprint == Fingerprint();
        }
        return true;
    }
}
