namespace LabSchedule.Core;

/// <summary>Size limits for the two draggable splitters of the main window (all values in DIPs).</summary>
public static class PaneLayout
{
    public const double DefaultEditorWidth = 390;
    public const double MinEditorWidth = 300;
    public const double MinLeftWidth = 420;
    public const double SplitterSize = 8;

    /// <summary>Share of the calendar + list height that the list gets by default.</summary>
    public const double DefaultListRatio = 1.0 / 3;
    public const double MinListHeight = 96;
    public const double MinTimelineHeight = 160;

    /// <summary>Editor width that leaves the left card at least <see cref="MinLeftWidth"/>.</summary>
    public static double ClampEditorWidth(double desired, double workbenchWidth)
    {
        var max = Math.Max(MinEditorWidth, workbenchWidth - MinLeftWidth - SplitterSize);
        return Math.Clamp(double.IsFinite(desired) ? desired : DefaultEditorWidth, MinEditorWidth, max);
    }

    /// <summary>List height for a drag, keeping both the calendar and the list above their minimums.</summary>
    public static double ClampListHeight(double desired, double sharedHeight)
    {
        var max = Math.Max(MinListHeight, sharedHeight - MinTimelineHeight);
        return Math.Clamp(desired, MinListHeight, max);
    }

    /// <summary>A stored list ratio, or the default when it is missing or out of range.</summary>
    public static double NormalizeListRatio(double? ratio) =>
        ratio is { } value && double.IsFinite(value) && value > 0.05 && value < 0.95 ? value : DefaultListRatio;
}
