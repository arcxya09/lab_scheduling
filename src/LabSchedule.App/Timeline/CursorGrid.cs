using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace LabSchedule.Timeline;

/// <summary>A Grid whose mouse cursor can be set from outside (ProtectedCursor is protected).</summary>
public partial class CursorGrid : Grid
{
    public InputCursor? Cursor
    {
        get => ProtectedCursor;
        set => ProtectedCursor = value;
    }
}
