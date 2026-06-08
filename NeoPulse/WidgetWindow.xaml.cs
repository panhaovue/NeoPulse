using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using NeoPulse.ViewModels;
using WinRT;

namespace NeoPulse;

public sealed partial class WidgetWindow : Window
{
    private SystemMonitor _monitor = new();
    private IntPtr _hwnd;
    private AppWindow? _appWindow;
    private CancellationTokenSource? _cts;
    private bool _dragging;
    private Win32.POINT _dragStartCursor;
    private PointInt32 _dragStartWindow;
    private DispatcherTimer? _resizeTimer;
    private bool _paused;
    private int _animTargetW, _animTargetH;
    private double _animCurW, _animCurH;
    private string? _currentBackdrop;
    private MicaBackdrop? _micaBackdrop;
    private DesktopAcrylicBackdrop? _acrylicBackdrop;
    private Window? _settingsWindow;
    private readonly WidgetViewModel _viewModel;

    public WidgetWindow()
    {
        InitializeComponent();

        _viewModel = new WidgetViewModel(AppSettings.Instance);
        if (Content is FrameworkElement root)
            root.DataContext = _viewModel;
        AppSettings.Instance.NotifyAll();

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _hwnd = hwnd;
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.TitleBar.ExtendsContentIntoTitleBar = false;

        if (Content is FrameworkElement fe)
        {
            fe.ActualThemeChanged += (_, _) => { };
        }

        SystemEvents.UserPreferenceChanged += OnSystemThemeChanged;

        ApplyTheme(AppSettings.Instance.ThemeMode);
        ApplyBackdrop(AppSettings.Instance.Backdrop);

        AppSettings.Instance.PropertyChanged += OnSettingChanged;
        _viewModel.RefreshSettings();

        const int GWL_STYLE = -16;
        const int GWL_EXSTYLE = -20;
        const int WS_POPUP = unchecked((int)0x80000000);
        Win32.SetWindowLong(hwnd, GWL_STYLE, new IntPtr(WS_POPUP));
        IntPtr exStyle = Win32.GetWindowLong(hwnd, GWL_EXSTYLE);
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const int WS_EX_LAYERED = 0x00080000;
        Win32.SetWindowLong(hwnd, GWL_EXSTYLE, new IntPtr(exStyle.ToInt32() | WS_EX_LAYERED | WS_EX_TOOLWINDOW));

        // Set window icon for taskbar thumbnail
        SetWindowIcon();

        int cornerPref = 2;
        Win32.DwmSetWindowAttribute(hwnd, 33, ref cornerPref, 4);
        var margins = new Win32.MARGINS { left = 0, right = 0, top = 0, bottom = 0 };
        Win32.DwmExtendFrameIntoClientArea(hwnd, ref margins);
        Win32.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0027);

