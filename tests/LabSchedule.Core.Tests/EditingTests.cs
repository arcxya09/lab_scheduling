using LabSchedule.Core;

namespace LabSchedule.Core.Tests;

/// <summary>Calendar drag maths and editing operations (renderer.js behaviour).</summary>
public class EditingTests
{
    private static Schedule Sample()
    {
        var schedule = ScheduleRules.NormalizeState(ScheduleRules.PrepareSchedule(SampleSchedule.CreateRaw()));
        ScheduleRules.RenumberInPlace(schedule.Items);
        return schedule;
    }

    private static ScheduleItem ById(Schedule schedule, string id) => schedule.Items.Single(item => item.Id == id);

    [Fact]
    public void MovingABarShiftsItAndCascadesToLaterItems()
    {
        var schedule = Sample();
        var item = ById(schedule, "EXP002");
        var before = ById(schedule, "EXP001");
        var after = ById(schedule, "EXP003");

        var proposal = TimelineLayout.Propose(DragMode.Move, "2026-06-05", "2026-06-08", item.StartDate, item.EndDate);
        Assert.Equal(new DragProposal("2026-06-06", "2026-06-13", 8, 3), proposal);

        ScheduleEditing.ApplyDateChange(schedule, item, proposal.Start, proposal.End, cascade: true);
        Assert.Equal(("2026-06-06", "2026-06-13"), (item.StartDate, item.EndDate));
        Assert.Equal(("2026-06-14", "2026-06-18"), (after.StartDate, after.EndDate));
        Assert.Equal(("2026-06-01", "2026-06-02"), (before.StartDate, before.EndDate));
    }

    [Fact]
    public void WithoutCascadeOnlyTheDraggedItemMoves()
    {
        var schedule = Sample();
        var item = ById(schedule, "EXP002");
        var after = ById(schedule, "EXP003");
        ScheduleEditing.ApplyDateChange(schedule, item, "2026-06-06", "2026-06-13", cascade: false);
        Assert.Equal(("2026-06-11", "2026-06-15"), (after.StartDate, after.EndDate));
    }

    [Fact]
    public void ResizingClampsAtTheOtherEnd()
    {
        Assert.Equal(new DragProposal("2026-06-03", "2026-06-12", 10, 2),
            TimelineLayout.Propose(DragMode.ResizeEnd, "2026-06-10", "2026-06-12", "2026-06-03", "2026-06-10"));
        Assert.Equal(new DragProposal("2026-06-03", "2026-06-03", 1, -7),
            TimelineLayout.Propose(DragMode.ResizeEnd, "2026-06-10", "2026-06-01", "2026-06-03", "2026-06-10"));
        Assert.Equal(new DragProposal("2026-06-10", "2026-06-10", 1, 0),
            TimelineLayout.Propose(DragMode.ResizeStart, "2026-06-03", "2026-06-20", "2026-06-03", "2026-06-10"));
        Assert.Equal(new DragProposal("2026-06-01", "2026-06-10", 10, 0),
            TimelineLayout.Propose(DragMode.ResizeStart, "2026-06-03", "2026-06-01", "2026-06-03", "2026-06-10"));
    }

    [Fact]
    public void PointerPositionMapsToADayInTheMonth()
    {
        Assert.Equal("2026-06-01", TimelineLayout.DateAt(0, 310, 2026, 6));
        Assert.Equal("2026-06-10", TimelineLayout.DateAt(95, 310, 2026, 6));
        Assert.Equal("2026-06-30", TimelineLayout.DateAt(305, 310, 2026, 6));
        Assert.Equal("2026-02-28", TimelineLayout.DateAt(305, 310, 2026, 2));
        Assert.Equal("2026-02-01", TimelineLayout.DateAt(-40, 310, 2026, 2));
    }

    [Fact]
    public void VisibleRangeIsLimitedToTheMonth()
    {
        Assert.Equal((16, 30), TimelineLayout.VisibleRange(2026, "2026-06-16", "2026-07-31", 6));
        Assert.Equal((1, 31), TimelineLayout.VisibleRange(2026, "2026-06-16", "2026-07-31", 7));
        Assert.Null(TimelineLayout.VisibleRange(2026, "2026-06-16", "2026-07-31", 8));
        Assert.Null(TimelineLayout.VisibleRange(2026, "", "2026-07-31", 7));
    }

    [Fact]
    public void SegmentsSplitByMonthAndOverlapsGetTheirOwnLane()
    {
        var schedule = Sample();
        var segments = TimelineLayout.BuildSegments(schedule);

        var june = segments[6].Single(segment => segment.Item.Id == "MAINT001");
        Assert.Equal((16, 30, true, false), (june.StartDay, june.EndDay, june.IsStart, june.IsEnd));
        var july = segments[7].Single(segment => segment.Item.Id == "MAINT001");
        Assert.Equal((1, 31, false, true), (july.StartDay, july.EndDay, july.IsStart, july.IsEnd));
        Assert.All(segments[6], segment => Assert.Equal(0, segment.Lane));

        schedule.Items.Add(new ScheduleItem { Id = "EXP099", Type = ItemTypes.Experiment, Name = "重叠", StartDate = "2026-06-05", EndDate = "2026-06-12" });
        var lanes = TimelineLayout.BuildSegments(schedule)[6].ToDictionary(segment => segment.Item.Id, segment => segment.Lane);
        Assert.Equal(0, lanes["EXP002"]);
        Assert.Equal(1, lanes["EXP099"]);
        Assert.Equal(0, lanes["EXP003"]);
    }

    [Fact]
    public void InsertingAfterTheSelectionMakesRoom()
    {
        var schedule = Sample();
        var selected = ById(schedule, "EXP001");
        var next = ById(schedule, "EXP002");
        var item = ScheduleEditing.InsertItem(schedule, ItemTypes.Experiment, selected, cascade: true);

        Assert.Equal(("新实验安排", "2026-06-03", "2026-06-09"), (item.Name, item.StartDate, item.EndDate));
        Assert.Equal("EXP002", item.Id);
        Assert.Equal(("2026-06-10", "2026-06-17"), (next.StartDate, next.EndDate));
        Assert.Equal(ScheduleRules.DefaultColors[13 % 10], item.Color);
        Assert.Equal(14, schedule.Items.Count);
    }

    [Fact]
    public void InsertingWithoutSelectionAppendsOrStartsTheYear()
    {
        var schedule = Sample();
        var item = ScheduleEditing.InsertItem(schedule, ItemTypes.Maintenance, null, cascade: true);
        Assert.Equal(("新维护安排", "2026-11-01", "2026-11-07", ScheduleRules.MaintenanceColor), (item.Name, item.StartDate, item.EndDate, item.Color));
        Assert.Equal("MAINT002", item.Id);

        var empty = new Schedule { Year = 2027, Title = "空" };
        var first = ScheduleEditing.InsertItem(empty, ItemTypes.Experiment, null, cascade: true);
        Assert.Equal(("2027-01-01", "2027-01-07", "EXP001"), (first.StartDate, first.EndDate, first.Id));
    }

    [Fact]
    public void ColourlessExperimentsUseThePaletteByPosition()
    {
        var schedule = Sample();
        var third = schedule.Items.Where(item => !item.IsMaintenance).ElementAt(2);
        third.Color = null;
        Assert.Equal(ScheduleRules.DefaultColors[2], ScheduleEditing.ColorForItem(schedule, third));
        Assert.Equal(ScheduleRules.MaintenanceColor, ScheduleEditing.ColorForItem(schedule, ById(schedule, "MAINT001")));
    }
}
