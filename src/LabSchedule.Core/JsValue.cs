using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LabSchedule.Core;

/// <summary>
/// A JSON value read with JavaScript semantics, so imported files are coerced exactly the way
/// the original Electron build did (<c>String(x || "")</c>, <c>Number(x)</c>, truthiness, ...).
/// </summary>
internal readonly struct JsValue
{
    private readonly JsonNode? _node;

    private JsValue(JsonNode? node, bool isUndefined)
    {
        _node = node;
        IsUndefined = isUndefined;
    }

    public static JsValue Undefined => new(null, true);

    public static JsValue Of(JsonNode? node) => new(node, false);

    public bool IsUndefined { get; }

    public bool IsNull => !IsUndefined && _node is null;

    public JsonObject? AsObject => _node as JsonObject;

    public JsonArray? AsArray => _node as JsonArray;

    /// <summary><c>value &amp;&amp; typeof value === "object"</c> (arrays included).</summary>
    public bool IsObjectLike => _node is JsonObject or JsonArray;

    public JsValue this[string key]
    {
        get
        {
            if (_node is JsonObject obj && obj.TryGetPropertyValue(key, out var child))
            {
                return Of(child);
            }
            return Undefined;
        }
    }

    private JsonValueKind Kind => _node?.GetValueKind() ?? JsonValueKind.Null;

    public bool IsString => _node is JsonValue && Kind == JsonValueKind.String;

    public bool IsFalse => _node is JsonValue && Kind == JsonValueKind.False;

    public bool Truthy
    {
        get
        {
            if (_node is null) return false;
            return Kind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => _node.GetValue<string>().Length > 0,
                JsonValueKind.Number => _node.GetValue<double>() is var number && number != 0 && !double.IsNaN(number),
                _ => true
            };
        }
    }

    /// <summary><c>String(value)</c>.</summary>
    public string ToJsString()
    {
        if (IsUndefined) return "undefined";
        if (_node is null) return "null";
        return Kind switch
        {
            JsonValueKind.String => _node.GetValue<string>(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => FormatNumber(_node.GetValue<double>()),
            JsonValueKind.Array => string.Join(",", _node.AsArray().Select(child => child is null ? "" : Of(child).ToJsString())),
            _ => "[object Object]"
        };
    }

    /// <summary><c>String(value || fallback)</c>.</summary>
    public string OrString(string fallback) => Truthy ? ToJsString() : fallback;

    /// <summary><c>Number(value)</c>.</summary>
    public double ToNumber()
    {
        if (IsUndefined) return double.NaN;
        if (_node is null) return 0;
        return Kind switch
        {
            JsonValueKind.Number => _node.GetValue<double>(),
            JsonValueKind.True => 1,
            JsonValueKind.False => 0,
            JsonValueKind.String => StringToNumber(_node.GetValue<string>()),
            JsonValueKind.Array => StringToNumber(ToJsString()),
            _ => double.NaN
        };
    }

    public static string FormatNumber(double value)
    {
        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "Infinity";
        if (double.IsNegativeInfinity(value)) return "-Infinity";
        if (value == Math.Floor(value) && Math.Abs(value) < 1e21)
        {
            return value.ToString("F0", CultureInfo.InvariantCulture);
        }
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static double StringToNumber(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return 0;
        if (trimmed is "Infinity" or "+Infinity") return double.PositiveInfinity;
        if (trimmed == "-Infinity") return double.NegativeInfinity;
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return long.TryParse(trimmed[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var hex)
                ? hex
                : double.NaN;
        }
        foreach (var ch in trimmed)
        {
            if (!(char.IsAsciiDigit(ch) || ch is '+' or '-' or '.' or 'e' or 'E')) return double.NaN;
        }
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : double.NaN;
    }
}
