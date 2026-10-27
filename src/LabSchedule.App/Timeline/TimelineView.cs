using System.Numerics;
using LabSchedule.Core;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace LabSchedule.Timeline;

public sealed class TimelineDragCompletedEventArgs(ScheduleItem item, DragMode mode, bool moved, string start, string end) : EventArgs
{
    public ScheduleItem Item { get; } = item;
    public DragMode Mode { get; } = mode;
    public bool Moved { get; } = moved;
    public string Start { get; } = start;
    public string End { get; } = end;
}

/// <summary>
/// The yearly calendar: one row per month, 31 day columns, overlapping items split into lanes.
/// Bars can be dragged to move an item, or dragged by their edges to change start/end dates.
/// </summary>
public sealed partial class TimelineView : UserControl
{
    private const double MonthLabelWidth = 58;
    private const double HeaderHeight = 30;
    private const double LaneHeight = 25;
    private const double LaneGap = 5;
    private const double TrackPadding = 9;
    private const double HandleWidth = 12;
    private const double MinTimelineWidth = 880;

    private readonly Border _frame;
    private readonly Grid _header;
    private readonly StackPanel _months;
    private readonly ScrollViewer _horizontalScroll;

    private Schedule? _schedule;
    private ScheduleItem? _selected;
    private bool _cascade;
    private Func<ScheduleItem, string> _colorFor = _ => ScheduleRules.DefaultColors[0];
    private FrameworkElement? _activeBar;
    private DragState? _drag;

