using Microsoft.UI.Xaml;

namespace LabSchedule;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) => WriteCrashLog(args.Exception);
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

    /// <summary>Keeps the details of a crash in %LocalAppData%\LabSchedule\crash.log.</summary>
    private static void WriteCrashLog(Exception error)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LabSchedule");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "crash.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {error}\n\n");
        }
        catch (IOException)
        {
        }
    }
}
