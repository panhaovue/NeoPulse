using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using WinRT;

namespace TaskbarMonitor.Settings;

public sealed partial class WidgetWindow : Window
{
    private SystemMonitor _monitor = new();
    private IntPtr _hwnd;
    private DispatcherTimer? _timer;
    private AppSettings _settings = AppSettings.Load();
    private TrayIconManager? _tray;
    private FileSystemWatcher? _watcher;
    private DateTime _lastSettingsWrite = DateTime.MinValue;
    private double _cpuTarget, _gpuTarget, _memTarget;
    private double _cpuCurrent, _gpuCurrent, _memCurrent;
    private DispatcherTimer? _animTimer;
    private bool _isDark;
    private DispatcherTimer? _themeTimer;
    private Window? _settingsWindow;
    private SUBCLASSPROC? _subclassDelegate;
    private string? _currentBackdrop;

    private MicaController? _micaController;
    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _backdropConfig;

    public WidgetWindow()
    {
        InitializeComponent();

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _hwnd = hwnd;        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonForegroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonHoverBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonHoverForegroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonPressedBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonPressedForegroundColor = Colors.Transparent;

        if (Content is FrameworkElement fe)
        {
            fe.ActualThemeChanged += (_, _) =>
            {
                SetBackdropConfigTheme();
                UpdateTrackColors();
            };
        }

        _isDark = AppSettings.IsSystemDarkMode();
        ApplyTheme(_settings.ThemeMode);
        ApplyBackdrop(_settings.Backdrop);

        _themeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _themeTimer.Tick += (s, e) =>
        {
            var fresh = AppSettings.Load();
            string mode = fresh.ThemeMode;
            bool dark = mode switch { "light" => false, "dark" => true, _ => AppSettings.IsSystemDarkMode() };
            string prevBackdrop = _settings?.Backdrop ?? "Mica";
            if (dark != _isDark || fresh.Backdrop != prevBackdrop)
            {
                _isDark = dark;
                ApplyTheme(mode);
                ApplyBackdrop(fresh.Backdrop);
                UpdateTrackColors();
            }
        };
        _themeTimer.Start();

        const int GWL_STYLE = -16;
        const int GWL_EXSTYLE = -20;
        SetWindowLong(hwnd, GWL_STYLE, 0x00CF0000);
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | 0x00000080);

        int cornerPref = 2;
        DwmSetWindowAttribute(hwnd, 33, ref cornerPref, 4);
        var margins = new MARGINS { left = 0, right = 0, top = 0, bottom = 0 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0027);