        if (Content is UIElement uroot)
        {
            uroot.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(uroot).Properties.IsLeftButtonPressed)
                {
                    _dragging = true;
                    Win32.GetCursorPos(out _dragStartCursor);
                    var pos = _appWindow.Position;
                    _dragStartWindow = new PointInt32(pos.X, pos.Y);
                    uroot.CapturePointer(e.Pointer);
                }
            };
            uroot.PointerReleased += (_, _) =>
            {
                _dragging = false;
            };
            uroot.PointerMoved += (_, e) =>
            {
                if (!_dragging) return;
                Win32.GetCursorPos(out Win32.POINT cur);
                int dx = cur.X - _dragStartCursor.X;
                int dy = cur.Y - _dragStartCursor.Y;
                _appWindow.Move(new PointInt32(_dragStartWindow.X + dx, _dragStartWindow.Y + dy));
            };
        }

        RepositionWidget();

        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE);

        // Hardware monitoring on background thread
        _cts = new CancellationTokenSource();
        _ = CollectHardwareDataLoop(_cts.Token);

        // Smooth window resize animation (30fps, Composition engine handles the rest)
        _resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _resizeTimer.Tick += (_, _) => { AnimateBars(); AnimateResize(); };
        _resizeTimer.Start();

        Translations.Lang = AppSettings.Instance.Language;
        UpdateContextMenuText();
        UpdateVisibility();

        Closed += (s, e) => {
            SavePosition();
            AppSettings.Instance.PropertyChanged -= OnSettingChanged;
            SystemEvents.UserPreferenceChanged -= OnSystemThemeChanged;
            _viewModel.Dispose();
            _cts?.Cancel();
            _resizeTimer?.Stop();
        };
    }

    private void OnSystemThemeChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General && AppSettings.Instance.ThemeMode == "auto")
        {
            DispatcherQueue.TryEnqueue(() => { ApplyTheme("auto"); });
        }
    }

    private void OnSettingChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.ThemeMode):
            case nameof(AppSettings.Backdrop):
                DispatcherQueue.TryEnqueue(() =>
                {
                    ApplyTheme(AppSettings.Instance.ThemeMode);
                    ApplyBackdrop(AppSettings.Instance.Backdrop);
                });
                break;
            case nameof(AppSettings.ShowCpu):
            case nameof(AppSettings.ShowGpu):
            case nameof(AppSettings.ShowNetwork):
            case nameof(AppSettings.ShowMemory):
            case nameof(AppSettings.Layout):
                DispatcherQueue.TryEnqueue(() =>
                {
                    UpdateVisibility();
                    _viewModel.RefreshSettings();
                });
                break;
            case nameof(AppSettings.Language):
                DispatcherQueue.TryEnqueue(UpdateContextMenuText);
                break;
        }
    }

    private void UpdateVisibility()
    {
        var savedX = _appWindow?.Position.X ?? -1;
        var savedY = _appWindow?.Position.Y ?? -1;

        bool vertical = AppSettings.Instance.Layout == "vertical";
        RootPanel.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        RepositionWidget();

        if (savedX >= 0 && savedY >= 0)
            _appWindow?.Move(new PointInt32(savedX, savedY));
    }

    private void SavePosition()
    {
        try
        {
            if (_appWindow == null) return;
            var pos = _appWindow.Position;
            AppSettings.Instance.LastX = pos.X;
            AppSettings.Instance.LastY = pos.Y;
            AppSettings.Instance.Save();
        }
        catch { }
    }

    private void RepositionWidget()
    {
        uint dpi = Win32.GetDpiForWindow(_hwnd);
        float scale = dpi / 96.0f;

        // Multi-monitor aware work area
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        var da = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(windowId, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
        var wa = da.WorkArea;

        var s = AppSettings.Instance;
        bool vertical = s.Layout == "vertical";
        int visible = (s.ShowNetwork ? 1 : 0) + (s.ShowCpu ? 1 : 0) + (s.ShowGpu ? 1 : 0) + (s.ShowMemory ? 1 : 0);
        if (visible == 0) visible = 1;

        // StackPanel: Padding=4, Spacing=4, card=105x55
        int panelW = vertical ? 113 : visible * 105 + (visible - 1) * 4 + 8;
        int panelH = vertical ? visible * 55 + (visible - 1) * 4 + 8 : 63;

        // First launch: snap immediately. Later: let _resizeTimer animate.
        if (_animCurW == 0)
        {
            _animCurW = panelW;
            _animCurH = panelH;
            uint dpiN = Win32.GetDpiForWindow(_hwnd);
            float sN = dpiN / 96.0f;
            Win32.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0,
                (int)(panelW * sN), (int)(panelH * sN),
                0x0002 | 0x0004);
        }
        _animTargetW = panelW;
        _animTargetH = panelH;

        // First launch: position window. Later: only resize, don't move.
        if (_animCurW == panelW && _animCurH == panelH)
        {
            int x = s.LastX >= 0 ? s.LastX : wa.X + (wa.Width - (int)(panelW * scale)) / 2;
            int y = s.LastY >= 0 ? s.LastY : wa.Y + wa.Height - (int)(panelH * scale);
            _appWindow?.Move(new PointInt32(x, y));
        }
    }

    private void SetWindowIcon()
    {
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("NeoPulse.Assets.app.ico");
            if (stream != null)
            {
                var bytes = new byte[stream.Length];
                stream.ReadExactly(bytes, 0, bytes.Length);
                using var ms = new System.IO.MemoryStream(bytes);
                var icon = new System.Drawing.Icon(ms);
                IntPtr hIcon = icon.Handle;
                Win32.SendMessage(_hwnd, 0x0080, IntPtr.Zero, hIcon);
                Win32.SendMessage(_hwnd, 0x0080, new IntPtr(1), hIcon);
            }
        }
        catch { }
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

    private void ApplyTheme(string mode)
    {
        if (Content is FrameworkElement fe)
        {
            fe.RequestedTheme = mode switch
            {
                "light" => ElementTheme.Light,
                "dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }
    }

    private void AnimateBars()
    {
        const double lerp = 0.2;
        _viewModel.CpuBarWidth += (_viewModel.CpuBarTarget - _viewModel.CpuBarWidth) * lerp;
        _viewModel.GpuBarWidth += (_viewModel.GpuBarTarget - _viewModel.GpuBarWidth) * lerp;
        _viewModel.MemBarWidth += (_viewModel.MemBarTarget - _viewModel.MemBarWidth) * lerp;
    }

    private void AnimateResize()
    {
        if (_animCurW == _animTargetW && _animCurH == _animTargetH) return;

        _animCurW += (_animTargetW - _animCurW) * 0.15;
        _animCurH += (_animTargetH - _animCurH) * 0.15;

        if (Math.Abs(_animCurW - _animTargetW) < 0.5) _animCurW = _animTargetW;
        if (Math.Abs(_animCurH - _animTargetH) < 0.5) _animCurH = _animTargetH;

        uint dpi = Win32.GetDpiForWindow(_hwnd);
        float s = dpi / 96.0f;
        Win32.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0,
            (int)(_animCurW * s), (int)(_animCurH * s),
            0x0002 | 0x0004);
    }

    private void CheckFullscreen()
    {
        var fgHwnd = Win32.GetForegroundWindow();
        if (fgHwnd == IntPtr.Zero || fgHwnd == _hwnd)
        {
            if (_paused) ResumeWidget();
            return;
        }

        Win32.GetWindowRect(fgHwnd, out Win32.RECT rect);
        int screenW = Win32.GetSystemMetrics(0);
        int screenH = Win32.GetSystemMetrics(1);

        bool isFullscreen = rect.Left == 0 && rect.Top == 0
                         && rect.Right == screenW && rect.Bottom == screenH;

        if (isFullscreen && !_paused)
            PauseWidget();
        else if (!isFullscreen && _paused)
            ResumeWidget();
    }

    private void PauseWidget()
    {
        _paused = true;
        _resizeTimer?.Stop();
        _appWindow?.Hide();
        Logger.Info("Fullscreen app detected, widget paused");
    }

    private void ResumeWidget()
    {
        _paused = false;
        _resizeTimer?.Start();
        _appWindow?.Show();
        Logger.Info("Fullscreen app exited, widget resumed");
    }

    private async Task CollectHardwareDataLoop(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(0.5));
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                if (_paused) continue;
                try
                {
                    _monitor.Update();
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Hardware collection: {ex.Message}");
                }
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (_dragging) return;
                    PushStatsToViewModel();
                });
            }
        }
        catch (OperationCanceledException) { }
    }

    private void PushStatsToViewModel()
    {
        _viewModel.UploadText = SystemMonitor.FormatSpeed(_monitor.UploadSpeed);
        _viewModel.DownloadText = SystemMonitor.FormatSpeed(_monitor.DownloadSpeed);

        double cpuW = UiCpuCard?.ActualWidth ?? 0;
        if (cpuW > 12) cpuW -= 12; else cpuW = 80;
        int cpuPct = (int)Math.Round(_monitor.CpuUsage);
        _viewModel.CpuText = $"{cpuPct}%";
        _viewModel.CpuBarTarget = cpuPct * cpuW / 100.0;

        double cpuTemp = _monitor.CpuTemp;
        _viewModel.CpuTempText = cpuTemp > 0 ? $"{cpuTemp:F0}°C" : "--°C";

        int cpuPower = _monitor.CpuPowerMw;
        _viewModel.CpuPowerText = cpuPower > 0 ? $"{cpuPower / 1000:F0}W" : "--W";

        double gpuW = UiGpuCard?.ActualWidth ?? 0;
        if (gpuW > 12) gpuW -= 12; else gpuW = 80;
        int gpuPct = (int)Math.Round(_monitor.GpuUsage);
        _viewModel.GpuText = $"{gpuPct}%";
        _viewModel.GpuBarTarget = gpuPct * gpuW / 100.0;

        double gpuTemp = _monitor.GpuTemp;
        _viewModel.GpuTempText = gpuTemp > 0 ? $"{gpuTemp:F0}°C" : "--°C";

        int gpuPower = _monitor.GpuPowerMw;
        _viewModel.GpuPowerText = gpuPower > 0 ? $"{gpuPower / 1000.0:G}W" : "--W";

        double memUsed = (double)_monitor.MemoryUsedBytes;
        double memTotal = (double)_monitor.MemoryTotalBytes;
        double memPct = memTotal > 0 ? memUsed / memTotal : 0;
        int memPctInt = (int)Math.Round(memPct * 100);
        double memW = UiMemCard?.ActualWidth ?? 0;
        if (memW > 12) memW -= 12; else memW = 80;
        _viewModel.MemText = SystemMonitor.FormatBytes((ulong)memUsed);
        _viewModel.MemPercentText = $"{memPctInt}%";
        _viewModel.MemBarTarget = memPctInt * memW / 100.0;
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

    private void UpdateContextMenuText()
    {
        CtxSettingsItem.Text = Translations.Get("Settings");
        CtxExitItem.Text = Translations.Get("Exit");
    }

    private void OnCtxSettingsClicked(object sender, RoutedEventArgs e)
    {
        OpenSettings();
    }

    private void OnCtxExitClicked(object sender, RoutedEventArgs e)
    {
        Application.Current.Exit();
    }
}
