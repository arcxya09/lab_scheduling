using LabSchedule.Timeline;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;

namespace LabSchedule.Controls;

/// <summary>
/// A thin draggable divider between two panes. It only reports movement; the owner decides how the
/// neighbouring rows or columns change. Arrow keys move it in 16-pixel steps; double-click resets.
/// </summary>
public sealed partial class SplitterBar : CursorGrid
{
    private const double KeyStep = 16;

    private readonly Rectangle _line = new() { IsHitTestVisible = false };
    private Point? _start;

    public SplitterBar()
    {
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        Children.Add(_line);
        Loaded += (_, _) => ApplyOrientation();
        PointerEntered += (_, _) => Highlight(true);
        PointerExited += (_, _) => Highlight(_start is not null);
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += (_, _) => EndDrag();
        DoubleTapped += (_, _) => ResetRequested?.Invoke(this, EventArgs.Empty);
        KeyDown += OnKeyDown;
        ActualThemeChanged += (_, _) => Highlight(false);
    }

    /// <summary>Vertical splits columns (dragged left/right); horizontal splits rows.</summary>
    public Orientation Orientation { get; set; } = Orientation.Vertical;

    public event EventHandler? DragStarted;

    /// <summary>Distance moved since the drag started, along the splitter's axis.</summary>
    public event EventHandler<double>? Dragging;

    public event EventHandler? DragCompleted;

    public event EventHandler? ResetRequested;

    private bool IsVertical => Orientation == Orientation.Vertical;

    private void ApplyOrientation()
    {
        Cursor = InputSystemCursor.Create(IsVertical ? InputSystemCursorShape.SizeWestEast : InputSystemCursorShape.SizeNorthSouth);
        AutomationProperties.SetName(this, IsVertical ? "调整编辑栏宽度" : "调整列表高度");
        _line.Width = IsVertical ? 1 : double.NaN;
        _line.Height = IsVertical ? double.NaN : 1;
        _line.HorizontalAlignment = IsVertical ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        _line.VerticalAlignment = IsVertical ? VerticalAlignment.Stretch : VerticalAlignment.Center;
        Highlight(false);
    }

    private void Highlight(bool active)
    {
        _line.Fill = ThemeColors.System(this, active ? "AccentFillColorDefaultBrush" : "DividerStrokeColorDefaultBrush");
        _line.Width = IsVertical ? (active ? 3 : 1) : double.NaN;
        _line.Height = IsVertical ? double.NaN : (active ? 3 : 1);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _start = e.GetCurrentPoint(null).Position;
        CapturePointer(e.Pointer);
        Highlight(true);
        DragStarted?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_start is not { } start) return;
        var position = e.GetCurrentPoint(null).Position;
        Dragging?.Invoke(this, IsVertical ? position.X - start.X : position.Y - start.Y);
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_start is null) return;
        ReleasePointerCapture(e.Pointer);
        EndDrag();
        e.Handled = true;
    }

    private void EndDrag()
    {
        if (_start is null) return;
        _start = null;
        Highlight(false);
        DragCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        double delta = (e.Key, IsVertical) switch
        {
            (VirtualKey.Left, true) or (VirtualKey.Up, false) => -KeyStep,
            (VirtualKey.Right, true) or (VirtualKey.Down, false) => KeyStep,
            _ => 0
        };
        if (delta == 0) return;
        MoveBy(delta);
        e.Handled = true;
    }

    /// <summary>Moves the splitter by a distance in one step (keyboard and UI Automation).</summary>
    private void MoveBy(double delta)
    {
        DragStarted?.Invoke(this, EventArgs.Empty);
        Dragging?.Invoke(this, delta);
        DragCompleted?.Invoke(this, EventArgs.Empty);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SplitterBarAutomationPeer(this);

    /// <summary>Exposes the splitter to screen readers and automation as a movable thumb.</summary>
    private sealed partial class SplitterBarAutomationPeer(SplitterBar owner) : FrameworkElementAutomationPeer(owner), ITransformProvider
    {
        public bool CanMove => true;
        public bool CanResize => false;
        public bool CanRotate => false;

        protected override object? GetPatternCore(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Transform ? this : base.GetPatternCore(patternInterface);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Thumb;

        protected override string GetClassNameCore() => nameof(SplitterBar);

        /// <summary>UI Automation passes the new screen position in physical pixels.</summary>
        public void Move(double x, double y)
        {
            var bounds = GetBoundingRectangle();
            var scale = owner.XamlRoot?.RasterizationScale ?? 1;
            owner.MoveBy((owner.IsVertical ? x - bounds.X : y - bounds.Y) / scale);
        }

        public void Resize(double width, double height)
        {
        }

        public void Rotate(double degrees)
        {
        }
    }
}
