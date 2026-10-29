using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LabSchedule.Core;

/// <summary>Reads and writes schedule JSON in the same layout as <c>JSON.stringify(schedule, null, 2)</c>.</summary>
public static class ScheduleJson
{
    private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = false };
    private static readonly JsonDocumentOptions DocumentOptions = new() { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow };

    public static JsonNode? Parse(string text) => JsonNode.Parse(text, NodeOptions, DocumentOptions);

    public static JsonObject ToJsonNode(Schedule schedule) => new()
    {
        // Stored as double so every number node reads back through GetValue<double>().
        ["year"] = (double)schedule.Year,
        ["title"] = schedule.Title,
        ["settings"] = new JsonObject
        {
            ["page_size"] = schedule.Settings.PageSize,
            ["orientation"] = schedule.Settings.Orientation,
            ["font"] = schedule.Settings.Font,
            ["show_legend"] = schedule.Settings.ShowLegend,
            ["show_weekends"] = schedule.Settings.ShowWeekends,
            ["show_maintenance_in_list"] = schedule.Settings.ShowMaintenanceInList
        },
        ["items"] = new JsonArray(schedule.Items.Select(item => (JsonNode)ItemNode(item)).ToArray())
    };

    private static JsonObject ItemNode(ScheduleItem item)
    {
        var node = new JsonObject
        {
            ["id"] = item.Id,
            ["type"] = item.Type,
            ["name"] = item.Name,
            ["leader"] = item.Leader
        };
        // Contact details came after 2.0.0; leaving out empty ones keeps older files unchanged.
        if (!string.IsNullOrEmpty(item.Unit)) node["unit"] = item.Unit;
        if (!string.IsNullOrEmpty(item.Phone)) node["phone"] = item.Phone;
        if (!string.IsNullOrEmpty(item.Email)) node["email"] = item.Email;
        node["start_date"] = item.StartDate;
        node["end_date"] = item.EndDate;
        node["color"] = item.Color;
        node["description"] = item.Description;
        return node;
    }

    /// <summary>The text written to disk: two-space indentation plus a trailing newline.</summary>
    public static string ToFileText(Schedule schedule) => Stringify(ToJsonNode(schedule), indent: 2) + "\n";

    /// <summary>Equivalent of <c>JSON.stringify(node, null, indent)</c>.</summary>
    public static string Stringify(JsonNode? node, int indent = 0)
    {
        var builder = new StringBuilder();
        Write(builder, node, indent, 0);
        return builder.ToString();
    }

    private static void Write(StringBuilder builder, JsonNode? node, int indent, int depth)
    {
        switch (node)
        {
            case null:
                builder.Append("null");
                break;
            case JsonObject obj:
                WriteContainer(builder, '{', '}', obj.ToList(), indent, depth, (entry, level) =>
                {
                    WriteString(builder, entry.Key);
                    builder.Append(indent > 0 ? ": " : ":");
                    Write(builder, entry.Value, indent, level);
                });
                break;
            case JsonArray array:
                WriteContainer(builder, '[', ']', array.ToList(), indent, depth, (child, level) => Write(builder, child, indent, level));
                break;
            default:
                WriteValue(builder, node);
                break;
        }
    }

    private static void WriteContainer<T>(StringBuilder builder, char open, char close, IReadOnlyList<T> entries, int indent, int depth, Action<T, int> writeEntry)
    {
        builder.Append(open);
        if (entries.Count == 0)
        {
            builder.Append(close);
            return;
        }
        for (var index = 0; index < entries.Count; index++)
        {
            if (index > 0) builder.Append(',');
            if (indent > 0) builder.Append('\n').Append(' ', indent * (depth + 1));
            writeEntry(entries[index], depth + 1);
        }
        if (indent > 0) builder.Append('\n').Append(' ', indent * depth);
        builder.Append(close);
    }

    private static void WriteValue(StringBuilder builder, JsonNode node)
    {
        switch (node.GetValueKind())
        {
            case JsonValueKind.String:
                WriteString(builder, node.GetValue<string>());
                break;
            case JsonValueKind.True:
                builder.Append("true");
                break;
            case JsonValueKind.False:
                builder.Append("false");
                break;
            case JsonValueKind.Number:
                var number = node.GetValue<double>();
                builder.Append(double.IsFinite(number) ? JsValue.FormatNumber(number) : "null");
                break;
            default:
                builder.Append("null");
                break;
        }
    }

    private static void WriteString(StringBuilder builder, string value)
    {
        builder.Append('"');
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            switch (ch)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (ch < 0x20)
                    {
                        AppendEscape(builder, ch);
                    }
                    else if (char.IsHighSurrogate(ch) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
                    {
                        builder.Append(ch).Append(value[++index]);
                    }
                    else if (char.IsSurrogate(ch))
                    {
                        AppendEscape(builder, ch);
                    }
                    else
                    {
                        builder.Append(ch);
                    }
                    break;
            }
        }
        builder.Append('"');
    }

    private static void AppendEscape(StringBuilder builder, char ch) =>
        builder.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
}
