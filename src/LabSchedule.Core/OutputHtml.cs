using System.Text;

namespace LabSchedule.Core;

public sealed record OutputHtmlResult(string Html, Schedule Schedule, IReadOnlyList<string> Warnings);

/// <summary>
/// Builds the printable yearly calendar. The markup is a line-for-line port of the Electron
/// build's core.js so the generated HTML/PDF stays byte-identical.
/// </summary>
public static class OutputHtml
{
    private sealed record Segment(string ItemId, int Month, int StartDay, int EndDay, int Lane, string Color, string Label);

    public static string Build(Schedule schedule) => BuildResult(schedule).Html;

    public static OutputHtmlResult BuildResult(Schedule schedule)
    {
        var (normalized, warnings) = ScheduleRules.PrepareOutputSchedule(schedule);
        var colors = ScheduleRules.AssignColors(normalized.Items);
        var segments = BuildSegments(normalized.Items, normalized.Year, colors);
        var laneCounts = MonthLaneCounts(segments);
        var experimentCount = normalized.Items.Count(item => item.Type == ItemTypes.Experiment);
        var totalDays = normalized.Items.Sum(ScheduleRules.DurationDays);
        var monthRows = string.Join("\n", Enumerable.Range(1, 12).Select(month =>
            MonthRowHtml(normalized, month, laneCounts[month], segments.Where(segment => segment.Month == month).ToList())));
        var listedItems = normalized.Settings.ShowMaintenanceInList
            ? normalized.Items
            : normalized.Items.Where(item => item.Type != ItemTypes.Maintenance).ToList();
        var rows = string.Join("\n", listedItems.Select(item => ExperimentRowHtml(item, colors[item.Id])));
        var title = EscapeHtml(normalized.Title);

        var html = $$"""
        <!doctype html>
        <html lang="zh-CN">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>{{title}}</title>
          <style>
            :root {
              --ink: #182022;
              --muted: #627076;
              --hairline: #ccd6d8;
              --paper: #fbfcfa;
              --invalid: {{ScheduleRules.InvalidDayColor}};
              --weekend: {{ScheduleRules.WeekendColor}};
              --track-height: 27px;
              --track-gap: 5px;
              --radius: 6px;
            }
            * { box-sizing: border-box; }
            body {
              margin: 0;
              color: var(--ink);
              background: #e8eeed;
              font-family: "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", sans-serif;
              font-size: 16px;
              line-height: 1.38;
            }
            .sheet {
              width: min(1180px, calc(100vw - 28px));
              min-height: calc(100vh - 28px);
              margin: 14px auto;
              padding: 22px;
              background: var(--paper);
              border: 1px solid rgba(24, 32, 34, .12);
              border-radius: 8px;
              box-shadow: 0 18px 46px rgba(24, 32, 34, .14);
            }
            .header {
              display: grid;
              grid-template-columns: 1fr auto;
              gap: 18px;
              align-items: end;
              padding-bottom: 14px;
              border-bottom: 2px solid var(--ink);
            }
            h1 {
              margin: 0;
              font-size: clamp(28px, 4vw, 42px);
              line-height: 1.08;
              letter-spacing: 0;
              font-weight: 800;
            }
            .meta {
              display: flex;
              gap: 8px;
              flex-wrap: wrap;
              justify-content: flex-end;
              color: var(--muted);
              font-size: 14px;
              white-space: nowrap;
            }
            .meta span {
              padding: 6px 9px;
              border: 1px solid var(--hairline);
              border-radius: 999px;
              background: #fff;
            }
            .timeline-wrap { margin-top: 16px; overflow-x: auto; padding-bottom: 4px; }
            .timeline {
              min-width: 860px;
              border: 1px solid var(--hairline);
              border-radius: 8px;
              overflow: hidden;
              background: #fff;
            }
            .day-header,
            .month-row {
              display: grid;
              grid-template-columns: 62px minmax(0, 1fr);
            }
            .day-header {
              background: #233032;
              color: #fff;
              font-size: 13px;
              font-weight: 700;
            }
            .month-label,
            .corner {
              display: grid;
              place-items: center;
              border-right: 1px solid rgba(255, 255, 255, .18);
            }
            .days-grid,
            .month-track {
              display: grid;
              grid-template-columns: repeat(31, minmax(0, 1fr));
            }
            .days-grid > div {
              min-height: 32px;
              display: grid;
              place-items: center;
              border-left: 1px solid rgba(255, 255, 255, .12);
            }
            .month-row {
              border-top: 1px solid var(--hairline);
              min-height: calc(var(--lanes) * var(--track-height) + (var(--lanes) - 1) * var(--track-gap) + 22px);
            }
            .month-label {
              color: #273235;
              background: #f5f7f5;
              border-right: 1px solid var(--hairline);
              font-size: 16px;
              font-weight: 800;
            }
            .month-track {
              position: relative;
              gap: var(--track-gap) 0;
              grid-template-rows: repeat(var(--lanes), var(--track-height));
              padding: 11px 0;
              background: #fff;
            }
            .date-fill,
            .date-line {
              position: absolute;
              top: 0;
              bottom: 0;
              pointer-events: none;
            }
            .date-fill {
              left: calc((var(--day) - 1) * 100% / 31);
              width: calc(100% / 31);
              z-index: 0;
            }
            .date-line {
              left: calc((var(--day) - 1) * 100% / 31);
              z-index: 3;
              border-left: 1px solid rgba(24, 32, 34, .14);
            }
            .date-line.end-line { left: 100%; }
            .date-fill.invalid-day { background: var(--invalid); }
            .date-fill.weekend-day { background: var(--weekend); }
            .segment {
              z-index: 2;
              align-self: stretch;
              margin: 0 2px;
              padding: 5px 7px;
              border-radius: var(--radius);
              color: var(--segment-text);
              background: var(--segment-color);
              overflow: hidden;
              text-overflow: ellipsis;
              white-space: nowrap;
              font-size: 12px;
              font-weight: 700;
            }
            .experiment-list { margin-top: 18px; }
            .experiment-list h2 {
              margin: 0 0 9px;
              font-size: 18px;
              letter-spacing: 0;
            }
            table {
              width: 100%;
              border-collapse: collapse;
              font-size: 13px;
              background: #fff;
              border: 1px solid var(--hairline);
              border-radius: 8px;
              overflow: hidden;
            }
            th, td {
              padding: 8px 9px;
              text-align: left;
              border-bottom: 1px solid var(--hairline);
              vertical-align: top;
            }
            th {
              color: #fff;
              background: #233032;
              font-size: 12px;
              font-weight: 800;
            }
            tr:last-child td { border-bottom: 0; }
            .swatch {
              display: inline-block;
              width: 18px;
              height: 12px;
              border-radius: 4px;
              vertical-align: -1px;
              box-shadow: inset 0 0 0 1px rgba(0, 0, 0, .16);
            }
            .note {
              margin-top: 10px;
              color: var(--muted);
              font-size: 12px;
            }
            @page {
              size: {{normalized.Settings.PageSize}} {{normalized.Settings.Orientation}};
              margin: 7mm;
            }
            @media print {
              body {
                background: #fff;
                print-color-adjust: exact;
                -webkit-print-color-adjust: exact;
              }
              .sheet {
                width: 100%;
                min-height: calc(100vh - 14mm);
                margin: 0;
                padding: 0;
                border: 0;
                border-radius: 0;
                box-shadow: none;
              }
              .timeline-wrap { overflow: visible; }
              .timeline { min-width: 0; }
              .month-row,
              .experiment-list {
                break-inside: avoid;
                page-break-inside: avoid;
              }
            }
          </style>
        </head>
        <body>
          <main class="sheet">
            <header class="header">
              <h1>{{title}}</h1>
              <div class="meta">
                <span>{{normalized.Year}}</span>
                <span>{{experimentCount}} 项实验</span>
                <span>{{totalDays}} 天占用</span>
              </div>
            </header>
            <section class="timeline-wrap" aria-label="年度实验时间安排图">
              <div class="timeline">
                <div class="day-header">
                  <div class="corner">月份</div>
                  <div class="days-grid">{{DayHeaderHtml()}}</div>
                </div>
                {{monthRows}}
              </div>
            </section>
            <section class="experiment-list">
              <h2>实验列表</h2>
              <table>
                <thead>
                  <tr>
                    <th>颜色</th>
                    <th>编号</th>
                    <th>类型</th>
                    <th>名称</th>
                    <th>负责人</th>
                    <th>起止日期</th>
                    <th>时长</th>
                    <th>备注</th>
                  </tr>
                </thead>
                <tbody>{{rows}}</tbody>
              </table>
            </section>
          </main>
        </body>
        </html>
        """;

        // Raw literals take the source file's line endings; the original always emitted "\n".
        return new OutputHtmlResult(html.Replace("\r\n", "\n", StringComparison.Ordinal), normalized, warnings);
    }

