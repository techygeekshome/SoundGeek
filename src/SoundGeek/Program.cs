using Avalonia;

namespace SoundGeek;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Without these, anything unhandled closes the window with no explanation at all.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Write("Unhandled: " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Write("Unobserved: " + e.Exception);
            e.SetObserved();
        };

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Write("SoundGeek stopped: " + ex);
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