    public TimelineView()
    {
        _header = new Grid { Height = HeaderHeight };
        _months = new StackPanel();
        var verticalScroll = new ScrollViewer
        {
            Content = _months,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var frameGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }
            }
        };
        Grid.SetRow(verticalScroll, 1);
        frameGrid.Children.Add(_header);
        frameGrid.Children.Add(verticalScroll);

        _frame = new Border
        {
            MinWidth = MinTimelineWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = frameGrid
        };
        _horizontalScroll = new ScrollViewer
        {
            Content = _frame,
            Padding = new Thickness(12),
            HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Content = _horizontalScroll;

        // The horizontal scroller measures its content with unlimited width, which would let long
        // labels widen the day columns. Pin the calendar to the visible width (at least 880).
        _horizontalScroll.SizeChanged += (_, args) =>
            _frame.Width = Math.Max(MinTimelineWidth, args.NewSize.Width - _horizontalScroll.Padding.Left - _horizontalScroll.Padding.Right);

        ActualThemeChanged += (_, _) => RenderCurrent();
    }

    /// <summary>Window-level canvas used for the floating drag ghost and tooltip.</summary>
    public Canvas? Overlay { get; set; }

    /// <summary>A bar was pressed; the item becomes the selection.</summary>
    public event EventHandler<ScheduleItem>? BarPressed;

    public event EventHandler<TimelineDragCompletedEventArgs>? DragCompleted;

    /// <summary>Status text while dragging, e.g. "移动：+3 天".</summary>
    public event EventHandler<string>? DragProgress;

    public event EventHandler? DragCanceled;

    public bool IsDragging => _drag is not null;

    public void Render(Schedule schedule, ScheduleItem? selected, bool cascade, Func<ScheduleItem, string> colorFor)
    {
        _schedule = schedule;
        _selected = selected;
        _cascade = cascade;
        _colorFor = colorFor;
        RenderCurrent();
    }

    public void ScrollSelectedIntoView()
    {
        var bar = _activeBar;
        if (bar is null) return;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (bar.XamlRoot is null) return;
            bar.StartBringIntoView(new BringIntoViewOptions { HorizontalAlignmentRatio = 0.5, AnimationDesired = false });
        });
    }

    // ---- rendering ----------------------------------------------------------------------------

    private void RenderCurrent()
    {
        if (_schedule is null || _drag is not null) return;
        _frame.Background = ThemeColors.Brush(this, "TimelineSurface");
        _frame.BorderBrush = ThemeColors.Brush(this, "TimelineBorder");
        _horizontalScroll.Background = ThemeColors.System("CardBackgroundFillColorSecondaryBrush");
        BuildHeader();

        _activeBar = null;
        _months.Children.Clear();
        var segmentsByMonth = TimelineLayout.BuildSegments(_schedule);
        for (var month = 1; month <= 12; month++)
        {
            _months.Children.Add(BuildMonthRow(month, segmentsByMonth.TryGetValue(month, out var list) ? list : []));
        }
    }

    private void BuildHeader()
    {
        _header.Children.Clear();
        _header.ColumnDefinitions.Clear();
        _header.Background = ThemeColors.Brush(this, "TimelineHeaderBackground");
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MonthLabelWidth) });
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var foreground = ThemeColors.Brush(this, "TimelineHeaderForeground");
        var divider = ThemeColors.Brush(this, "TimelineHeaderDivider");

        _header.Children.Add(new Border
        {
            BorderBrush = ThemeColors.Brush(this, "TimelineBorder"),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = HeaderText("月份", foreground)
        });

        var days = DayColumnsGrid();
        for (var day = 1; day <= 31; day++)
        {
            var cell = new Border
            {
                BorderBrush = divider,
                BorderThickness = new Thickness(day == 1 ? 0 : 1, 0, 0, 0),
                Child = HeaderText(day.ToString(), foreground)
            };
            Grid.SetColumn(cell, day - 1);
            days.Children.Add(cell);
        }
        Grid.SetColumn(days, 1);
        _header.Children.Add(days);
    }

    private static TextBlock HeaderText(string text, Brush foreground) => new()
    {
        Text = text,
        Foreground = foreground,
        FontSize = 12,
        FontWeight = FontWeights.Bold,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Grid DayColumnsGrid()
    {
        var grid = new Grid();
        for (var day = 1; day <= 31; day++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        return grid;
    }

    private Grid BuildMonthRow(int month, List<TimelineSegment> segments)
    {
        var year = _schedule!.Year;
        var lanes = Math.Max(1, segments.Count == 0 ? 1 : segments.Max(segment => segment.Lane + 1));
        var borderBrush = ThemeColors.Brush(this, "TimelineBorder");

        var row = new Grid
        {
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(MonthLabelWidth) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            }
        };

        row.Children.Add(new Border
        {
            Background = ThemeColors.Brush(this, "TimelineMonthBackground"),
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new TextBlock
            {
                Text = $"{month}月",
                FontWeight = FontWeights.Bold,
                Foreground = ThemeColors.Brush(this, "TimelineMonthForeground"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });

        var host = new Grid
        {
            Background = ThemeColors.Brush(this, "TimelineTrackBackground"),
            MinHeight = lanes * LaneHeight + (lanes - 1) * LaneGap + TrackPadding * 2
        };
        Grid.SetColumn(host, 1);
        row.Children.Add(host);

        // Day fills (invalid dates, weekends) sit underneath everything.
        var fills = DayColumnsGrid();
        var monthDays = ScheduleRules.DaysInMonth(year, month);
        for (var day = 1; day <= 31; day++)
        {
            string? key = null;
            if (day > monthDays) key = "TimelineInvalidDay";
            else if (_schedule.Settings.ShowWeekends && ScheduleRules.IsWeekend(year, month, day)) key = "TimelineWeekend";
            if (key is null) continue;
            var fill = new Rectangle { Fill = ThemeColors.Brush(this, key) };
            Grid.SetColumn(fill, day - 1);
            fills.Children.Add(fill);
        }
        host.Children.Add(fills);

        var bars = DayColumnsGrid();
        bars.Margin = new Thickness(0, TrackPadding, 0, TrackPadding);
        bars.RowSpacing = LaneGap;
        for (var lane = 0; lane < lanes; lane++)
        {
            bars.RowDefinitions.Add(new RowDefinition { Height = new GridLength(LaneHeight) });
        }
        host.Children.Add(bars);

        var track = new Track(month, host, bars);
        foreach (var segment in segments)
        {
            AddBar(track, segment);
        }

        // Day separators are drawn above the bars, like the printed calendar.
        var lines = DayColumnsGrid();
        lines.IsHitTestVisible = false;
        var lineBrush = ThemeColors.Brush(this, "TimelineDayLine");
        for (var day = 1; day <= 31; day++)
        {
            var line = new Border
            {
                BorderBrush = lineBrush,
                BorderThickness = new Thickness(1, 0, day == 31 ? 1 : 0, 0)
            };
            Grid.SetColumn(line, day - 1);
            lines.Children.Add(line);
        }
        host.Children.Add(lines);

        return row;
    }

    private void AddBar(Track track, TimelineSegment segment)
    {
        var item = segment.Item;
        var label = ScheduleRules.ItemDisplayLabel(item);
        var color = _colorFor(item);
        var textColor = ScheduleRules.IsDark(color) ? Colors.White : Color.FromArgb(255, 0x17, 0x20, 0x22);
        var active = ReferenceEquals(item, _selected);

        if (active)
        {
            // Selection ring just outside the bar.
            var ring = new Border
            {
                Margin = new Thickness(0, -2, 0, -2),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(ColorUtil.WithAlpha(ThemeColors.SystemColor("SystemAccentColor"), 0.6)),
                IsHitTestVisible = false
            };
            PlaceInTrack(ring, segment.StartDay, segment.EndDay, segment.Lane);
            track.Bars.Children.Add(ring);
        }

        var bar = new CursorGrid
        {
            Cursor = InputSystemCursor.Create(InputSystemCursorShape.Hand),
            Background = new SolidColorBrush(ColorUtil.Parse(color)),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(2, 0, 2, 0),
            TranslationTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(160) },
            Tag = new BarInfo(item, track, segment.Lane, label, color, textColor)
        };
        bar.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(textColor),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 7, 0),
            IsHitTestVisible = false
        });
        if (segment.IsStart) bar.Children.Add(Handle(DragMode.ResizeStart, textColor));
        if (segment.IsEnd) bar.Children.Add(Handle(DragMode.ResizeEnd, textColor));
        if (active)
        {
            bar.Children.Add(new Border
            {
                Margin = new Thickness(1),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(5),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xDB, 0xFF, 0xFF, 0xFF)),
                IsHitTestVisible = false
            });
            _activeBar = bar;
        }

        ToolTipService.SetToolTip(bar, label);
        PlaceInTrack(bar, segment.StartDay, segment.EndDay, segment.Lane);
        bar.PointerEntered += Bar_PointerEntered;
        bar.PointerExited += Bar_PointerExited;
        bar.PointerPressed += Bar_PointerPressed;
        bar.PointerMoved += Bar_PointerMoved;
        bar.PointerReleased += Bar_PointerReleased;
        bar.PointerCanceled += Bar_PointerCanceled;
        bar.PointerCaptureLost += Bar_PointerCanceled;
        track.Bars.Children.Add(bar);
    }

    private static CursorGrid Handle(DragMode mode, Color textColor)
    {
        var handle = new CursorGrid
        {
            Cursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast),
            Width = HandleWidth,
            HorizontalAlignment = mode == DragMode.ResizeStart ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            Background = new SolidColorBrush(Colors.Transparent),
            Opacity = 0.35,
            Tag = mode
        };
        var grip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 1,
            Margin = new Thickness(4, 5, 0, 5),
            Opacity = 0.5,
            IsHitTestVisible = false
        };
        grip.Children.Add(new Rectangle { Width = 2, RadiusX = 1, RadiusY = 1, Fill = new SolidColorBrush(textColor) });
        grip.Children.Add(new Rectangle { Width = 2, RadiusX = 1, RadiusY = 1, Fill = new SolidColorBrush(textColor) });
        handle.Children.Add(grip);
        return handle;
    }

    private static void PlaceInTrack(FrameworkElement element, int startDay, int endDay, int lane)
    {
        Grid.SetColumn(element, startDay - 1);
        Grid.SetColumnSpan(element, endDay - startDay + 1);
        Grid.SetRow(element, lane);
    }

    // ---- hover --------------------------------------------------------------------------------

    private void Bar_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (_drag is not null || sender is not Grid bar) return;
        bar.Translation = new Vector3(0, -1, 0);
        foreach (var handle in bar.Children.OfType<CursorGrid>())
        {
            handle.Opacity = 0.9;
            handle.Background = new SolidColorBrush(Color.FromArgb(0x47, 0xFF, 0xFF, 0xFF));
        }
    }

    private void Bar_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Grid bar) return;
        bar.Translation = Vector3.Zero;
        foreach (var handle in bar.Children.OfType<CursorGrid>())
        {
            handle.Opacity = 0.35;
            handle.Background = new SolidColorBrush(Colors.Transparent);
        }
    }

    // ---- dragging -----------------------------------------------------------------------------

    private void Bar_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_drag is not null || sender is not CursorGrid bar || bar.Tag is not BarInfo info) return;
        if (!e.GetCurrentPoint(bar).Properties.IsLeftButtonPressed) return;

        var mode = e.OriginalSource is FrameworkElement { Tag: DragMode handleMode } ? handleMode : DragMode.Move;
        var pointerDate = DateAtX(info.Track, e.GetCurrentPoint(info.Track.Host).Position.X);
        BarPressed?.Invoke(this, info.Item);

        _drag = new DragState
        {
            Bar = bar,
            Info = info,
            Mode = mode,
            PointerDate = pointerDate,
            CurrentDate = pointerDate,
            OriginalStart = info.Item.StartDate,
            OriginalEnd = info.Item.EndDate
        };
        CreateFeedback(_drag, e);
        bar.CapturePointer(e.Pointer);
        RenderFeedback(_drag, e);
        e.Handled = true;
    }

    private void Bar_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_drag is null || !ReferenceEquals(_drag.Bar, sender)) return;
        UpdateDrag(_drag, e);
        e.Handled = true;
    }

    private void Bar_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var drag = _drag;
        if (drag is null || !ReferenceEquals(drag.Bar, sender)) return;
        UpdateDrag(drag, e);
        _drag = null;
        CleanupFeedback(drag);
        drag.Bar.ReleasePointerCapture(e.Pointer);
        e.Handled = true;

        var proposal = Propose(drag);
        DragCompleted?.Invoke(this, new TimelineDragCompletedEventArgs(drag.Info.Item, drag.Mode, drag.Moved, proposal.Start, proposal.End));
    }

    private void Bar_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        var drag = _drag;
        if (drag is null || !ReferenceEquals(drag.Bar, sender)) return;
        _drag = null;
        CleanupFeedback(drag);
        DragCanceled?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateDrag(DragState drag, PointerRoutedEventArgs e)
    {
        drag.CurrentDate = DateAtX(drag.Info.Track, e.GetCurrentPoint(drag.Info.Track.Host).Position.X);
        var delta = ScheduleRules.DiffDays(drag.PointerDate, drag.CurrentDate);
        drag.Moved = drag.Moved || delta != 0;
        RenderFeedback(drag, e);
        if (!drag.Moved) return;
        var action = drag.Mode switch
        {
            DragMode.Move => "移动",
            DragMode.ResizeEnd => "调整结束",
            _ => "调整开始"
        };
        DragProgress?.Invoke(this, $"{action}：{(delta > 0 ? "+" : "")}{delta} 天");
    }

    private string DateAtX(Track track, double x) =>
        TimelineLayout.DateAt(x, track.Host.ActualWidth, _schedule!.Year, track.Month);

    private static DragProposal Propose(DragState drag) =>
        TimelineLayout.Propose(drag.Mode, drag.PointerDate, drag.CurrentDate, drag.OriginalStart, drag.OriginalEnd);

    private (int StartDay, int EndDay)? VisibleRange(string startValue, string endValue, int month) =>
        TimelineLayout.VisibleRange(_schedule!.Year, startValue, endValue, month);

    private void CreateFeedback(DragState drag, PointerRoutedEventArgs e)
    {
        var bar = drag.Bar;
        var info = drag.Info;
        var accent = ThemeColors.SystemColor("SystemAccentColor");
        var resizing = drag.Mode != DragMode.Move;

        // Highlight the month being edited.
        drag.SavedTrackBackground = info.Track.Host.Background;
        info.Track.Host.Background = new SolidColorBrush(Blend(ThemeColors.Get(this, "TimelineTrackBackground"), accent, 0.08));
        drag.TrackOutline = new Border
        {
            BorderBrush = new SolidColorBrush(ColorUtil.WithAlpha(accent, 0.3)),
            BorderThickness = new Thickness(2),
            IsHitTestVisible = false
        };
        info.Track.Host.Children.Add(drag.TrackOutline);
        bar.Opacity = 0.34;
        bar.Translation = Vector3.Zero;

        drag.PreviewText = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.Black,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = resizing
                ? ThemeColors.Brush(this, "ResizePreviewText")
                : ThemeColors.System("AccentTextFillColorPrimaryBrush")
        };
        drag.Preview = new Border
        {
            Margin = new Thickness(1, -2, 1, -2),
            Padding = new Thickness(8, 0, 8, 0),
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(2),
            Background = resizing ? ThemeColors.Brush(this, "ResizePreviewFill") : new SolidColorBrush(ColorUtil.WithAlpha(accent, 0.18)),
            BorderBrush = resizing ? ThemeColors.Brush(this, "ResizePreviewStroke") : new SolidColorBrush(ColorUtil.WithAlpha(accent, 0.82)),
            IsHitTestVisible = false,
            Child = drag.PreviewText
        };
        Canvas.SetZIndex(drag.Preview, 10);
        Grid.SetRow(drag.Preview, info.Lane);
        info.Track.Bars.Children.Add(drag.Preview);

        if (Overlay is null) return;
        var position = e.GetCurrentPoint(bar).Position;
        drag.OffsetX = Math.Clamp(position.X, 0, bar.ActualWidth);
        drag.OffsetY = Math.Clamp(position.Y, 0, bar.ActualHeight);

        drag.Ghost = new Border
        {
            Width = bar.ActualWidth,
            Height = bar.ActualHeight,
            MinWidth = 72,
            MaxWidth = 460,
            Padding = new Thickness(7, 0, 7, 0),
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(ColorUtil.Parse(info.Color)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            Opacity = 0.96,
            Child = new TextBlock
            {
                Text = info.Label,
                FontSize = 12,
                FontWeight = FontWeights.Black,
                Foreground = new SolidColorBrush(info.TextColor),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        drag.TooltipText = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            LineHeight = 17,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeColors.Brush(this, "DragTooltipForeground")
        };
        drag.Tooltip = new Border
        {
            MaxWidth = 260,
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(7),
            Background = ThemeColors.Brush(this, "DragTooltipBackground"),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x29, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Child = drag.TooltipText
        };
        Overlay.Children.Add(drag.Ghost);
        Overlay.Children.Add(drag.Tooltip);
    }

    private void RenderFeedback(DragState drag, PointerRoutedEventArgs e)
    {
        var proposal = Propose(drag);
        var visible = VisibleRange(proposal.Start, proposal.End, drag.Info.Track.Month);
        if (drag.Preview is not null && drag.PreviewText is not null)
        {
            if (visible is { } range)
            {
                drag.Preview.Visibility = Visibility.Visible;
                Grid.SetColumn(drag.Preview, range.StartDay - 1);
                Grid.SetColumnSpan(drag.Preview, range.EndDay - range.StartDay + 1);
                drag.PreviewText.Text = $"{proposal.Start} 至 {proposal.End} · {proposal.Duration}天";
            }
            else
            {
                drag.Preview.Visibility = Visibility.Collapsed;
            }
        }

        if (Overlay is null || drag.Tooltip is null || drag.TooltipText is null || drag.Ghost is null) return;
        var action = drag.Mode switch
        {
            DragMode.Move => "移动事件",
            DragMode.ResizeEnd => "调整结束日期",
            _ => "调整开始日期"
        };
        var cascadeText = _cascade && proposal.EndDelta != 0
            ? $"\n后续安排 {(proposal.EndDelta > 0 ? "+" : "")}{proposal.EndDelta} 天"
            : "";
        drag.TooltipText.Text = $"{action}\n{proposal.Start} 至 {proposal.End} · {proposal.Duration}天{cascadeText}";

        var point = e.GetCurrentPoint(Overlay).Position;
        drag.Tooltip.Measure(new Size(260, double.PositiveInfinity));
        var tooltipX = Math.Min(Overlay.ActualWidth - 280, Math.Max(10, point.X + 14));
        var tooltipY = Math.Max(12, point.Y - 12) - drag.Tooltip.DesiredSize.Height;
        Canvas.SetLeft(drag.Tooltip, tooltipX);
        Canvas.SetTop(drag.Tooltip, tooltipY);
        Canvas.SetLeft(drag.Ghost, point.X - drag.OffsetX);
        Canvas.SetTop(drag.Ghost, point.Y - drag.OffsetY - 12);
    }

    private void CleanupFeedback(DragState drag)
    {
        var host = drag.Info.Track.Host;
        host.Background = drag.SavedTrackBackground;
        if (drag.TrackOutline is not null) host.Children.Remove(drag.TrackOutline);
        drag.Bar.Opacity = 1;
        if (drag.Preview is not null) drag.Info.Track.Bars.Children.Remove(drag.Preview);
        if (Overlay is not null)
        {
            if (drag.Ghost is not null) Overlay.Children.Remove(drag.Ghost);
            if (drag.Tooltip is not null) Overlay.Children.Remove(drag.Tooltip);
        }
    }

    private static Color Blend(Color baseColor, Color tint, double amount) => Color.FromArgb(
        255,
        (byte)Math.Round(baseColor.R + (tint.R - baseColor.R) * amount),
        (byte)Math.Round(baseColor.G + (tint.G - baseColor.G) * amount),
        (byte)Math.Round(baseColor.B + (tint.B - baseColor.B) * amount));

    private sealed record Track(int Month, Grid Host, Grid Bars);

    private sealed record BarInfo(ScheduleItem Item, Track Track, int Lane, string Label, string Color, Color TextColor);

    private sealed class DragState
    {
        public required CursorGrid Bar { get; init; }
        public required BarInfo Info { get; init; }
        public required DragMode Mode { get; init; }
        public required string PointerDate { get; init; }
        public required string CurrentDate { get; set; }
        public required string OriginalStart { get; init; }
        public required string OriginalEnd { get; init; }
        public bool Moved { get; set; }
        public Brush? SavedTrackBackground { get; set; }
        public Border? TrackOutline { get; set; }
        public Border? Preview { get; set; }
        public TextBlock? PreviewText { get; set; }
        public Border? Ghost { get; set; }
        public Border? Tooltip { get; set; }
        public TextBlock? TooltipText { get; set; }
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
    }
}
