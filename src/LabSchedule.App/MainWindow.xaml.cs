using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using LabSchedule.Core;
using LabSchedule.Timeline;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Globalization.NumberFormatting;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;
using WinRT.Interop;

namespace LabSchedule;

/// <summary>The editor window. The logic is a port of the Electron renderer (renderer.js).</summary>
public sealed partial class MainWindow : Window
{
    private enum StatusKind
    {
        Normal,
        Ok,
        Error
    }

    private sealed record EditSession(object Element, string Snapshot);

    private readonly ScheduleHistory _history = new(maxEntries: 100);
    private readonly ObservableCollection<ItemRow> _rows = [];
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _statusTimer;
    private readonly IntPtr _hwnd;

    private Schedule _schedule = new();
    private ScheduleItem? _selected;
    private bool _cascadeUpdates = true;
    private EditSession? _editSession;
    private string? _colorSessionSnapshot;
    private string _editorColor = ScheduleRules.DefaultColors[0];
    private string _savedFingerprint = "";
    private bool _hasSavedDefault;
    private bool _generating;
    private int _rendering;
    private StatusKind _statusKind;
    private readonly AppSettings _settings = AppSettings.Load(AppSettings.DefaultFilePath);

    public MainWindow()
    {
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);
        SetupWindow();
        ScheduleStore.AppVersion = Updater.CurrentVersion.ToString();
        ScheduleStore.OutputDirSetting = _settings.OutputDir;
        _cascadeUpdates = _settings.CascadeByDefault;
        ApplyTheme(_settings.Theme);

        YearInput.NumberFormatter = new DecimalFormatter
        {
            IsGrouped = false,
            FractionDigits = 0,
            IntegerDigits = 1,
            NumberRounder = new IncrementNumberRounder { Increment = 1, RoundingAlgorithm = RoundingAlgorithm.RoundHalfUp }
        };
        foreach (var picker in new[] { ItemStartInput, ItemEndInput })
        {
            picker.MinDate = new DateTimeOffset(new DateTime(1900, 1, 1));
            picker.MaxDate = new DateTimeOffset(new DateTime(2200, 12, 31));
        }

        ItemList.ItemsSource = _rows;
        TimelinePanel.Overlay = OverlayCanvas;
        TimelinePanel.BarPressed += Timeline_BarPressed;
        TimelinePanel.DragProgress += (_, text) => SetStatus(text, StatusKind.Normal, 0);
        TimelinePanel.DragCompleted += Timeline_DragCompleted;
        TimelinePanel.DragCanceled += (_, _) => RenderAll();
        RootGrid.ActualThemeChanged += (_, _) =>
        {
            UpdateCaptionButtons();
            UpdateDirtyState();
            ApplyStatusColor();
        };
        RootGrid.Loaded += (_, _) => UpdateCaptionButtons();

        _statusTimer = DispatcherQueue.CreateTimer();
        _statusTimer.IsRepeating = false;
        _statusTimer.Tick += (_, _) => ShowIssues();

