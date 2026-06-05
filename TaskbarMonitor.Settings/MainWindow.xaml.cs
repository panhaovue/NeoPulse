using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using System.Runtime.InteropServices;
using System.Linq;
using WinRT;

namespace TaskbarMonitor.Settings;

public sealed partial class MainWindow : Window
{
    private bool _loading = true;
    private MicaController? _micaController;
    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _backdropConfig;

    public MainWindow()
    {
        InitializeComponent();

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonForegroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonHoverBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonHoverForegroundColor = Colors.Transparent;

        var size = new SizeInt32(1200, 800);
        appWindow.Resize(size);
        int screenW = GetSystemMetrics(0);
        int screenH = GetSystemMetrics(1);
        appWindow.Move(new PointInt32((screenW - size.Width) / 2, (screenH - size.Height) / 2));

        if (Content is FrameworkElement fe)
        {
            fe.ActualThemeChanged += (_, _) =>
            {
                SetBackdropConfigTheme();
                UpdateTitleBarButtons();
            };
        }

        ThemeModeCombo.ItemsSource = new[] { "Auto (System)", "Light", "Dark" };
        BackdropCombo.ItemsSource = new[] { "Mica", "Mica Alt", "Acrylic", "Acrylic Thin", "None" };

        LoadSystemFonts();
        LoadSettings();
        ApplySettings();
        _loading = false;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            var cur = AppSettings.Load();
            if (cur.ThemeMode == "auto")
            {
                bool dark = IsSystemDarkMode();
                bool curDark = (Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark;
                if (dark != curDark)
                {
                    ApplyTheme("auto");
                    ApplyBackdrop(cur.Backdrop);
                }
            }
        };
        timer.Start();
    }

    private bool TrySetupMicaBackdrop(bool useMicaAlt)
    {
        _micaController?.Dispose();
        _micaController = null;
        _backdropConfig = null;

        if (!MicaController.IsSupported()) return false;

        DispatcherQueue.EnsureSystemDispatcherQueue();

        _backdropConfig = new SystemBackdropConfiguration();
        _backdropConfig.IsInputActive = true;
        SetBackdropConfigTheme();

        _micaController = new MicaController();
        _micaController.Kind = useMicaAlt ? MicaKind.BaseAlt : MicaKind.Base;
        _micaController.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
        _micaController.SetSystemBackdropConfiguration(_backdropConfig);
        return true;
    }

    private bool TrySetAcrylicBackdrop(bool useAcrylicThin)
    {
        _acrylicController?.Dispose();
        _acrylicController = null;
        if (!DesktopAcrylicController.IsSupported()) return false;

        DispatcherQueue.EnsureSystemDispatcherQueue();
        _backdropConfig ??= new SystemBackdropConfiguration();
        _backdropConfig.IsInputActive = true;
        SetBackdropConfigTheme();

        _acrylicController = new DesktopAcrylicController();
        _acrylicController.Kind = useAcrylicThin ? DesktopAcrylicKind.Thin : DesktopAcrylicKind.Base;
        _acrylicController.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
        _acrylicController.SetSystemBackdropConfiguration(_backdropConfig);
        return true;
    }

    private void SetBackdropConfigTheme()
    {
        if (_backdropConfig == null) return;
        _backdropConfig.Theme = (Content as FrameworkElement)?.ActualTheme switch
        {
            ElementTheme.Dark => SystemBackdropTheme.Dark,
            ElementTheme.Light => SystemBackdropTheme.Light,
            _ => SystemBackdropTheme.Default
        };
    }

