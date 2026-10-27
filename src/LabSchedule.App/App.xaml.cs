using Microsoft.UI.Xaml;

namespace LabSchedule;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
#if DEBUG
        // Lets a debug build be checked in the other theme without changing Windows settings.
        switch (Environment.GetEnvironmentVariable("LAB_SCHEDULE_THEME"))
        {
            case "Dark":
                RequestedTheme = ApplicationTheme.Dark;
                break;
            case "Light":
                RequestedTheme = ApplicationTheme.Light;
                break;
        }
#endif
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