        InitializeLayout();
        InitializeAttachments();
        Boot();
        InitializeSettingsPage();
        AnnounceUpdateAndCheck();
    }

    // ---- window ----------------------------------------------------------------------------------

    private void SetupWindow()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        var scale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min((int)(1460 * scale), workArea.Width);
        var height = Math.Min((int)(940 * scale), workArea.Height);
        AppWindow.MoveAndResize(new RectInt32(
            workArea.X + (workArea.Width - width) / 2,
            workArea.Y + (workArea.Height - height) / 2,
            width,
            height));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(1100 * scale);
            presenter.PreferredMinimumHeight = (int)(720 * scale);
        }
    }

    private void UpdateCaptionButtons()
    {
        var dark = RootGrid.ActualTheme == ElementTheme.Dark;
        var titleBar = AppWindow.TitleBar;
        titleBar.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        titleBar.ButtonHoverForegroundColor = dark ? Colors.White : Colors.Black;
        titleBar.ButtonHoverBackgroundColor = dark ? Windows.UI.Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF) : Windows.UI.Color.FromArgb(0x10, 0, 0, 0);
        titleBar.ButtonPressedBackgroundColor = dark ? Windows.UI.Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF) : Windows.UI.Color.FromArgb(0x0A, 0, 0, 0);
        titleBar.ButtonInactiveForegroundColor = dark ? Windows.UI.Color.FromArgb(0x87, 0xFF, 0xFF, 0xFF) : Windows.UI.Color.FromArgb(0x72, 0, 0, 0);
    }

    // ---- boot ------------------------------------------------------------------------------------

    private void Boot()
    {
        var payload = ScheduleStore.LoadInitial();
        _schedule = ScheduleRules.NormalizeState(payload.Schedule);
        ScheduleRules.RenumberInPlace(_schedule.Items);
        _selected = _schedule.Items.FirstOrDefault();
        _hasSavedDefault = payload.FromDefaultJson;
        _savedFingerprint = _hasSavedDefault ? Fingerprint() : "";
        _history.Reset(SnapshotState());
        RenderAll();
        if (!string.IsNullOrEmpty(payload.Warning))
        {
            SetStatus(payload.Warning, StatusKind.Error, 9000);
        }
        else if (payload.FromDefaultJson)
        {
            SetStatus("已加载默认 JSON", StatusKind.Ok, 4000);
        }
    }

    // ---- rendering -------------------------------------------------------------------------------

    private void RenderAll()
    {
        ScheduleRules.RenumberInPlace(_schedule.Items);
        _attachmentCounts.Clear();
        RenderConfig();
        RenderEditor();
        RenderAttachments();
        RenderList();
        RenderTimeline();
        RenderSummary();
        ScrollSelectedIntoView();
        UpdateButtons();
        ShowIssues();
        UpdateDirtyState();
    }

    private void RenderConfig()
    {
        _rendering++;
        try
        {
            if (YearInput.Value != _schedule.Year) YearInput.Value = _schedule.Year;
            SetText(TitleInput, string.IsNullOrEmpty(_schedule.Title) ? $"{_schedule.Year}年度实验时间安排图" : _schedule.Title);
            PageSizeInput.SelectedIndex = _schedule.Settings.PageSize == "A3" ? 1 : _schedule.Settings.PageSize == "A4" ? 0 : -1;
            OrientationInput.SelectedIndex = _schedule.Settings.Orientation == "portrait" ? 1 : _schedule.Settings.Orientation == "landscape" ? 0 : -1;
            CascadeInput.IsChecked = _cascadeUpdates;
            WeekendInput.IsChecked = _schedule.Settings.ShowWeekends;
            MaintenanceListInput.IsChecked = _schedule.Settings.ShowMaintenanceInList;
        }
        finally
        {
            _rendering--;
        }
    }

    private void RenderEditor()
    {
        _rendering++;
        try
        {
            var item = _selected;
            foreach (var control in new Control[] { ItemTypeInput, ItemNameInput, ItemLeaderInput, ItemUnitInput, ItemPhoneInput, ItemEmailInput, ItemColorButton, ItemStartInput, ItemEndInput, ItemDescriptionInput })
            {
                control.IsEnabled = item is not null;
            }
            if (item is null)
            {
                SetText(ItemIdInput, "");
                ItemTypeInput.SelectedIndex = 0;
                SetText(ItemNameInput, "");
                SetText(ItemLeaderInput, "");
                SetText(ItemUnitInput, "");
                SetText(ItemPhoneInput, "");
                SetText(ItemEmailInput, "");
                ShowEditorColor(ScheduleRules.DefaultColors[0]);
                ItemStartInput.Date = null;
                ItemEndInput.Date = null;
                SetText(ItemDurationInput, "");
                SetText(ItemDescriptionInput, "");
                return;
            }

            SetText(ItemIdInput, item.Id);
            ItemTypeInput.SelectedIndex = item.IsMaintenance ? 1 : 0;
            SetText(ItemNameInput, item.Name, trimAware: true);
            SetText(ItemLeaderInput, item.Leader, trimAware: true);
            SetText(ItemUnitInput, item.Unit, trimAware: true);
            SetText(ItemPhoneInput, item.Phone, trimAware: true);
            SetText(ItemEmailInput, item.Email, trimAware: true);
            ShowEditorColor(ColorForItem(item));
            ItemColorButton.IsEnabled = !item.IsMaintenance;
            SetDate(ItemStartInput, item.StartDate);
            SetDate(ItemEndInput, item.EndDate);
            SetText(ItemDurationInput, $"{ScheduleRules.DurationDays(item)} 天");
            SetText(ItemDescriptionInput, item.Description, trimAware: true);
        }
        finally
        {
            _rendering--;
        }
    }

    private void ShowEditorColor(string color)
    {
        _editorColor = color.ToUpperInvariant();
        ItemColorSwatch.Background = new SolidColorBrush(ColorUtil.Parse(_editorColor));
        ItemColorText.Text = _editorColor;
    }

    private void RenderList()
    {
        _rendering++;
        try
        {
            var listed = ListedItems();
            if (_rows.Count != listed.Count)
            {
                _rows.Clear();
                foreach (var item in listed) _rows.Add(new ItemRow(item));
            }
            for (var index = 0; index < listed.Count; index++)
            {
                _rows[index].Update(listed[index], ColorForItem(listed[index]), AttachmentCount(listed[index]));
            }
            var selectedRow = _rows.FirstOrDefault(row => ReferenceEquals(row.Item, _selected));
            if (!ReferenceEquals(ItemList.SelectedItem, selectedRow)) ItemList.SelectedItem = selectedRow;
        }
        finally
        {
            _rendering--;
        }
    }

    private void RenderTimeline() => TimelinePanel.Render(_schedule, _selected, _cascadeUpdates, ColorForItem);

    private void RenderSummary()
    {
        var days = _schedule.Items.Sum(ScheduleRules.DurationDays);
        SummaryText.Text = $"{_schedule.Year} · {_schedule.Items.Count} 项安排 · {days} 天占用";
    }

    private void ScrollSelectedIntoView()
    {
        var row = _rows.FirstOrDefault(candidate => ReferenceEquals(candidate.Item, _selected));
        if (row is not null) ItemList.ScrollIntoView(row);
        TimelinePanel.ScrollSelectedIntoView();
    }

    private void UpdateButtons()
    {
        var hasItem = _selected is not null;
        DeleteButton.IsEnabled = hasItem;
        DuplicateButton.IsEnabled = hasItem;
        UndoButton.IsEnabled = _history.CanUndo;
        RedoButton.IsEnabled = _history.CanRedo;
        GenerateButton.IsEnabled = !_generating;
    }

    private void UpdateDirtyState()
    {
        var dirty = !_hasSavedDefault || _savedFingerprint != Fingerprint();
        DirtyText.Text = dirty ? "未保存" : "已保存";
        DirtyBadge.Background = ThemeColors.System(RootGrid, dirty ? "SystemFillColorCautionBackgroundBrush" : "SystemFillColorSuccessBackgroundBrush");
        DirtyText.Foreground = ThemeColors.System(RootGrid, dirty ? "SystemFillColorCautionBrush" : "SystemFillColorSuccessBrush");
        if (RootGrid.ActualTheme == ElementTheme.Light)
        {
            // The caution/success fills are too light to read as text on light backgrounds.
            DirtyText.Foreground = new SolidColorBrush(dirty ? Windows.UI.Color.FromArgb(255, 0x8A, 0x4B, 0x00) : Windows.UI.Color.FromArgb(255, 0x11, 0x5E, 0x59));
        }
    }

    private static void SetText(TextBox box, string value, bool trimAware = false)
    {
        var current = NormalizeNewlines(box.Text);
        if (current == value || (trimAware && current.Trim() == value)) return;
        box.Text = value;
    }

    private static void SetDate(CalendarDatePicker picker, string iso)
    {
        var date = ScheduleRules.ParseDate(iso);
        if (ReadDate(picker) == (date is null ? "" : iso)) return;
        picker.Date = date is null ? null : new DateTimeOffset(date.Value.ToDateTime(TimeOnly.MinValue));
    }

    private static string ReadDate(CalendarDatePicker picker) =>
        picker.Date is { } value ? ScheduleRules.DateToIso(DateOnly.FromDateTime(value.DateTime)) : "";

    private static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    // ---- state helpers ---------------------------------------------------------------------------

    private List<ScheduleItem> ListedItems() => _schedule.Settings.ShowMaintenanceInList
        ? _schedule.Items.ToList()
        : _schedule.Items.Where(item => !item.IsMaintenance).ToList();

    private string ColorForItem(ScheduleItem item) => ScheduleEditing.ColorForItem(_schedule, item);

    private string Fingerprint() => ScheduleJson.Stringify(ScheduleJson.ToJsonNode(ScheduleRules.NormalizeState(_schedule)));

    private Schedule CurrentPayload()
    {
        CommitPendingEdit();
        ScheduleRules.RenumberInPlace(_schedule.Items);
        return ScheduleRules.NormalizeState(_schedule);
    }

    // ---- history ---------------------------------------------------------------------------------

    private string SnapshotState() => ScheduleJson.Stringify(new JsonObject
    {
        ["schedule"] = ScheduleJson.ToJsonNode(ScheduleRules.NormalizeState(_schedule)),
        ["selectedId"] = _selected?.Id,
        ["cascadeUpdates"] = _cascadeUpdates
    });

    private void RestoreSnapshot(string snapshot)
    {
        var state = ScheduleJson.Parse(snapshot)!.AsObject();
        _schedule = ScheduleRules.NormalizeState(ScheduleRules.NormalizeJson(state["schedule"]));
        var selectedId = state["selectedId"]?.GetValue<string>();
        _selected = _schedule.Items.FirstOrDefault(item => item.Id == selectedId) ?? _schedule.Items.FirstOrDefault();
        _cascadeUpdates = state["cascadeUpdates"]?.GetValue<bool>() != false;
        RenderAll();
    }

    private void RecordHistory(string label) => _history.Commit(SnapshotState(), label);

    private void BeginFormEdit(object element)
    {
        if (element is Control { IsEnabled: false } or TextBox { IsReadOnly: true }) return;
        if (_editSession is not null && ReferenceEquals(_editSession.Element, element)) return;
        CommitPendingEdit();
        _editSession = new EditSession(element, SnapshotState());
    }

    private void FinishFormEdit()
    {
        if (_editSession is null) return;
        var snapshot = _editSession.Snapshot;
        _editSession = null;
        _history.CommitFrom(snapshot, SnapshotState(), "编辑内容");
        UpdateButtons();
        UpdateDirtyState();
    }

    private void CommitPendingEdit() => FinishFormEdit();

    /// <summary>
    /// Applies a change coming from an editor control. Text fields are recorded when they lose focus;
    /// everything else (lists, check boxes, dates) becomes its own undo step immediately.
    /// </summary>
    private void ApplyFormChange(object source, Action mutate)
    {
        if (_editSession is not null && ReferenceEquals(_editSession.Element, source))
        {
            mutate();
            return;
        }
        CommitPendingEdit();
        if (source is TextBox { FocusState: not FocusState.Unfocused })
        {
            // Still typing in a box that kept focus across an undo or a calendar click.
            _editSession = new EditSession(source, SnapshotState());
            mutate();
            return;
        }
        var before = SnapshotState();
        mutate();
        _history.CommitFrom(before, SnapshotState(), "编辑内容");
        UpdateButtons();
        UpdateDirtyState();
    }

    private void Undo()
    {
        CommitPendingEdit();
        var snapshot = _history.Undo();
        if (snapshot is null) return;
        RestoreSnapshot(snapshot);
        SetStatus("已撤销", StatusKind.Ok, 2000);
    }

    private void Redo()
    {
        CommitPendingEdit();
        var snapshot = _history.Redo();
        if (snapshot is null) return;
        RestoreSnapshot(snapshot);
        SetStatus("已重做", StatusKind.Ok, 2000);
    }

    // ---- status ----------------------------------------------------------------------------------

    private void ShowIssues()
    {
        var issues = ScheduleRules.CollectIssues(_schedule);
        if (issues.Count > 0) SetStatus(issues[0], StatusKind.Error, 0);
        else SetStatus("就绪", StatusKind.Ok, 0);
    }

    private void SetStatus(string message, StatusKind kind = StatusKind.Normal, int timeoutMs = 3000)
    {
        _statusTimer.Stop();
        _statusKind = kind;
        StatusText.Text = message;
        ToolTipService.SetToolTip(StatusText, message);
        ApplyStatusColor();
        if (timeoutMs > 0)
        {
            _statusTimer.Interval = TimeSpan.FromMilliseconds(timeoutMs);
            _statusTimer.Start();
        }
    }

    private void ApplyStatusColor() =>
        StatusText.Foreground = ThemeColors.System(RootGrid, _statusKind switch
        {
            StatusKind.Error => "SystemFillColorCriticalBrush",
            StatusKind.Ok => "SystemFillColorSuccessBrush",
            _ => "TextFillColorSecondaryBrush"
        });

    // ---- configuration panel ---------------------------------------------------------------------

    private void UpdateConfigFromForm()
    {
        var year = double.IsNaN(YearInput.Value) ? DateTime.Now.Year : (int)Math.Round(YearInput.Value);
        _schedule.Year = year;
        _schedule.Title = string.IsNullOrEmpty(TitleInput.Text) ? $"{year}年度实验时间安排图" : TitleInput.Text;
        _schedule.Settings.PageSize = (PageSizeInput.SelectedItem as ComboBoxItem)?.Tag as string ?? _schedule.Settings.PageSize;
        _schedule.Settings.Orientation = (OrientationInput.SelectedItem as ComboBoxItem)?.Tag as string ?? _schedule.Settings.Orientation;
        _cascadeUpdates = CascadeInput.IsChecked == true;
        _schedule.Settings.ShowWeekends = WeekendInput.IsChecked == true;
        _schedule.Settings.ShowMaintenanceInList = MaintenanceListInput.IsChecked == true;
        RenderList();
        RenderTimeline();
        RenderSummary();
        ShowIssues();
        UpdateDirtyState();
    }

    private void YearInput_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_rendering > 0) return;
        var year = double.IsNaN(sender.Value) ? DateTime.Now.Year : (int)Math.Round(sender.Value);
        if (year == _schedule.Year) return;
        ApplyFormChange(sender, UpdateConfigFromForm);
    }

    private void ConfigText_TextChanged(object sender, TextChangedEventArgs e)
    {
        var title = string.IsNullOrEmpty(TitleInput.Text) ? $"{_schedule.Year}年度实验时间安排图" : TitleInput.Text;
        if (title == _schedule.Title) return;
        ApplyFormChange(sender, UpdateConfigFromForm);
    }

    private void ConfigCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_rendering > 0 || (sender as ComboBox)?.SelectedItem is null) return;
        ApplyFormChange(sender, UpdateConfigFromForm);
    }

    private void ConfigCheck_Click(object sender, RoutedEventArgs e) => ApplyFormChange(sender, UpdateConfigFromForm);

    private void HistoryField_GotFocus(object sender, RoutedEventArgs e) => BeginFormEdit(sender);

    private void HistoryField_LostFocus(object sender, RoutedEventArgs e) => FinishFormEdit();

    // ---- item editor -----------------------------------------------------------------------------

    private void UpdateItemFromForm()
    {
        var item = _selected;
        if (item is null) return;
        item.Type = (ItemTypeInput.SelectedItem as ComboBoxItem)?.Tag as string == ItemTypes.Maintenance ? ItemTypes.Maintenance : ItemTypes.Experiment;
        item.Name = ItemNameInput.Text.Trim();
        item.Leader = ItemLeaderInput.Text.Trim();
        item.Unit = ItemUnitInput.Text.Trim();
        item.Phone = ItemPhoneInput.Text.Trim();
        item.Email = ItemEmailInput.Text.Trim();
        item.Color = item.IsMaintenance ? ScheduleRules.MaintenanceColor : _editorColor;
        item.Description = NormalizeNewlines(ItemDescriptionInput.Text).Trim();
        ScheduleEditing.ApplyDateChange(_schedule, item, ReadDate(ItemStartInput), ReadDate(ItemEndInput), _cascadeUpdates);
        RenderList();
        RenderTimeline();
        RenderEditor();
        RenderSummary();
        ShowIssues();
        UpdateDirtyState();
    }

    private void ItemText_TextChanged(object sender, TextChangedEventArgs e)
    {
        var item = _selected;
        if (item is null || sender is not TextBox box) return;
        var value = NormalizeNewlines(box.Text).Trim();
        var current = ReferenceEquals(box, ItemNameInput) ? item.Name
            : ReferenceEquals(box, ItemLeaderInput) ? item.Leader
            : ReferenceEquals(box, ItemUnitInput) ? item.Unit
            : ReferenceEquals(box, ItemPhoneInput) ? item.Phone
            : ReferenceEquals(box, ItemEmailInput) ? item.Email
            : item.Description;
        if (value == current) return;
        ApplyFormChange(sender, UpdateItemFromForm);
    }

    private void ItemTypeInput_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_rendering > 0 || _selected is null || ItemTypeInput.SelectedItem is not ComboBoxItem { Tag: string type }) return;
        if (type == _selected.Type) return;
        ApplyFormChange(sender, UpdateItemFromForm);
    }

    private void ItemDate_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (_rendering > 0 || _selected is null) return;
        if (args.NewDate is null)
        {
            // Picking another day can briefly clear the date before setting the new one, and clicking the
            // selected day clears it. Dates can't be emptied here, so ignore that and restore if needed.
            DispatcherQueue.TryEnqueue(() =>
            {
                if (sender.Date is not null || _selected is null) return;
                _rendering++;
                try
                {
                    SetDate(sender, ReferenceEquals(sender, ItemStartInput) ? _selected.StartDate : _selected.EndDate);
                }
                finally
                {
                    _rendering--;
                }
            });
            return;
        }
        var current = ReferenceEquals(sender, ItemStartInput) ? _selected.StartDate : _selected.EndDate;
        if (ReadDate(sender) == current) return;
        ApplyFormChange(sender, UpdateItemFromForm);
    }

    private void ItemColorFlyout_Opening(object sender, object e)
    {
        CommitPendingEdit();
        _colorSessionSnapshot = SnapshotState();
        _rendering++;
        try
        {
            ItemColorPicker.Color = ColorUtil.Parse(_editorColor);
        }
        finally
        {
            _rendering--;
        }
    }

    private void ItemColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_rendering > 0 || _selected is null || _selected.IsMaintenance) return;
        var hex = ColorUtil.ToHex(args.NewColor);
        if (hex == _editorColor) return;
        ShowEditorColor(hex);
        UpdateItemFromForm();
    }

    private void ItemColorFlyout_Closed(object sender, object e)
    {
        if (_colorSessionSnapshot is null) return;
        _history.CommitFrom(_colorSessionSnapshot, SnapshotState(), "编辑内容");
        _colorSessionSnapshot = null;
        UpdateButtons();
        UpdateDirtyState();
    }

    // ---- list and calendar -----------------------------------------------------------------------

    private void ItemList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_rendering > 0 || ItemList.SelectedItem is not ItemRow row || ReferenceEquals(row.Item, _selected)) return;
        _selected = row.Item;
        RenderAll();
    }

    private void Timeline_BarPressed(object? sender, ScheduleItem item)
    {
        CommitPendingEdit();
        _selected = item;
    }

    private void Timeline_DragCompleted(object? sender, TimelineDragCompletedEventArgs e)
    {
        if (!e.Moved)
        {
            _selected = e.Item;
            RenderAll();
            return;
        }
        ScheduleEditing.ApplyDateChange(_schedule, e.Item, e.Start, e.End, _cascadeUpdates);
        RecordHistory(e.Mode == DragMode.Move ? "拖动安排" : "调整时长");
        RenderAll();
        SetStatus("已更新日期并重排编号", StatusKind.Ok, 3000);
    }

    // ---- item commands ---------------------------------------------------------------------------

    private void AddExperimentButton_Click(object sender, RoutedEventArgs e) => AddItem(ItemTypes.Experiment);

    private void AddMaintenanceButton_Click(object sender, RoutedEventArgs e) => AddItem(ItemTypes.Maintenance);

    private void AddItem(string type)
    {
        CommitPendingEdit();
        var selected = _selected;
        _selected = ScheduleEditing.InsertItem(_schedule, type, selected, _cascadeUpdates);
        RecordHistory(type == ItemTypes.Maintenance ? "插入维护" : "插入实验");
        RenderAll();
        SetStatus(selected is not null && _cascadeUpdates ? "已插入，并平移后续安排 7 天" : "已插入新安排", StatusKind.Ok, 3000);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        CommitPendingEdit();
        var hadAttachments = AttachmentCount(_selected) > 0;
        _schedule.Items.Remove(_selected);
        _selected = _schedule.Items.FirstOrDefault();
        RecordHistory("删除安排");
        RenderAll();
        // The files stay on disk; undo links them again.
        if (hadAttachments) SetStatus("已删除安排；它的附件仍保留在附件文件夹中，撤销即可恢复关联", StatusKind.Ok, 6000);
    }

    private void DuplicateButton_Click(object sender, RoutedEventArgs e)
    {
        var item = _selected;
        if (item is null) return;
        CommitPendingEdit();
        var copy = item.Clone();
        copy.Id = "";
        copy.Name = $"{item.Name} 副本";
        copy.AttachmentFolder = "";
        _schedule.Items.Add(copy);
        ScheduleRules.RenumberInPlace(_schedule.Items);
        _selected = copy;
        RecordHistory("复制安排");
        RenderAll();
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e) => Undo();

    private void RedoButton_Click(object sender, RoutedEventArgs e) => Redo();

    private void RootGrid_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        static bool IsDown(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
        if (IsSettingsOpen)
        {
            if (e.Key == VirtualKey.Escape)
            {
                e.Handled = true;
                CloseSettings();
            }
            return;
        }
        if (!IsDown(VirtualKey.Control) || IsDown(VirtualKey.Menu) || TimelinePanel.IsDragging) return;
        if (e.Key == VirtualKey.Z && IsDown(VirtualKey.Shift))
        {
            e.Handled = true;
            Redo();
        }
        else if (e.Key == VirtualKey.Z)
        {
            e.Handled = true;
            Undo();
        }
        else if (e.Key == VirtualKey.Y)
        {
            e.Handled = true;
            Redo();
        }
    }

    // ---- files -----------------------------------------------------------------------------------

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingEdit();
        var path = NativeMethods.ShowOpenDialog(_hwnd, "导入排班 JSON", "JSON 文件", "json");
        if (path is null) return;
        var result = ScheduleStore.Import(path);
        if (!result.Ok)
        {
            SetStatus(result.Error!, StatusKind.Error, 9000);
            return;
        }
        _schedule = ScheduleRules.NormalizeState(result.Value!);
        _selected = _schedule.Items.FirstOrDefault();
        _hasSavedDefault = false;
        RecordHistory("导入 JSON");
        RenderAll();
        SetStatus("已导入 JSON", StatusKind.Ok);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingEdit();
        var issues = ScheduleRules.CollectIssues(_schedule);
        if (issues.Count > 0)
        {
            SetStatus(issues[0], StatusKind.Error);
            return;
        }
        StoreResult<string> result;
        try
        {
            result = ScheduleStore.SaveDefault(CurrentPayload());
        }
        catch (Exception error)
        {
            SetStatus($"保存失败：{error.Message}", StatusKind.Error, 9000);
            return;
        }
        if (!result.Ok)
        {
            SetStatus(result.Error!, StatusKind.Error, 9000);
            return;
        }
        _hasSavedDefault = true;
        _savedFingerprint = Fingerprint();
        UpdateDirtyState();
        SetStatus($"已保存到默认 JSON：{result.Value}", StatusKind.Ok, 6000);
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingEdit();
        var issues = ScheduleRules.CollectIssues(_schedule);
        if (issues.Count > 0)
        {
            SetStatus(issues[0], StatusKind.Error);
            return;
        }
        var payload = CurrentPayload();
        var validation = ScheduleRules.Validate(payload);
        if (validation.Count > 0)
        {
            SetStatus(validation[0], StatusKind.Error, 9000);
            return;
        }
        var defaultPath = Path.Combine(ScheduleStore.DefaultOutputDir, $"schedule_{ScheduleRules.PrepareSchedule(payload).Year}.json");
        var path = NativeMethods.ShowSaveDialog(_hwnd, "导出排班 JSON", "JSON 文件", "json", defaultPath);
        if (path is null) return;
        try
        {
            var result = ScheduleStore.WriteJson(payload, path);
            if (!result.Ok)
            {
                SetStatus(result.Error!, StatusKind.Error, 9000);
                return;
            }
            SetStatus($"已导出：{result.Value}", StatusKind.Ok, 6000);
        }
        catch (Exception error)
        {
            SetStatus($"导出失败：{error.Message}", StatusKind.Error, 9000);
        }
    }

    private async void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingEdit();
        var issues = ScheduleRules.CollectIssues(_schedule);
        if (issues.Count > 0)
        {
            SetStatus(issues[0], StatusKind.Error);
            return;
        }
        var payload = CurrentPayload();
        var validation = ScheduleRules.Validate(payload);
        if (validation.Count > 0)
        {
            SetStatus(validation[0], StatusKind.Error, 9000);
            return;
        }
        var prepared = ScheduleRules.PrepareSchedule(payload);

        // Output always goes to the folder chosen on the settings page.
        var outputs = ScheduleStore.DefaultPaths(prepared);
        var pdfPath = outputs.PdfPath;
        var htmlPath = outputs.HtmlPath;

        SetStatus("正在生成", StatusKind.Normal, 0);
        _generating = true;
        UpdateButtons();
        try
        {
            var render = OutputHtml.BuildResult(prepared);
            ScheduleStore.WriteText(htmlPath, render.Html);
            await PdfPrinter.PrintAsync(PrintHost, htmlPath, pdfPath, render.Schedule.Settings);
            ScheduleStore.WriteText(
                ScheduleStore.RenderLogPath(pdfPath),
                ScheduleStore.BuildRenderLog(pdfPath, htmlPath, render.Schedule, render.Warnings, DateTime.UtcNow));

            var warningText = render.Warnings.Count > 0 ? $"；{render.Warnings.Count} 条提醒" : "";
            SetStatus($"已生成：{pdfPath}{warningText}", StatusKind.Ok, 8000);
        }
        catch (Exception error)
        {
            SetStatus(error.Message, StatusKind.Error, 9000);
        }
        finally
        {
            _generating = false;
            UpdateButtons();
        }
    }

    private void ShowFolderButton_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingEdit();
        var outputs = ScheduleStore.DefaultPaths(_schedule);
        try
        {
            // Select the generated PDF when there is one, otherwise just open the folder.
            var target = File.Exists(outputs.PdfPath) ? outputs.PdfPath : ScheduleStore.DefaultOutputDir;
            if (!NativeMethods.ShowInFolder(target)) SetStatus($"输出文件夹不存在：{ScheduleStore.DefaultOutputDir}", StatusKind.Error);
        }
        catch (Exception error)
        {
            SetStatus(error.Message, StatusKind.Error);
        }
    }
}