        RepositionWidget();

        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);

        _subclassDelegate = TrayWndProc;
        SetWindowSubclass(hwnd, _subclassDelegate, 1, 0);

        _tray = new TrayIconManager(hwnd,
            onSettings: () => OpenSettings(),
            onExit: () => { _tray?.Dispose(); Application.Current.Exit(); }
        );

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (s, e) => UpdateStats();
        _timer.Start();

        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _animTimer.Tick += (s, e) => AnimateBars();
        _animTimer.Start();

        UpdateStats();
        SetupSettingsWatcher();

        Closed += (s, e) => {
            if (_subclassDelegate != null) try { RemoveWindowSubclass(hwnd, _subclassDelegate, 1); } catch { }
            _micaController?.Dispose();
            _acrylicController?.Dispose();
            _tray?.Dispose();
            _watcher?.Dispose();
            _animTimer?.Stop();
            _themeTimer?.Stop();
        };
    }

    private void RepositionWidget()
    {
        uint dpi = GetDpiForWindow(_hwnd);
        float scale = dpi / 96.0f;
        int screenW = GetSystemMetrics(0);
        int screenH = GetSystemMetrics(1);
        int panelW = (int)(430 * scale);
        int panelH = (int)(55 * scale);

        int x = _settings.Position switch
        {
            "left" => (int)(100 * scale),
            "center" => (screenW - panelW) / 2,
            _ => screenW - panelW - (int)(200 * scale),
        };
        int y = screenH - panelH;

        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        AppWindow.GetFromWindowId(windowId).MoveAndResize(new RectInt32(x, y, panelW, panelH));
    }

    private bool TrySetupMicaBackdrop(bool useMicaAlt)
    {
        _micaController?.Dispose();
        _micaController = null;
        if (!MicaController.IsSupported()) return false;

        DispatcherQueue.EnsureSystemDispatcherQueue();
        _backdropConfig ??= new SystemBackdropConfiguration();
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

    private void ApplyBackdrop(string? name)
    {
        try
        {
            _micaController?.Dispose();
            _micaController = null;
            _acrylicController?.Dispose();
            _acrylicController = null;
            _backdropConfig = null;
            SystemBackdrop = null;

            if (Content is not FrameworkElement fe) return;
            bool isDark = fe.ActualTheme == ElementTheme.Dark;

            switch (name)
            {
                case "Mica":
                    if (!TrySetupMicaBackdrop(false))
                        goto default;
                    if (Content is Grid g0) g0.Background = null;
                    break;
                case "Mica Alt":
                    if (!TrySetupMicaBackdrop(true))
                        goto default;
                    if (Content is Grid g1) g1.Background = null;
                    break;
                case "Acrylic":
                    if (!TrySetAcrylicBackdrop(false))
                        goto default;
                    if (Content is Grid g2) g2.Background = null;
                    break;
                case "Acrylic Thin":
                    if (!TrySetAcrylicBackdrop(true))
                        goto default;
                    if (Content is Grid g3) g3.Background = null;
                    break;
                default:
                    var bg = isDark
                        ? Windows.UI.Color.FromArgb(255, 32, 32, 32)
                        : Windows.UI.Color.FromArgb(255, 243, 243, 243);
                    if (Content is Grid g4)
                        g4.Background = new SolidColorBrush(bg);
                    break;
            }
        }
        catch { }
    }

    private void ApplyTheme(string mode)
    {
        bool dark = mode switch
        {
            "light" => false,
            "dark" => true,
            _ => AppSettings.IsSystemDarkMode()
        };
        _isDark = dark;
        if (Content is FrameworkElement fe)
        {
            fe.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
            SetBackdropConfigTheme();
            UpdateTrackColors();
        }
    }

    private void UpdateTrackColors()
    {
        bool dark = (Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark;
        var track = dark
            ? Windows.UI.Color.FromArgb(80, 255, 255, 255)
            : Windows.UI.Color.FromArgb(80, 0, 0, 0);
        var brush = new SolidColorBrush(track);
        CpuBarGrid.Background = brush;
        GpuBarGrid.Background = brush;
        MemBarGrid.Background = brush;
    }

    private void AnimateBars()
    {
        const double lerp = 0.2;
        _cpuCurrent += (_cpuTarget - _cpuCurrent) * lerp;
        _gpuCurrent += (_gpuTarget - _gpuCurrent) * lerp;
        _memCurrent += (_memTarget - _memCurrent) * lerp;
        if (Math.Abs(_cpuCurrent - _cpuTarget) > 0.1) CpuBarFill.Width = _cpuCurrent;
        if (Math.Abs(_gpuCurrent - _gpuTarget) > 0.1) GpuBarFill.Width = _gpuCurrent;
        if (Math.Abs(_memCurrent - _memTarget) > 0.1) MemBarFill.Width = _memCurrent;
    }

    private void SetupSettingsWatcher()
    {
        try
        {
            _watcher = new FileSystemWatcher(
                Path.GetDirectoryName(AppSettings.SettingsPath)!,
                Path.GetFileName(AppSettings.SettingsPath))
            {
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.LastWrite
            };
            _watcher.Changed += (s, e) =>
            {
                try
                {
                    if ((DateTime.UtcNow - _lastSettingsWrite).TotalMilliseconds < 200) return;
                    _lastSettingsWrite = DateTime.UtcNow;
                    var fresh = AppSettings.Load();
                    _settings = fresh;
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        ApplyBackdrop(fresh.Backdrop);
                        if (UiNetworkCard != null) UiNetworkCard.Visibility = fresh.ShowNetwork ? Visibility.Visible : Visibility.Collapsed;
                        if (UiCpuCard != null) UiCpuCard.Visibility = fresh.ShowCpu ? Visibility.Visible : Visibility.Collapsed;
                        if (UiGpuCard != null) UiGpuCard.Visibility = fresh.ShowGpu ? Visibility.Visible : Visibility.Collapsed;
                        if (UiMemCard != null) UiMemCard.Visibility = fresh.ShowMemory ? Visibility.Visible : Visibility.Collapsed;
                        RepositionWidget();
                    });
                }
                catch { }
            };
        }
        catch { }
    }

    private void UpdateStats()
    {
        _monitor.Update();
        var settings = AppSettings.Load();

        UploadText.Text = FormatBytes(_monitor.UploadSpeed) + "/s";
        DownloadText.Text = FormatBytes(_monitor.DownloadSpeed) + "/s";

        int cpuPct = (int)Math.Round(_monitor.CpuUsage);
        double cpuW = (UiCpuCard?.ActualWidth ?? 105) - 12;
        _cpuTarget = cpuPct * cpuW / 100.0;
        CpuText.Text = $"{cpuPct}%";

        double cpuTemp = _monitor.CpuTemp;
        CpuTempText.Text = cpuTemp > 0 ? $"{cpuTemp:F0}°C" : "--°C";

        int gpuPct = (int)Math.Round(_monitor.GpuUsage);
        double gpuW = (UiGpuCard?.ActualWidth ?? 105) - 12;
        _gpuTarget = gpuPct * gpuW / 100.0;
        GpuText.Text = $"{gpuPct}%";

        double gpuTemp = _monitor.GpuTemp;
        GpuTempText.Text = gpuTemp > 0 ? $"{gpuTemp:F0}°C" : "--°C";

        int gpuPower = _monitor.GpuPowerMw;
        GpuPowerText.Text = gpuPower > 0 ? $"{gpuPower / 1000.0:F1}W" : "--W";

        double memUsed = (double)_monitor.MemoryUsedBytes;
        double memTotal = (double)_monitor.MemoryTotalBytes;
        double memPct = memTotal > 0 ? memUsed / memTotal : 0;
        int memPctInt = (int)Math.Round(memPct * 100);
        double memW = (UiMemCard?.ActualWidth ?? 105) - 12;
        _memTarget = memPctInt * memW / 100.0;
        MemText.Text = FormatBytes(memUsed);
        MemPercentText.Text = $"{memPctInt}%";
    }

    private static string FormatBytes(double bytes)
    {
        if (bytes < 0) return "0 B";
        return bytes switch
        {
            >= 1_000_000_000 => $"{bytes / 1_000_000_000:F1} GB",
            >= 1_000_000 => $"{bytes / 1_000_000:F1} MB",
            >= 1_000 => $"{bytes / 1_000:F1} KB",
            _ => $"{bytes:F0} B"
        };
    }

    private void OpenSettings()
    {
        try
        {
            if (_settingsWindow != null)
            {
                try { _settingsWindow.Activate(); } catch { }
                return;
            }
            _settingsWindow = new MainWindow();
            _settingsWindow.Closed += (s, ev) => _settingsWindow = null;
            _settingsWindow.Activate();
        }
        catch { }
    }

    private IntPtr TrayWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr uId, IntPtr dwRefData)
    {
        if (msg == TrayIconManager.WM_OPEN_SETTINGS) { OpenSettings(); return new IntPtr(1); }
        if (msg == TrayIconManager.WM_EXIT) { _tray?.Dispose(); Application.Current.Exit(); return new IntPtr(1); }
        if (_tray != null && _tray.HandleWndProc(msg, wParam, lParam))
        {
            return new IntPtr(1);
        }
        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        ContextPopup.IsOpen = false;
        TrayIconManager.GetCursorPos(out TrayIconManager.POINT pt);
        bool dark = (Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark;
        var bgColor = dark ? Windows.UI.Color.FromArgb(255, 32, 32, 32) : Windows.UI.Color.FromArgb(255, 243, 243, 243);
        var borderColor = dark ? Windows.UI.Color.FromArgb(60, 255, 255, 255) : Windows.UI.Color.FromArgb(40, 0, 0, 0);
        var fgColor = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        ContextMenuBorder.Background = new SolidColorBrush(bgColor);
        ContextMenuBorder.BorderBrush = new SolidColorBrush(borderColor);
        CtxSettingsBtn.Foreground = new SolidColorBrush(fgColor);
        CtxExitBtn.Foreground = new SolidColorBrush(fgColor);
        var scale = Content?.XamlRoot?.RasterizationScale ?? 1.0;
        ContextPopup.HorizontalOffset = pt.X / scale;
        ContextPopup.VerticalOffset = pt.Y / scale;
        ContextPopup.IsOpen = true;
    }

    private void OnCtxSettingsClicked(object sender, RoutedEventArgs e)
    {
        ContextPopup.IsOpen = false;
        OpenSettings();
    }

    private void OnCtxExitClicked(object sender, RoutedEventArgs e)
    {
        ContextPopup.IsOpen = false;
        _tray?.Dispose();
        Application.Current.Exit();
    }

    private void OnContextMenuClosed(object sender, object e)
    {
    }

    private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, uint uIdSubclass, uint dwRefData);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, uint uIdSubclass);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int left, right, top, bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
}





