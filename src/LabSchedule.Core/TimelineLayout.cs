namespace LabSchedule.Core;

public enum DragMode
{
    Move,
    ResizeStart,
    ResizeEnd
}

/// <summary>One month's slice of an item on the editor calendar.</summary>
public sealed record TimelineSegment(ScheduleItem Item, int Month, int StartDay, int EndDay, int Lane, bool IsStart, bool IsEnd);

public readonly record struct DragProposal(string Start, string End, int Duration, int EndDelta);

/// <summary>
/// Layout and drag maths for the editor calendar (port of renderer.js buildSegments, assignLanes,
/// proposalForDrag, visibleRangeForMonth and pointerDateInTrack).
/// </summary>
public static class TimelineLayout
{
    public const int Columns = 31;

    /// <summary>Segments per month with lanes assigned so overlapping items never share a lane.</summary>
    public static Dictionary<int, List<TimelineSegment>> BuildSegments(Schedule schedule)
    {
        var byMonth = new Dictionary<int, List<TimelineSegment>>();
        var year = schedule.Year;
        foreach (var item in schedule.Items)
        {
            var start = ScheduleRules.ParseDate(item.StartDate);
            var end = ScheduleRules.ParseDate(item.EndDate);
            if (start is null || end is null || start > end) continue;
            for (var month = 1; month <= 12; month++)
            {
                var monthStart = new DateOnly(year, month, 1);
                var monthEnd = new DateOnly(year, month, ScheduleRules.DaysInMonth(year, month));
                if (end < monthStart || start > monthEnd) continue;
                if (!byMonth.TryGetValue(month, out var list)) byMonth[month] = list = [];
                list.Add(new TimelineSegment(
                    item,
                    month,
                    start > monthStart ? start.Value.Day : 1,
                    end < monthEnd ? end.Value.Day : monthEnd.Day,
                    0,
                    item.StartDate == ScheduleRules.DateToIso(monthStart > start ? monthStart : start.Value),
                    item.EndDate == ScheduleRules.DateToIso(monthEnd < end ? monthEnd : end.Value)));
            }
        }
        return byMonth.ToDictionary(entry => entry.Key, entry => AssignLanes(entry.Value));
    }

    public static List<TimelineSegment> AssignLanes(IEnumerable<TimelineSegment> segments)
    {
        var lanes = new List<List<TimelineSegment>>();
        var placedSegments = new List<TimelineSegment>();
        var ordered = segments
            .OrderBy(segment => segment.StartDay)
            .ThenBy(segment => segment.EndDay)
            .ThenBy(segment => segment.Item.Id, StringComparer.Ordinal);
        foreach (var segment in ordered)
        {
            var laneIndex = lanes.FindIndex(lane => lane.All(other => segment.EndDay < other.StartDay || segment.StartDay > other.EndDay));
            if (laneIndex < 0)
            {
                laneIndex = lanes.Count;
                lanes.Add([]);
            }
            var placed = segment with { Lane = laneIndex };
            lanes[laneIndex].Add(placed);
            placedSegments.Add(placed);
        }
        return placedSegments;
    }

    /// <summary>The date under a horizontal offset in a month row, clamped to that month's days.</summary>
    public static string DateAt(double x, double trackWidth, int year, int month)
    {
        var width = Math.Max(1, trackWidth);
        var rawDay = (int)Math.Floor(x / (width / Columns)) + 1;
        var day = Math.Clamp(rawDay, 1, ScheduleRules.DaysInMonth(year, month));
        return ScheduleRules.DateToIso(new DateOnly(year, month, day));
    }

    /// <summary>Where an item would land if the drag ended with the pointer on <paramref name="currentDate"/>.</summary>
    public static DragProposal Propose(DragMode mode, string pointerDate, string currentDate, string originalStart, string originalEnd)
    {
        var delta = ScheduleRules.DiffDays(pointerDate, currentDate);
        var start = originalStart;
        var end = originalEnd;
        switch (mode)
        {
            case DragMode.Move:
                start = ScheduleRules.AddDaysIso(originalStart, delta);
                end = ScheduleRules.AddDaysIso(originalEnd, delta);
                break;
            case DragMode.ResizeStart:
                start = string.CompareOrdinal(currentDate, originalEnd) > 0 ? originalEnd : currentDate;
                break;
            case DragMode.ResizeEnd:
                end = string.CompareOrdinal(currentDate, originalStart) < 0 ? originalStart : currentDate;
                break;
        }
        return new DragProposal(start, end, ScheduleRules.DiffDays(start, end) + 1, ScheduleRules.DiffDays(originalEnd, end));
    }

    /// <summary>The days of <paramref name="month"/> covered by the range, or null when it misses the month.</summary>
    public static (int StartDay, int EndDay)? VisibleRange(int year, string startValue, string endValue, int month)
    {
        var start = ScheduleRules.ParseDate(startValue);
        var end = ScheduleRules.ParseDate(endValue);
        if (start is null || end is null) return null;
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = new DateOnly(year, month, ScheduleRules.DaysInMonth(year, month));
        if (end < monthStart || start > monthEnd) return null;
        return ((start > monthStart ? start.Value : monthStart).Day, (end < monthEnd ? end.Value : monthEnd).Day);
    }
}
