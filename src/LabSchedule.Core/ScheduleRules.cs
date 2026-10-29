using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LabSchedule.Core;

public static partial class ScheduleRules
{
    public static readonly IReadOnlyList<string> DefaultColors =
    [
        "#4E79A7",
        "#F28E2B",
        "#59A14F",
        "#E15759",
        "#76B7B2",
        "#EDC948",
        "#B07AA1",
        "#FF9DA7",
        "#9C755F",
        "#BAB0AC"
    ];

    public const string MaintenanceColor = "#B0B0B0";
    public const string InvalidDayColor = "#F0F0F0";
    public const string WeekendColor = "#F7F7F7";

    [GeneratedRegex("^[0-9]{4}-[0-9]{2}-[0-9]{2}$")]
    private static partial Regex IsoDatePattern();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColorPattern();

    public static bool IsHexColor(string? value) => value is not null && HexColorPattern().IsMatch(value);

    // ---- dates -------------------------------------------------------------------------------

    public static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrEmpty(value) || !IsoDatePattern().IsMatch(value)) return null;
        var year = int.Parse(value[..4], CultureInfo.InvariantCulture);
        var month = int.Parse(value.Substring(5, 2), CultureInfo.InvariantCulture);
        var day = int.Parse(value.Substring(8, 2), CultureInfo.InvariantCulture);
        // JavaScript's Date maps years 0-99 to 1900-1999, so those never round-trip.
        if (year < 100 || month < 1 || month > 12) return null;
        if (day < 1 || day > DateTime.DaysInMonth(year, month)) return null;
        return new DateOnly(year, month, day);
    }

    public static string DateToIso(DateOnly date) =>
        $"{date.Year}-{date.Month.ToString("D2", CultureInfo.InvariantCulture)}-{date.Day.ToString("D2", CultureInfo.InvariantCulture)}";

    public static string AddDaysIso(string value, int days)
    {
        var date = ParseDate(value);
        if (date is null) return value;
        try
        {
            return DateToIso(date.Value.AddDays(days));
        }
        catch (ArgumentOutOfRangeException)
        {
            return value;
        }
    }

    public static int DiffDays(string? from, string? to)
    {
        var start = ParseDate(from);
        var end = ParseDate(to);
        if (start is null || end is null) return 0;
        return end.Value.DayNumber - start.Value.DayNumber;
    }

    public static int DaysInMonth(int year, int month)
    {
        if (month == 2) return IsLeapYear(year) ? 29 : 28;
        return month is 4 or 6 or 9 or 11 ? 30 : 31;
    }

    private static bool IsLeapYear(int year) => (year % 4 == 0 && year % 100 != 0) || year % 400 == 0;

    public static bool IsWeekend(int year, int month, int day)
    {
        if (year is < 1 or > 9999) return false;
        var weekday = new DateOnly(year, month, day).DayOfWeek;
        return weekday is DayOfWeek.Saturday or DayOfWeek.Sunday;
    }

    public static int DurationDays(ScheduleItem item)
    {
        var start = ParseDate(item.StartDate);
        var end = ParseDate(item.EndDate);
        if (start is null || end is null || start > end) return 0;
        return end.Value.DayNumber - start.Value.DayNumber + 1;
    }

    public static string ItemDisplayLabel(ScheduleItem item) =>
        string.Join(" · ", new[] { item.Name, item.Leader, $"{DurationDays(item)}天" }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

    public static bool IsDark(string? color)
    {
        var hex = (color ?? "#000000").Replace("#", "", StringComparison.Ordinal);
        if (hex.Length < 6 ||
            !int.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red) ||
            !int.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) ||
            !int.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
        {
            return false;
        }
        return (0.299 * red + 0.587 * green + 0.114 * blue) / 255 < 0.55;
    }

    public static string? NormalizeColor(string? color)
    {
        if (string.IsNullOrEmpty(color)) return null;
        var value = color.Trim();
        return IsHexColor(value) ? value.ToUpperInvariant() : null;
    }

    // ---- ordering and numbering -------------------------------------------------------------

    public static int CompareItemsByDate(ScheduleItem left, ScheduleItem right)
    {
        var byStart = string.CompareOrdinal(left.StartDate ?? "", right.StartDate ?? "");
        return byStart != 0 ? byStart : string.CompareOrdinal(left.EndDate ?? "", right.EndDate ?? "");
    }

    /// <summary>Stable sort by start date, then end date, then current relative order.</summary>
    public static List<ScheduleItem> SortItemsByDate(IEnumerable<ScheduleItem> items) =>
        items.Select((item, index) => (item, index))
            .OrderBy(entry => entry, Comparer<(ScheduleItem item, int index)>.Create((left, right) =>
            {
                var byDate = CompareItemsByDate(left.item, right.item);
                return byDate != 0 ? byDate : left.index.CompareTo(right.index);
            }))
            .Select(entry => entry.item)
            .ToList();

    /// <summary>Sorted copies with ids rewritten to EXP001… / MAINT001….</summary>
    public static List<ScheduleItem> AutoNumberItems(IEnumerable<ScheduleItem> items)
    {
        var copies = SortItemsByDate(items).Select(item => item.Clone()).ToList();
        AssignIds(copies);
        return copies;
    }

    /// <summary>Sorts the list in place and rewrites ids, keeping the same item instances.</summary>
    public static void RenumberInPlace(List<ScheduleItem> items)
    {
        var sorted = SortItemsByDate(items);
        items.Clear();
        items.AddRange(sorted);
        AssignIds(items);
    }

    private static void AssignIds(List<ScheduleItem> items)
    {
        var experimentIndex = 1;
        var maintenanceIndex = 1;
        foreach (var item in items)
        {
            var isMaintenance = item.Type == ItemTypes.Maintenance;
            var number = isMaintenance ? maintenanceIndex++ : experimentIndex++;
            item.Id = $"{(isMaintenance ? "MAINT" : "EXP")}{number.ToString("D3", CultureInfo.InvariantCulture)}";
        }
    }

    // ---- normalisation ------------------------------------------------------------------------

    /// <summary>Port of core.js <c>normalizeSchedule</c> applied to raw JSON.</summary>
    public static Schedule NormalizeJson(JsonNode? raw)
    {
        var schedule = JsValue.Of(raw);
        var yearValue = schedule["year"];
        var yearNumber = yearValue.Truthy ? yearValue.ToNumber() : DateTime.Now.Year;
        var year = double.IsFinite(yearNumber) && yearNumber == Math.Floor(yearNumber) && Math.Abs(yearNumber) <= int.MaxValue
            ? (int)yearNumber
            : DateTime.Now.Year;
        var settingsValue = schedule["settings"];
        var settings = settingsValue.Truthy ? settingsValue : JsValue.Of(new JsonObject());

        var items = new List<ScheduleItem>();
        if (schedule["items"].AsArray is { } rawItems)
        {
            foreach (var rawItem in rawItems)
            {
                var item = JsValue.Of(rawItem);
                var type = item["type"].IsString && item["type"].ToJsString() == ItemTypes.Maintenance
                    ? ItemTypes.Maintenance
                    : ItemTypes.Experiment;
                var color = item["color"];
                items.Add(new ScheduleItem
                {
                    Id = item["id"].OrString("").Trim(),
                    Type = type,
                    Name = item["name"].OrString("").Trim(),
                    Leader = item["leader"].OrString("").Trim(),
                    Unit = item["unit"].OrString("").Trim(),
                    Phone = item["phone"].OrString("").Trim(),
                    Email = item["email"].OrString("").Trim(),
                    StartDate = item["start_date"].OrString(""),
                    EndDate = item["end_date"].OrString(""),
                    Color = type == ItemTypes.Maintenance
                        ? MaintenanceColor
                        : NormalizeColor(color.Truthy ? color.ToJsString() : null),
                    Description = item["description"].OrString("").Trim()
                });
            }
        }

        return new Schedule
        {
            Year = year,
            Title = schedule["title"].OrString($"{year}年度实验时间安排图"),
            Settings = new ScheduleSettings
            {
                PageSize = settings["page_size"].OrString("A4").ToUpperInvariant(),
                Orientation = settings["orientation"].OrString("landscape").ToLowerInvariant(),
                Font = settings["font"].OrString("SimHei"),
                ShowLegend = !settings["show_legend"].IsFalse,
                ShowWeekends = settings["show_weekends"].Truthy,
                ShowMaintenanceInList = !settings["show_maintenance_in_list"].IsFalse
            },
            Items = SortItemsByDate(items)
        };
    }

    /// <summary>core.js <c>normalizeSchedule</c> for an in-memory schedule.</summary>
    public static Schedule Normalize(Schedule schedule) => NormalizeJson(ScheduleJson.ToJsonNode(schedule));

    /// <summary>core.js <c>prepareSchedule</c>: normalise, sort and renumber.</summary>
    public static Schedule PrepareSchedule(Schedule schedule)
    {
        var normalized = Normalize(schedule);
        normalized.Items = AutoNumberItems(normalized.Items);
        return normalized;
    }

    public static Schedule PrepareSchedule(JsonNode? raw)
    {
        var normalized = NormalizeJson(raw);
        normalized.Items = AutoNumberItems(normalized.Items);
        return normalized;
    }

    /// <summary>
    /// renderer.js <c>normalizeSchedule</c>: the lighter clean-up the editor applies to its own state
    /// (used for undo snapshots and the unsaved-changes fingerprint).
    /// </summary>
    public static Schedule NormalizeState(Schedule schedule)
    {
        var copy = schedule.Clone();
        copy.Title = string.IsNullOrEmpty(copy.Title) ? $"{copy.Year}年度实验时间安排图" : copy.Title;
        copy.Settings.PageSize = string.IsNullOrEmpty(copy.Settings.PageSize) ? "A4" : copy.Settings.PageSize;
        copy.Settings.Orientation = string.IsNullOrEmpty(copy.Settings.Orientation) ? "landscape" : copy.Settings.Orientation;
        copy.Settings.Font = string.IsNullOrEmpty(copy.Settings.Font) ? "SimHei" : copy.Settings.Font;
        foreach (var item in copy.Items)
        {
            item.Id = (item.Id ?? "").Trim();
            item.Type = item.Type == ItemTypes.Maintenance ? ItemTypes.Maintenance : ItemTypes.Experiment;
            item.Name = (item.Name ?? "").Trim();
            item.Leader = (item.Leader ?? "").Trim();
            item.Unit = (item.Unit ?? "").Trim();
            item.Phone = (item.Phone ?? "").Trim();
            item.Email = (item.Email ?? "").Trim();
            item.StartDate ??= "";
            item.EndDate ??= "";
            item.Color = item.Type == ItemTypes.Maintenance ? MaintenanceColor : (string.IsNullOrEmpty(item.Color) ? null : item.Color);
            item.Description = (item.Description ?? "").Trim();
        }
        copy.Items = SortItemsByDate(copy.Items);
        return copy;
    }

    // ---- validation ---------------------------------------------------------------------------

    /// <summary>Port of core.js <c>validateSchedule</c> (used for files and before writing output).</summary>
    public static List<string> Validate(JsonNode? raw)
    {
        var issues = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var source = JsValue.Of(raw is JsonObject ? raw : new JsonObject());
        var settings = source["settings"].IsObjectLike ? source["settings"] : JsValue.Of(new JsonObject());
        var year = source["year"].ToNumber();
        var title = source["title"].OrString("").Trim();
        var pageSize = settings["page_size"].OrString("A4").ToUpperInvariant();
        var orientation = settings["orientation"].OrString("landscape").ToLowerInvariant();

        if (!double.IsFinite(year) || year != Math.Floor(year) || year < 1900 || year > 2200)
        {
            issues.Add("年份必须在 1900 到 2200 之间");
        }
        if (title.Length == 0) issues.Add("标题不能为空");
        if (pageSize is not ("A3" or "A4")) issues.Add("页面大小只能是 A3 或 A4");
        if (orientation is not ("landscape" or "portrait")) issues.Add("方向只能是 landscape 或 portrait");
        if (source["items"].AsArray is not { } items)
        {
            issues.Add("items 必须是数组");
            return issues;
        }

        for (var index = 0; index < items.Count; index++)
        {
            var rawItem = JsValue.Of(items[index]);
            var item = rawItem.IsObjectLike ? rawItem : JsValue.Of(new JsonObject());
            var id = item["id"].OrString("").Trim();
            var label = item["id"].Truthy ? item["id"].ToJsString() : $"第 {index + 1} 项";
            var type = item["type"].OrString("").Trim();
            if (id.Length == 0) issues.Add($"{label} 缺少编号");
            if (id.Length > 0 && ids.Contains(id)) issues.Add($"{label} 编号重复");
            if (id.Length > 0) ids.Add(id);
            if (type is not (ItemTypes.Experiment or ItemTypes.Maintenance)) issues.Add($"{label} 类型无效");
            if (item["name"].OrString("").Trim().Length == 0) issues.Add($"{label} 缺少名称");
            var start = item["start_date"].Truthy ? ParseDate(item["start_date"].ToJsString()) : null;
            var end = item["end_date"].Truthy ? ParseDate(item["end_date"].ToJsString()) : null;
            if (start is null) issues.Add($"{label} 开始日期无效");
            if (end is null) issues.Add($"{label} 结束日期无效");
            if (start is not null && end is not null && start > end) issues.Add($"{label} 开始日期晚于结束日期");
            if (item["color"].Truthy && !IsHexColor(item["color"].ToJsString())) issues.Add($"{label} 颜色格式应为 #RRGGBB");
        }

        return issues;
    }

    public static List<string> Validate(Schedule schedule) => Validate(ScheduleJson.ToJsonNode(schedule));

    /// <summary>Port of renderer.js <c>collectIssues</c> (the live status line of the editor).</summary>
    public static List<string> CollectIssues(Schedule schedule)
    {
        var issues = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (schedule.Year == 0) issues.Add("年份不能为空");
        if (string.IsNullOrEmpty(schedule.Title)) issues.Add("标题不能为空");
        for (var index = 0; index < schedule.Items.Count; index++)
        {
            var item = schedule.Items[index];
            var line = string.IsNullOrEmpty(item.Id) ? $"第 {index + 1} 项" : item.Id;
            if (string.IsNullOrEmpty(item.Id)) issues.Add($"{line} 缺少编号");
            if (ids.Contains(item.Id ?? "")) issues.Add($"{line} 编号重复");
            ids.Add(item.Id ?? "");
            if (item.Type is not (ItemTypes.Experiment or ItemTypes.Maintenance)) issues.Add($"{line} 类型无效");
            if (string.IsNullOrEmpty(item.Name)) issues.Add($"{line} 缺少名称");
            var start = ParseDate(item.StartDate);
            var end = ParseDate(item.EndDate);
            if (start is null) issues.Add($"{line} 开始日期无效");
            if (end is null) issues.Add($"{line} 结束日期无效");
            if (start is not null && end is not null && start > end) issues.Add($"{line} 开始日期晚于结束日期");
            if (!string.IsNullOrEmpty(item.Color) && !IsHexColor(item.Color)) issues.Add($"{line} 颜色格式应为 #RRGGBB");
        }
        return issues;
    }

    // ---- output preparation -----------------------------------------------------------------

    public static (List<ScheduleItem> Items, List<string> Warnings) ClipItemsToYear(IEnumerable<ScheduleItem> items, int year)
    {
        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year, 12, 31);
        var clipped = new List<ScheduleItem>();
        var warnings = new List<string>();

        foreach (var item in items)
        {
            var start = ParseDate(item.StartDate);
            var end = ParseDate(item.EndDate);
            if (start is null || end is null || start > end) continue;

            if (end < yearStart || start > yearEnd)
            {
                warnings.Add($"{item.Id} ({item.Name}) is outside {year} and was ignored.");
                continue;
            }

            var nextStart = start < yearStart ? yearStart : start.Value;
            var nextEnd = end > yearEnd ? yearEnd : end.Value;
            if (nextStart != start || nextEnd != end)
            {
                warnings.Add($"{item.Id} ({item.Name}) was clipped to {DateToIso(nextStart)} - {DateToIso(nextEnd)}.");
            }

            var copy = item.Clone();
            copy.StartDate = DateToIso(nextStart);
            copy.EndDate = DateToIso(nextEnd);
            clipped.Add(copy);
        }

        return (clipped, warnings);
    }

    public static (Schedule Schedule, List<string> Warnings) PrepareOutputSchedule(Schedule schedule)
    {
        var prepared = PrepareSchedule(schedule);
        var (items, warnings) = ClipItemsToYear(prepared.Items, prepared.Year);
        prepared.Items = AutoNumberItems(items);
        return (prepared, warnings);
    }

    /// <summary>core.js <c>assignColors</c>: colours used in the generated HTML/PDF.</summary>
    public static Dictionary<string, string> AssignColors(IEnumerable<ScheduleItem> items)
    {
        var colors = new Dictionary<string, string>(StringComparer.Ordinal);
        var nextIndex = 0;
        foreach (var item in items)
        {
            if (item.Type == ItemTypes.Maintenance)
            {
                colors[item.Id] = MaintenanceColor;
            }
            else if (!string.IsNullOrEmpty(item.Color))
            {
                colors[item.Id] = item.Color;
            }
            else
            {
                colors[item.Id] = DefaultColors[nextIndex % DefaultColors.Count];
                nextIndex++;
            }
        }
        return colors;
    }
}
