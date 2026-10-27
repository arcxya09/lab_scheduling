namespace LabSchedule.Core;

/// <summary>
/// Session undo/redo stack (port of history.js). Snapshots are serialized strings, so they are
/// immutable and compare by value.
/// </summary>
public sealed class ScheduleHistory(int maxEntries = 100)
{
    private readonly record struct Entry(string Snapshot, string Label);

    private readonly List<Entry> _undoStack = [];
    private readonly List<Entry> _redoStack = [];
    private string? _current;

    public bool CanUndo => _undoStack.Count > 0;

    public bool CanRedo => _redoStack.Count > 0;

    public (int Undo, int Redo) Size => (_undoStack.Count, _redoStack.Count);

    public void Reset(string snapshot)
    {
        _current = snapshot;
        _undoStack.Clear();
        _redoStack.Clear();
    }

    public bool Commit(string snapshot, string label = "")
    {
        if (_current is null)
        {
            Reset(snapshot);
            return false;
        }
        if (_current == snapshot) return false;

        Push(_undoStack, new Entry(_current, label));
        _redoStack.Clear();
        _current = snapshot;
        return true;
    }

    public bool CommitFrom(string baseSnapshot, string nextSnapshot, string label = "")
    {
        if (baseSnapshot == nextSnapshot)
        {
            _current = nextSnapshot;
            return false;
        }

        Push(_undoStack, new Entry(baseSnapshot, label));
        _redoStack.Clear();
        _current = nextSnapshot;
        return true;
    }

    public string? Undo()
    {
        if (_undoStack.Count == 0 || _current is null) return null;
        var entry = Pop(_undoStack);
        _redoStack.Add(new Entry(_current, entry.Label));
        _current = entry.Snapshot;
        return _current;
    }

    public string? Redo()
    {
        if (_redoStack.Count == 0 || _current is null) return null;
        var entry = Pop(_redoStack);
        _undoStack.Add(new Entry(_current, entry.Label));
        _current = entry.Snapshot;
        return _current;
    }

    private void Push(List<Entry> stack, Entry entry)
    {
        stack.Add(entry);
        if (stack.Count > maxEntries) stack.RemoveAt(0);
    }

    private static Entry Pop(List<Entry> stack)
    {
        var entry = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return entry;
    }
}
