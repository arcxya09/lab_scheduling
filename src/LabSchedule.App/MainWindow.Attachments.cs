using System.Collections.ObjectModel;
using System.Diagnostics;
using LabSchedule.Core;
using LabSchedule.Timeline;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.VisualBasic.FileIO;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace LabSchedule;

/// <summary>
/// The "附件" section of the editor: files kept for the selected item in 输出文件夹\附件\&lt;folder&gt;
/// (see <see cref="Attachments"/>). File operations happen at once and are not part of undo; only
/// linking an item to its folder is.
/// </summary>
public sealed partial class MainWindow
{
    private readonly ObservableCollection<AttachmentRow> _attachmentRows = [];
    private readonly Dictionary<string, int> _attachmentCounts = new(StringComparer.OrdinalIgnoreCase);
    private bool _copyingAttachments;

    private void InitializeAttachments()
    {
        AttachmentList.ItemsSource = _attachmentRows;
        // Files may have been added, renamed or removed in Explorer in the meantime.
        Activated += (_, args) =>
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated) RefreshAttachments();
        };
    }

    /// <summary>Re-reads the attachment folders (list counts and the selected item's files).</summary>
    private void RefreshAttachments()
    {
        _attachmentCounts.Clear();
        RenderList();
        RenderAttachments();
    }

    private int AttachmentCount(ScheduleItem item)
    {
        var folder = Attachments.FolderPath(Attachments.DefaultRootDir, item);
        if (folder is null) return 0;
        if (!_attachmentCounts.TryGetValue(folder, out var count))
        {
            count = Attachments.Count(folder);
            _attachmentCounts[folder] = count;
        }
        return count;
    }

    private void RenderAttachments()
    {
        var item = _selected;
        var folder = item is null ? null : Attachments.FolderPath(Attachments.DefaultRootDir, item);
        List<AttachmentEntry> entries;
        try
        {
            entries = Attachments.List(folder);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            entries = [];
            SetStatus($"读取附件失败：{error.Message}", StatusKind.Error, 9000);
        }

        // Rebuilding the rows would drop the selection, so only do it when something changed.
        if (!entries.SequenceEqual(_attachmentRows.Select(row => row.Entry)))
        {
            _attachmentRows.Clear();
            foreach (var entry in entries) _attachmentRows.Add(new AttachmentRow(entry));
        }

        AttachmentsTitle.Text = entries.Count > 0 ? $"附件（{entries.Count}）" : "附件";
        AttachmentList.Visibility = entries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AttachmentEmpty.Visibility = entries.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        AttachmentEmptyText.Text = item is null
            ? "先在列表或日历中选择一个安排"
            : "还没有附件。点“添加”选择文件，或把文件拖到这里";
        if (folder is not null)
        {
            AttachmentFolderText.Text = $"保存在 {Attachments.RootFolderName}\\{item!.AttachmentFolder}";
            ToolTipService.SetToolTip(AttachmentFolderText, folder);
            AttachmentFolderText.Visibility = Visibility.Visible;
        }
        else
        {
            AttachmentFolderText.Visibility = Visibility.Collapsed;
        }
        UpdateAttachmentButtons();
    }

    private void UpdateAttachmentButtons()
    {
        var enabled = _selected is not null && !_copyingAttachments;
        AddAttachmentButton.IsEnabled = enabled;
        OpenAttachmentFolderButton.IsEnabled = enabled;
    }

    /// <summary>
    /// The item's attachment folder, linking a new one first if it has none. A schedule that was saved
    /// before is saved again right away, so the link isn't lost if the program is closed without saving.
    /// </summary>
    private string LinkAttachmentFolder(ScheduleItem item)
    {
        var existing = Attachments.FolderPath(Attachments.DefaultRootDir, item);
        if (existing is not null) return existing;

        CommitPendingEdit();
        var wasSaved = _hasSavedDefault && _savedFingerprint == Fingerprint();
        item.AttachmentFolder = Attachments.SuggestFolderName(_schedule, item);
        RecordHistory("添加附件");
        if (wasSaved)
        {
            try
            {
                if (ScheduleStore.SaveDefault(CurrentPayload()).Ok) _savedFingerprint = Fingerprint();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Stays "未保存"; the link is kept in memory until the user saves.
            }
        }
        UpdateButtons();
        UpdateDirtyState();
        return Attachments.FolderPath(Attachments.DefaultRootDir, item)!;
    }

    private async Task AddAttachmentsAsync(ScheduleItem item, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0 || _copyingAttachments) return;
        _copyingAttachments = true;
        UpdateAttachmentButtons();
        SetStatus($"正在添加 {paths.Count} 个附件…", StatusKind.Normal, 0);
        try
        {
            var newLink = Attachments.FolderPath(Attachments.DefaultRootDir, item) is null;
            var folder = LinkAttachmentFolder(item);
            var result = await Task.Run(() => Attachments.Copy(folder, paths));
            var notes = new List<string>();
            if (result.Renamed > 0) notes.Add($"{result.Renamed} 个与已有附件重名，已自动编号");
            if (result.Skipped > 0) notes.Add($"{result.Skipped} 个已在附件文件夹中或找不到，已跳过");
            var noteText = notes.Count > 0 ? $"（{string.Join("，", notes)}）" : "";
            var unsaved = newLink && (!_hasSavedDefault || _savedFingerprint != Fingerprint()) ? "。请保存 JSON 以保留附件关联" : "";
            SetStatus(result.Added.Count > 0
                ? $"已添加 {result.Added.Count} 个附件{noteText}{unsaved}"
                : $"没有添加附件{noteText}", result.Added.Count > 0 ? StatusKind.Ok : StatusKind.Normal, 6000);
        }
        catch (Exception error)
        {
            SetStatus($"添加附件失败：{error.Message}", StatusKind.Error, 9000);
        }
        finally
        {
            _copyingAttachments = false;
            RefreshAttachments();
        }
    }

    // ---- section buttons ---------------------------------------------------------------------------

    private async void AddAttachmentButton_Click(object sender, RoutedEventArgs e)
    {
        var item = _selected;
        if (item is null) return;
        CommitPendingEdit();
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, _hwnd);
        var files = await picker.PickMultipleFilesAsync();
        if (files is null || files.Count == 0) return;
        await AddAttachmentsAsync(item, files.Select(file => file.Path).Where(path => !string.IsNullOrEmpty(path)).ToList());
    }

    private void OpenAttachmentFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var item = _selected;
        if (item is null) return;
        try
        {
            // Files dropped into this folder in Explorer show up when the window is next activated.
            var folder = LinkAttachmentFolder(item);
            Directory.CreateDirectory(folder);
            NativeMethods.ShowInFolder(folder);
            RefreshAttachments();
        }
        catch (Exception error)
        {
            SetStatus(error.Message, StatusKind.Error);
        }
    }

    // ---- drag and drop -----------------------------------------------------------------------------

    private bool CanDropAttachments(DragEventArgs e) =>
        _selected is not null && !_copyingAttachments && e.DataView.Contains(StandardDataFormats.StorageItems);

    private void Attachments_DragOver(object sender, DragEventArgs e)
    {
        if (!CanDropAttachments(e))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = $"添加为“{_selected!.Name}”的附件";
        AttachmentsSection.Background = ThemeColors.System(RootGrid, "SubtleFillColorSecondaryBrush");
    }

    private void Attachments_DragLeave(object sender, DragEventArgs e) =>
        AttachmentsSection.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);

    private async void Attachments_Drop(object sender, DragEventArgs e)
    {
        Attachments_DragLeave(sender, e);
        var item = _selected;
        if (item is null || !CanDropAttachments(e)) return;
        var deferral = e.GetDeferral();
        List<string> paths;
        try
        {
            var dropped = await e.DataView.GetStorageItemsAsync();
            paths = dropped.Select(storageItem => storageItem.Path).Where(path => !string.IsNullOrEmpty(path)).ToList();
        }
        catch (Exception error)
        {
            SetStatus($"读取拖入的文件失败：{error.Message}", StatusKind.Error, 9000);
            return;
        }
        finally
        {
            deferral.Complete();
        }
        CommitPendingEdit();
        await AddAttachmentsAsync(item, paths);
    }

    // ---- list --------------------------------------------------------------------------------------

    private void AttachmentList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is AttachmentRow row) OpenAttachment(row);
    }

    private async void AttachmentList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (AttachmentList.SelectedItem is not AttachmentRow row) return;
        switch (e.Key)
        {
            case VirtualKey.Enter:
                e.Handled = true;
                OpenAttachment(row);
                break;
            case VirtualKey.F2:
                e.Handled = true;
                await RenameAttachmentAsync(row);
                break;
            case VirtualKey.Delete:
                e.Handled = true;
                await DeleteAttachmentAsync(row);
                break;
        }
    }

    private void AttachmentRow_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if ((sender as FrameworkElement)?.DataContext is not AttachmentRow row) return;
        args.Handled = true;
        AttachmentList.SelectedItem = row;
        var menu = AttachmentMenu(row);
        if (args.TryGetPosition(sender, out var point)) menu.ShowAt(sender, new FlyoutShowOptions { Position = point });
        else menu.ShowAt((FrameworkElement)sender);
    }

    private void AttachmentMore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AttachmentRow row } button) return;
        AttachmentList.SelectedItem = row;
        AttachmentMenu(row).ShowAt(button, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight });
    }

    private MenuFlyout AttachmentMenu(AttachmentRow row)
    {
        MenuFlyoutItem Entry(string text, string glyph, Action action, string? shortcut = null)
        {
            var menuItem = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
            if (shortcut is not null) menuItem.KeyboardAcceleratorTextOverride = shortcut;
            menuItem.Click += (_, _) => action();
            return menuItem;
        }

        var menu = new MenuFlyout();
        menu.Items.Add(Entry("打开", "\uE8E5", () => OpenAttachment(row), "Enter"));
        menu.Items.Add(Entry("在文件夹中显示", "\uE838", () => ShowAttachmentInFolder(row)));
        menu.Items.Add(Entry("重命名", "\uE8AC", () => _ = RenameAttachmentAsync(row), "F2"));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Entry("删除", "\uE74D", () => _ = DeleteAttachmentAsync(row), "Delete"));
        return menu;
    }

    // ---- file operations ---------------------------------------------------------------------------

    private void OpenAttachment(AttachmentRow row)
    {
        try
        {
            Process.Start(new ProcessStartInfo(row.Entry.FullPath) { UseShellExecute = true });
        }
        catch (Exception error)
        {
            SetStatus($"无法打开“{row.Name}”：{error.Message}", StatusKind.Error, 9000);
            RefreshAttachments();
        }
    }

    private void ShowAttachmentInFolder(AttachmentRow row)
    {
        try
        {
            if (!NativeMethods.ShowInFolder(row.Entry.FullPath)) SetStatus($"找不到“{row.Name}”", StatusKind.Error);
        }
        catch (Exception error)
        {
            SetStatus(error.Message, StatusKind.Error);
        }
    }

    private async Task RenameAttachmentAsync(AttachmentRow row)
    {
        var entry = row.Entry;
        var extension = entry.IsFolder ? "" : Path.GetExtension(entry.Name);
        var nameBox = new TextBox
        {
            Header = "新名称",
            Text = entry.IsFolder ? entry.Name : Path.GetFileNameWithoutExtension(entry.Name),
            MinWidth = 280
        };
        nameBox.Loaded += (_, _) =>
        {
            nameBox.Focus(FocusState.Programmatic);
            nameBox.SelectAll();
        };
        var errorText = new TextBlock
        {
            Foreground = ThemeColors.System(RootGrid, "SystemFillColorCriticalBrush"),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        var nameRow = new Grid { ColumnSpacing = 8 };
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        nameRow.Children.Add(nameBox);
        if (extension.Length > 0)
        {
            var extensionText = new TextBlock { Text = extension, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6) };
            Grid.SetColumn(extensionText, 1);
            nameRow.Children.Add(extensionText);
        }
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(nameRow);
        content.Children.Add(errorText);

        string? newName = null;
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            RequestedTheme = RootGrid.ActualTheme,
            Title = entry.IsFolder ? "重命名文件夹" : "重命名附件",
            Content = content,
            PrimaryButtonText = "重命名",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var (name, error) = Attachments.RenameTarget(entry, nameBox.Text);
            if (error is null)
            {
                newName = name;
                return;
            }
            errorText.Text = error;
            errorText.Visibility = Visibility.Visible;
            args.Cancel = true;
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || newName is null || newName == entry.Name) return;

        try
        {
            Attachments.Rename(entry, newName);
            SetStatus($"已重命名为“{newName}”", StatusKind.Ok, 4000);
        }
        catch (Exception error)
        {
            SetStatus($"重命名失败：{error.Message}", StatusKind.Error, 9000);
        }
        RefreshAttachments();
    }

    private async Task DeleteAttachmentAsync(AttachmentRow row)
    {
        var entry = row.Entry;
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            RequestedTheme = RootGrid.ActualTheme,
            Title = "删除附件",
            Content = entry.IsFolder
                ? $"确定删除文件夹“{entry.Name}”吗？文件夹和其中的文件会移到回收站。"
                : $"确定删除“{entry.Name}”吗？文件会移到回收站。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            if (entry.IsFolder) FileSystem.DeleteDirectory(entry.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            else FileSystem.DeleteFile(entry.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            SetStatus($"已把“{entry.Name}”移到回收站", StatusKind.Ok, 4000);
        }
        catch (Exception error)
        {
            SetStatus($"删除失败：{error.Message}", StatusKind.Error, 9000);
        }
        RefreshAttachments();
    }
}