    private void UpdateTitleBarButtons()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            if (appWindow == null) return;
            bool dark = (Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark;
            var fg = dark ? Colors.White : Colors.Black;
            appWindow.TitleBar.ButtonForegroundColor = fg;
            appWindow.TitleBar.ButtonHoverForegroundColor = fg;
            appWindow.TitleBar.ButtonPressedForegroundColor = fg;
        }
        catch { }
    }

    private void ApplySettings()
    {
        var s = AppSettings.Load();
        CpuToggle.IsOn = s.ShowCpu;
        GpuToggle.IsOn = s.ShowGpu;
        NetworkToggle.IsOn = s.ShowNetwork;
        MemoryToggle.IsOn = s.ShowMemory;

        ThemeModeCombo.SelectedIndex = s.ThemeMode switch { "light" => 1, "dark" => 2, _ => 0 };
        BackdropCombo.SelectedItem = string.IsNullOrEmpty(s.Backdrop) ? "Mica" : s.Backdrop;
        LabelFontCombo.SelectedItem = s.LabelFont;
        ValueFontCombo.SelectedItem = s.ValueFont;

        int posIdx = s.Position switch { "left" => 0, "center" => 1, _ => 2 };
        PositionRadio.SelectedIndex = posIdx;

        ApplyTheme(s.ThemeMode);
        ApplyBackdrop(s.Backdrop);
    }

    private void ApplyTheme(string mode)
    {
        try
        {
            bool dark = mode switch
            {
                "light" => false,
                "dark" => true,
                _ => IsSystemDarkMode()
            };
            if (Content is FrameworkElement r)
                r.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
            SetBackdropConfigTheme();
            UpdateTitleBarButtons();
        }
        catch { }
    }

    private void ApplyBackdrop(string? name)
    {
        try
        {
            _micaController?.Dispose();
            _micaController = null;
            _backdropConfig = null;
            SystemBackdrop = null;

            if (Content is not FrameworkElement fe) return;
            bool isDark = fe.ActualTheme == ElementTheme.Dark;

            switch (name)
            {
                case "Mica":
                    if (!TrySetupMicaBackdrop(false))
                        goto default;
                    if (Content is Control c0) c0.Background = null;
                    break;
                case "Mica Alt":
                    if (!TrySetupMicaBackdrop(true))
                        goto default;
                    if (Content is Control c1) c1.Background = null;
                    break;
                case "Acrylic Thin":
                    if (!DesktopAcrylicController.IsSupported())
                        goto default;
                    TrySetAcrylicBackdrop(true);
                    if (Content is Control c2b) c2b.Background = null;
                    break;
                case "Acrylic":
                    if (!DesktopAcrylicController.IsSupported())
                        goto default;
                    TrySetAcrylicBackdrop(false);
                    if (Content is Control c2) c2.Background = null;
                    break;
                default:
                    if (Content is Control c3)
                        c3.Background = new SolidColorBrush(
                            isDark ? Windows.UI.Color.FromArgb(255, 32, 32, 32)
                                   : Windows.UI.Color.FromArgb(255, 243, 243, 243));
                    break;
            }
        }
        catch { }
    }

    private void OnCpuToggled(object sender, RoutedEventArgs e)
    {
        var s = AppSettings.Load(); s.ShowCpu = CpuToggle.IsOn; s.Save();
    }

    private void OnGpuToggled(object sender, RoutedEventArgs e)
    {
        var s = AppSettings.Load(); s.ShowGpu = GpuToggle.IsOn; s.Save();
    }

    private void OnNetworkToggled(object sender, RoutedEventArgs e)
    {
        var s = AppSettings.Load(); s.ShowNetwork = NetworkToggle.IsOn; s.Save();
    }

    private void OnMemoryToggled(object sender, RoutedEventArgs e)
    {
        var s = AppSettings.Load(); s.ShowMemory = MemoryToggle.IsOn; s.Save();
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (ThemeModeCombo.SelectedItem is string selected)
        {
            var mode = selected switch
            {
                "Light" => "light",
                "Dark" => "dark",
                _ => "auto"
            };
            var s = AppSettings.Load(); s.ThemeMode = mode; s.Save();
            ApplyTheme(mode);
            ApplyBackdrop(s.Backdrop);
        }
    }

    private void OnBackdropChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (BackdropCombo.SelectedItem is string selected)
        {
            var s = AppSettings.Load(); s.Backdrop = selected; s.Save();
            ApplyBackdrop(selected);
        }
    }

    private void OnPositionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var pos = PositionRadio.SelectedIndex switch { 0 => "left", 1 => "center", _ => "right" };
        var s = AppSettings.Load(); s.Position = pos; s.Save();
    }

    private static bool IsSystemDarkMode()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize", false);
            if (key?.GetValue("AppsUseLightTheme") is int i) return i == 0;
        }
        catch { }
        return true;
    }

    private void LoadSystemFonts()
    {
        try
        {
            using var fonts = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts", false);
            if (fonts == null) return;
            var names = new System.Collections.Generic.HashSet<string>();
            foreach (var valName in fonts.GetValueNames())
            {
                var name = valName;
                int idx = valName.IndexOf('(');
                if (idx > 0) name = valName.Substring(0, idx).Trim();
                if (!string.IsNullOrEmpty(name) && name.Length > 1) names.Add(name);
            }
            var sorted = names.OrderBy(n => n).ToList();
            LabelFontCombo.ItemsSource = sorted;
            ValueFontCombo.ItemsSource = sorted;
        }
        catch { }
    }

    private void LoadSettings()
    {
        var s = AppSettings.Load();
        CpuToggle.IsOn = s.ShowCpu;
        GpuToggle.IsOn = s.ShowGpu;
        NetworkToggle.IsOn = s.ShowNetwork;
        MemoryToggle.IsOn = s.ShowMemory;
        ThemeModeCombo.SelectedIndex = s.ThemeMode switch { "light" => 1, "dark" => 2, _ => 0 };
        BackdropCombo.SelectedItem = string.IsNullOrEmpty(s.Backdrop) ? "Mica" : s.Backdrop;
        LabelFontCombo.SelectedItem = s.LabelFont;
        ValueFontCombo.SelectedItem = s.ValueFont;

        int posIdx = s.Position switch { "left" => 0, "center" => 1, _ => 2 };
        PositionRadio.SelectedIndex = posIdx;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}


