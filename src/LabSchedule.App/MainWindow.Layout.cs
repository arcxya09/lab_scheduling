using LabSchedule.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LabSchedule;

/// <summary>The two splitters: editor panel width, and how the calendar and the list share height.</summary>
public sealed partial class MainWindow
{
    private double _editorWidth = PaneLayout.DefaultEditorWidth;
    private double _listRatio = PaneLayout.DefaultListRatio;
    private double _dragStartEditorWidth;
    private double _dragStartListHeight;
    private double _dragSharedHeight;

    private void InitializeLayout()
    {
        _editorWidth = _settings.EditorPanelWidth ?? PaneLayout.DefaultEditorWidth;
        _listRatio = PaneLayout.NormalizeListRatio(_settings.ListPaneRatio);
        ApplyListRatio();
        Workbench.SizeChanged += (_, _) => ApplyEditorWidth();
        ActionsRow.SizeChanged += (_, _) => ArrangeActionsRow();

        EditorSplitter.DragStarted += (_, _) => _dragStartEditorWidth = EditorColumn.ActualWidth;
        EditorSplitter.Dragging += (_, delta) =>
        {
            // Dragging the divider to the right makes the editor on the right narrower.
            _editorWidth = PaneLayout.ClampEditorWidth(_dragStartEditorWidth - delta, Workbench.ActualWidth);
            ApplyEditorWidth();
        };
        EditorSplitter.DragCompleted += (_, _) => SaveLayout();
        EditorSplitter.ResetRequested += (_, _) =>
        {
            _editorWidth = PaneLayout.DefaultEditorWidth;
            ApplyEditorWidth();
            SaveLayout();
        };

        ListSplitter.DragStarted += (_, _) =>
        {
            _dragStartListHeight = ListRow.ActualHeight;
            _dragSharedHeight = TimelineRow.ActualHeight + ListRow.ActualHeight;
        };
        ListSplitter.Dragging += (_, delta) =>
        {
            if (_dragSharedHeight <= 0) return;
            var height = PaneLayout.ClampListHeight(_dragStartListHeight - delta, _dragSharedHeight);
            _listRatio = height / _dragSharedHeight;
            ApplyListRatio();
        };
        ListSplitter.DragCompleted += (_, _) => SaveLayout();
        ListSplitter.ResetRequested += (_, _) =>
        {
            _listRatio = PaneLayout.DefaultListRatio;
            ApplyListRatio();
            SaveLayout();
        };
    }

    /// <summary>Keeps the item buttons beside the check boxes, or below them when there is no room.</summary>
    private void ArrangeActionsRow()
    {
        var unlimited = new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity);
        OptionsPanel.Measure(unlimited);
        ItemButtonsPanel.Measure(unlimited);
        var wrap = OptionsPanel.DesiredSize.Width + ActionsRow.ColumnSpacing + ItemButtonsPanel.DesiredSize.Width > ActionsRow.ActualWidth;
        if (Grid.GetRow(ItemButtonsPanel) == (wrap ? 1 : 0)) return;
        Grid.SetRow(ItemButtonsPanel, wrap ? 1 : 0);
        Grid.SetColumn(ItemButtonsPanel, wrap ? 0 : 1);
        ItemButtonsPanel.HorizontalAlignment = wrap ? HorizontalAlignment.Left : HorizontalAlignment.Right;
    }

    /// <summary>Applies the chosen width, narrowed when the window is too small for it.</summary>
    private void ApplyEditorWidth()
    {
        var width = Workbench.ActualWidth > 0 ? PaneLayout.ClampEditorWidth(_editorWidth, Workbench.ActualWidth) : _editorWidth;
        EditorColumn.Width = new GridLength(width);
    }

    /// <summary>Both rows stay star-sized, so they always fit the card whatever the window height.</summary>
    private void ApplyListRatio()
    {
        TimelineRow.Height = new GridLength(1 - _listRatio, GridUnitType.Star);
        ListRow.Height = new GridLength(_listRatio, GridUnitType.Star);
    }

    private void SaveLayout()
    {
        _settings.EditorPanelWidth = Math.Round(_editorWidth, 1);
        _settings.ListPaneRatio = Math.Round(_listRatio, 4);
        SaveSettings();
    }
}
