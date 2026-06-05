using System.Runtime.InteropServices;

namespace TaskbarMonitor.Settings;

internal sealed class TrayIconManager : IDisposable
{
    private const int WM_TRAYICON = 0x8001;
    private const int WM_RBUTTONUP = 0x0205;
    private const int ID_SETTINGS = 1001;
    private const int ID_EXIT = 1002;
    public const int WM_OPEN_SETTINGS = WM_TRAYICON + 1;
    public const int WM_EXIT = WM_TRAYICON + 2;

    private const int NIM_ADD = 0x00;
    private const int NIM_DELETE = 0x02;
    private const int NIF_MESSAGE = 0x01;
    private const int NIF_ICON = 0x02;
    private const int NIF_TIP = 0x04;

    private readonly IntPtr _hwnd;
    private NOTIFYICONDATA _nid;
    private bool _disposed;
    private readonly Action _onSettings;
    private readonly Action _onExit;

    public TrayIconManager(IntPtr hwnd, Action onSettings, Action onExit)
    {
        _hwnd = hwnd;
        _onSettings = onSettings;
        _onExit = onExit;
        AddTrayIcon();
    }

    private void AddTrayIcon()
    {
        _nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = LoadIcon(IntPtr.Zero, (IntPtr)32512),
            szTip = "Taskbar Monitor",
        };
        Shell_NotifyIconW(NIM_ADD, ref _nid);
    }

    public bool HandleWndProc(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAYICON && (uint)lParam == WM_RBUTTONUP)
        {
            ShowContextMenuViaWin32();
            return true;
        }
        return false;
    }

    private void ShowContextMenuViaWin32()
    {
        GetCursorPos(out POINT pt);
        var hMenu = CreatePopupMenu();
        AppendMenuW(hMenu, 0, ID_SETTINGS, "Settings");
        AppendMenuW(hMenu, 0, ID_EXIT, "Exit");
        SetForegroundWindow(_hwnd);
        int cmd = TrackPopupMenuEx(hMenu, 0x0100, pt.X, pt.Y, _hwnd, IntPtr.Zero);
        if (cmd == ID_SETTINGS) { PostMessage(_hwnd, WM_TRAYICON + 1, IntPtr.Zero, IntPtr.Zero); }
        else if (cmd == ID_EXIT) { PostMessage(_hwnd, WM_TRAYICON + 2, IntPtr.Zero, IntPtr.Zero); }
        DestroyMenu(hMenu);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Shell_NotifyIconW(NIM_DELETE, ref _nid);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize; public IntPtr hWnd; public uint uID; public uint uFlags;
        public uint uCallbackMessage; public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState; public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon;
    }

    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, int uIDNewItem, string lpNewItem);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lptpm);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr hMenu);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATA lpData);
    [DllImport("user32.dll")] private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
}
