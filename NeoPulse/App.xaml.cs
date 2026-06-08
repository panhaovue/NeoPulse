using Microsoft.UI.Xaml;

namespace NeoPulse;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        // Required for single-file self-extraction
        System.Environment.SetEnvironmentVariable(
            "MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY",
            System.AppContext.BaseDirectory);

        this.InitializeComponent();
        Logger.Cleanup();
        Logger.Info("App starting");

        this.UnhandledException += (s, e) =>
        {
            Logger.Error("Unhandled exception", e.Exception);
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new WidgetWindow();
            _window.Activate();
        }
        catch (System.Exception ex)
        {
            Logger.Error("FATAL: OnLaunched failed", ex);
        }
    }
}
