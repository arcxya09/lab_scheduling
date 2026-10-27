using LabSchedule.Core;

namespace LabSchedule.Core.Tests;

public class HistoryTests
{
    [Fact]
    public void UndoRedoFollowsHistoryJs()
    {
        var history = new ScheduleHistory(maxEntries: 2);
        history.Reset("1");
        Assert.False(history.CanUndo);
        history.Commit("2", "first");
        history.Commit("3", "second");
        history.Commit("4", "third");
        Assert.Equal((2, 0), history.Size);
        Assert.Equal("3", history.Undo());
        Assert.Equal("2", history.Undo());
        Assert.Null(history.Undo());
        Assert.Equal("3", history.Redo());
        history.Commit("5", "new");
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void CommitFromIgnoresUnchangedSessions()
    {
        var history = new ScheduleHistory();
        history.Reset("a");
        Assert.False(history.CommitFrom("a", "a"));
        Assert.False(history.CanUndo);
        Assert.True(history.CommitFrom("a", "b"));
        Assert.Equal("a", history.Undo());
        Assert.Equal("b", history.Redo());
    }
}
