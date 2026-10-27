using System.Globalization;
using Microsoft.UI;
using Windows.UI;

namespace LabSchedule;

internal static class ColorUtil
{
    /// <summary>Parses #RRGGBB (anything else falls back to black, like an invalid CSS colour).</summary>
    public static Color Parse(string? hex)
    {
        var value = (hex ?? "").Trim().TrimStart('#');
        if (value.Length == 6 && uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }
        return Colors.Black;
    }

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static Color WithAlpha(Color color, double alpha) =>
        Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), color.R, color.G, color.B);
}
