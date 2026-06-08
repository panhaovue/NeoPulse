using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace NeoPulse;

public sealed partial class MainWindow : Window
{
    public AppSettings S => AppSettings.Instance;

    private string? _currentBackdrop;
    private MicaBackdrop? _micaBackdrop;
    private DesktopAcrylicBackdrop? _acrylicBackdrop;

    public MainWindow()
    {
        InitializeComponent();
        if (Content is FrameworkElement root)
            root.DataContext = AppSettings.Instance;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonForegroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonHoverBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonHoverForegroundColor = Colors.Transparent;

        var sz = new SizeInt32(1000, 950);
        appWindow.Resize(sz);

        // Multi-monitor aware: center on the current screen's work area
        var da = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
        var wa = da.WorkArea;
        appWindow.Move(new PointInt32(
            wa.X + (wa.Width - sz.Width) / 2,
            wa.Y + (wa.Height - sz.Height) / 2));

        if (appWindow.Presenter is OverlappedPresenter ovp)
            ovp.IsResizable = ovp.IsMaximizable = ovp.IsMinimizable = false;

        if (Content is FrameworkElement fe)
            fe.ActualThemeChanged += (_, _) => UpdateTitleBarButtons();

        // Subscribe to setting changes that need Win32 side effects
        AppSettings.Instance.PropertyChanged += OnSettingChanged;
        ApplyBackdrop(AppSettings.Instance.Backdrop);

        // React to system theme changes
        SystemEvents.UserPreferenceChanged += OnUserPrefChanged;
        Closed += (_, _) =>
        {
            AppSettings.Instance.PropertyChanged -= OnSettingChanged;
            AppSettings.Instance.Save();
            SystemEvents.UserPreferenceChanged -= OnUserPrefChanged;
        };
    }

    private void OnSettingChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.ThemeMode):
                ApplyTheme();
                ApplyBackdrop(AppSettings.Instance.Backdrop);
                break;
            case nameof(AppSettings.Backdrop):
                ApplyBackdrop(AppSettings.Instance.Backdrop);
                break;
            case nameof(AppSettings.Language):
                DispatcherQueue.TryEnqueue(UpdateComboItems);
                break;
        }
    }

    private void OnUserPrefChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General && AppSettings.Instance.ThemeMode == "auto")
        {
            DispatcherQueue.TryEnqueue(() => { ApplyTheme(); ApplyBackdrop(AppSettings.Instance.Backdrop); });
        }
    }

    private void ApplyTheme()
    {
        if (Content is FrameworkElement r)
            r.RequestedTheme = AppSettings.Instance.ThemeMode switch
            {
                "light" => ElementTheme.Light,
                "dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        UpdateTitleBarButtons();
    }

    private void ApplyBackdrop(string? name)
    {
        if (_currentBackdrop == name) return;
        _currentBackdrop = name;

        if (name is "Mica" or "Mica Alt")
        {
            _micaBackdrop ??= new MicaBackdrop();
            _micaBackdrop.Kind = name == "Mica Alt" ? MicaKind.BaseAlt : MicaKind.Base;
            SystemBackdrop = _micaBackdrop;
        }
        else if (name == "Acrylic")
        {
            _acrylicBackdrop ??= new DesktopAcrylicBackdrop();
            SystemBackdrop = _acrylicBackdrop;
        }
        else
        {
            _micaBackdrop ??= new MicaBackdrop();
            SystemBackdrop = _micaBackdrop;
        }
    }

    private void UpdateTitleBarButtons()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
            if (appWindow == null) return;
            bool dark = (Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark;
            var fg = dark ? Colors.White : Colors.Black;
            appWindow.TitleBar.ButtonForegroundColor = fg;
            appWindow.TitleBar.ButtonHoverForegroundColor = fg;
            appWindow.TitleBar.ButtonPressedForegroundColor = fg;
        }
        catch { }
    }

    private void UpdateComboItems()
    {
        bool zh = AppSettings.Instance.Language == "zh";
        if (ThemeModeCombo.Items.Count >= 3)
        {
            ((ComboBoxItem)ThemeModeCombo.Items[0]).Content = zh ? "自动" : "Auto (System)";
            ((ComboBoxItem)ThemeModeCombo.Items[1]).Content = zh ? "浅色" : "Light";
            ((ComboBoxItem)ThemeModeCombo.Items[2]).Content = zh ? "深色" : "Dark";
        }
        if (LayoutCombo.Items.Count >= 2)
        {
            ((ComboBoxItem)LayoutCombo.Items[0]).Content = zh ? "横向" : "Horizontal";
            ((ComboBoxItem)LayoutCombo.Items[1]).Content = zh ? "纵向" : "Vertical";
        }
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NeoPulse", "logs");
        Directory.CreateDirectory(logDir);
        System.Diagnostics.Process.Start("explorer.exe", logDir);
    }
}