    private static string DayHeaderHtml() =>
        string.Concat(Enumerable.Range(1, 31).Select(day => $"<div>{day}</div>"));

    private static string MonthRowHtml(Schedule schedule, int month, int laneCount, List<Segment> segments) =>
        $"<div class=\"month-row\" style=\"--lanes: {laneCount};\">\n" +
        $"  <div class=\"month-label\">{month}月</div>\n" +
        "  <div class=\"month-track\">\n" +
        $"    {DateCellsHtml(schedule, month)}\n" +
        $"    {string.Join("\n", segments.Select(SegmentHtml))}\n" +
        "  </div>\n" +
        "</div>";

    private static string DateCellsHtml(Schedule schedule, int month)
    {
        var monthDays = ScheduleRules.DaysInMonth(schedule.Year, month);
        var parts = new List<string>();
        for (var day = 1; day <= 31; day++)
        {
            string? extra = null;
            if (day > monthDays) extra = "invalid-day";
            else if (schedule.Settings.ShowWeekends && ScheduleRules.IsWeekend(schedule.Year, month, day)) extra = "weekend-day";
            if (extra is not null) parts.Add($"<div class=\"date-fill {extra}\" style=\"--day: {day};\"></div>");
        }
        for (var day = 1; day <= 31; day++)
        {
            parts.Add($"<div class=\"date-line\" style=\"--day: {day};\"></div>");
        }
        parts.Add("<div class=\"date-line end-line\"></div>");
        return string.Join("\n", parts);
    }

