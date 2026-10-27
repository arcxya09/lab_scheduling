using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace LabSchedule.Timeline;

/// <summary>Looks up the theme-keyed colours declared in App.xaml for the element's current theme.</summary>
internal static class ThemeColors
{
    public static Color Get(FrameworkElement element, string key)
    {
        var themeKey = element.ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
        var dictionary = (ResourceDictionary)Application.Current.Resources.ThemeDictionaries[themeKey];
        return (Color)dictionary[key];
    }

    public static SolidColorBrush Brush(FrameworkElement element, string key) => new(Get(element, key));

    /// <summary>
    /// A WinUI theme resource (e.g. "TextFillColorSecondaryBrush") as it looks in the element's own
    /// theme. Application.Current.Resources[key] would follow the app theme instead, which differs
    /// when the user picks a theme on the settings page.
    /// </summary>
    public static Brush System(FrameworkElement element, string key)
    {
        var themeKeys = element.ActualTheme == ElementTheme.Dark ? new[] { "Dark", "Default" } : new[] { "Light" };
        foreach (var dictionary in Application.Current.Resources.MergedDictionaries)
        {
            if (FindThemed(dictionary, themeKeys, key) is Brush brush) return brush;
        }
        return (Brush)Application.Current.Resources[key];
    }

    public static Color SystemColor(string key) => (Color)Application.Current.Resources[key];

    private static object? FindThemed(ResourceDictionary dictionary, string[] themeKeys, string key)
    {
        foreach (var themeKey in themeKeys)
        {
            if (dictionary.ThemeDictionaries.TryGetValue(themeKey, out var themed) &&
                themed is ResourceDictionary themeDictionary &&
                themeDictionary.TryGetValue(key, out var value))
            {
                return value;
            }
        }
        foreach (var merged in dictionary.MergedDictionaries)
        {
            if (FindThemed(merged, themeKeys, key) is { } value) return value;
        }
        return null;
    }
}
