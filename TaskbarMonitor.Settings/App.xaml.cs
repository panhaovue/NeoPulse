using Microsoft.UI.Xaml;

namespace TaskbarMonitor.Settings;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        this.InitializeComponent();
        this.UnhandledException += (s, e) =>
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    "TaskbarMonitor", "crash.log"),
                $"{System.DateTime.Now}: {e.Message}\n{e.Exception}\n");
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
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    "TaskbarMonitor", "crash.log"),
                $"{System.DateTime.Now}: FATAL: {ex}\n");
        }
    }
}
