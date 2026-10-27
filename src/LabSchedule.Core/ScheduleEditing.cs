namespace LabSchedule.Core;

/// <summary>Editing operations shared by the form, the calendar drag and "insert" (renderer.js).</summary>
public static class ScheduleEditing
{
    /// <summary>Items that come after <paramref name="item"/> in date order.</summary>
    public static List<ScheduleItem> FollowingItems(IEnumerable<ScheduleItem> items, ScheduleItem item)
    {
        var ordered = ScheduleRules.SortItemsByDate(items);
        var index = ordered.IndexOf(item);
        return index < 0 ? [] : ordered.Skip(index + 1).ToList();
    }

    public static void ShiftItems(IEnumerable<ScheduleItem> items, int deltaDays)
    {
        if (deltaDays == 0) return;
        foreach (var item in items)
        {
            item.StartDate = ScheduleRules.AddDaysIso(item.StartDate, deltaDays);
            item.EndDate = ScheduleRules.AddDaysIso(item.EndDate, deltaDays);
        }
    }

    /// <summary>
    /// Sets new dates on an item; with <paramref name="cascade"/> ("联动后续安排") every later item
    /// moves by the same number of days the end date moved. Re-sorts and renumbers in place.
    /// </summary>
    public static void ApplyDateChange(Schedule schedule, ScheduleItem item, string newStart, string newEnd, bool cascade)
    {
        var following = FollowingItems(schedule.Items, item);
        var endDelta = ScheduleRules.DiffDays(item.EndDate, newEnd);
        item.StartDate = newStart;
        item.EndDate = newEnd;
        if (cascade && endDelta != 0) ShiftItems(following, endDelta);
        ScheduleRules.RenumberInPlace(schedule.Items);
    }

    /// <summary>The colour the editor shows for an item (renderer.js colorForItem).</summary>
    public static string ColorForItem(Schedule schedule, ScheduleItem item)
    {
        if (item.IsMaintenance) return ScheduleRules.MaintenanceColor;
        if (!string.IsNullOrEmpty(item.Color)) return item.Color;
        var experiments = schedule.Items.Where(candidate => !candidate.IsMaintenance).ToList();
        var index = Math.Max(0, experiments.IndexOf(item));
        return ScheduleRules.DefaultColors[index % ScheduleRules.DefaultColors.Count];
    }

    /// <summary>
    /// Inserts a new 7-day item after the selection (or after the last item, or on 1 January).
    /// With <paramref name="cascade"/> and a selection, later items move 7 days to make room.
    /// </summary>
    public static ScheduleItem InsertItem(Schedule schedule, string type, ScheduleItem? selected, bool cascade)
    {
        var selectedEnd = selected is not null && ScheduleRules.ParseDate(selected.EndDate) is not null ? selected.EndDate : null;
        var lastEnd = schedule.Items
            .Select(item => item.EndDate)
            .Where(value => ScheduleRules.ParseDate(value) is not null)
            .OrderBy(value => value, StringComparer.Ordinal)
            .LastOrDefault();
        var startDate = selectedEnd is not null
            ? ScheduleRules.AddDaysIso(selectedEnd, 1)
            : lastEnd is not null ? ScheduleRules.AddDaysIso(lastEnd, 1) : $"{schedule.Year}-01-01";
        var following = selected is not null ? FollowingItems(schedule.Items, selected) : [];
        if (selected is not null && cascade) ShiftItems(following, 7);

        var isMaintenance = type == ItemTypes.Maintenance;
        var item = new ScheduleItem
        {
            Type = isMaintenance ? ItemTypes.Maintenance : ItemTypes.Experiment,
            Name = isMaintenance ? "新维护安排" : "新实验安排",
            StartDate = startDate,
            EndDate = ScheduleRules.AddDaysIso(startDate, 6),
            Color = isMaintenance ? ScheduleRules.MaintenanceColor : ScheduleRules.DefaultColors[schedule.Items.Count % ScheduleRules.DefaultColors.Count]
        };
        schedule.Items.Add(item);
        ScheduleRules.RenumberInPlace(schedule.Items);
        return item;
    }
}
