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

    /// <summary>A brush from the WinUI theme resources (e.g. "AccentFillColorDefaultBrush").</summary>
    public static Brush System(string key) => (Brush)Application.Current.Resources[key];

    public static Color SystemColor(string key) => (Color)Application.Current.Resources[key];
}