    private static string SegmentHtml(Segment segment)
    {
        var textColor = ScheduleRules.IsDark(segment.Color) ? "#ffffff" : "#182022";
        var label = EscapeHtml(segment.Label);
        return $"<div class=\"segment\" style=\"grid-column: {segment.StartDay} / {segment.EndDay + 1}; grid-row: {segment.Lane + 1}; " +
               $"--segment-color: {segment.Color}; --segment-text: {textColor};\" title=\"{label}\">{label}</div>";
    }

    private static string ExperimentRowHtml(ScheduleItem item, string color)
    {
        var typeText = item.Type == ItemTypes.Maintenance ? "维护" : "实验";
        var leader = string.IsNullOrEmpty(item.Leader) ? "-" : item.Leader;
        return "<tr>\n" +
               $"  <td><span class=\"swatch\" style=\"background: {color};\"></span></td>\n" +
               $"  <td>{EscapeHtml(item.Id)}</td>\n" +
               $"  <td>{typeText}</td>\n" +
               $"  <td>{EscapeHtml(item.Name)}</td>\n" +
               $"  <td>{EscapeHtml(leader)}</td>\n" +
               $"  <td>{item.StartDate} 至 {item.EndDate}</td>\n" +
               $"  <td>{ScheduleRules.DurationDays(item)} 天</td>\n" +
               $"  <td>{EscapeHtml(item.Description ?? "")}</td>\n" +
               "</tr>";
    }

    private static List<Segment> BuildSegments(List<ScheduleItem> items, int year, Dictionary<string, string> colors)
    {
        var byMonth = new SortedDictionary<int, List<Segment>>();
        foreach (var item in items)
        {
            var start = ScheduleRules.ParseDate(item.StartDate);
            var end = ScheduleRules.ParseDate(item.EndDate);
            if (start is null || end is null || start > end) continue;
            for (var month = 1; month <= 12; month++)
            {
                var monthStart = new DateOnly(year, month, 1);
                var monthEnd = new DateOnly(year, month, ScheduleRules.DaysInMonth(year, month));
                if (end < monthStart || start > monthEnd) continue;
                var startDay = start > monthStart ? start.Value.Day : 1;
                var endDay = end < monthEnd ? end.Value.Day : monthEnd.Day;
                if (!byMonth.TryGetValue(month, out var list)) byMonth[month] = list = [];
                list.Add(new Segment(item.Id, month, startDay, endDay, 0, colors[item.Id], ScheduleRules.ItemDisplayLabel(item)));
            }
        }
        return byMonth.Values.SelectMany(AssignLanes).ToList();
    }

    private static IEnumerable<Segment> AssignLanes(List<Segment> segments)
    {
        var lanes = new List<List<Segment>>();
        var placedSegments = new List<Segment>();
        var ordered = segments
            .OrderBy(segment => segment.StartDay)
            .ThenBy(segment => segment.EndDay)
            .ThenBy(segment => segment.ItemId, StringComparer.Ordinal);
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

    private static Dictionary<int, int> MonthLaneCounts(List<Segment> segments)
    {
        var counts = Enumerable.Range(1, 12).ToDictionary(month => month, _ => 1);
        foreach (var segment in segments)
        {
            counts[segment.Month] = Math.Max(counts[segment.Month], segment.Lane + 1);
        }
        return counts;
    }

    public static string EscapeHtml(string? value)
    {
        var text = value ?? "";
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            builder.Append(ch switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\'' => "&#039;",
                _ => ch.ToString()
            });
        }
        return builder.ToString();
    }
}
