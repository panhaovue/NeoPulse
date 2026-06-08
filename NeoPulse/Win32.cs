using System.Runtime.InteropServices;

namespace NeoPulse;

internal static partial class Win32
{
    [LibraryImport("user32.dll")] internal static partial int GetSystemMetrics(int nIndex);
    [LibraryImport("user32.dll")] internal static partial uint GetDpiForWindow(IntPtr hwnd);
    [LibraryImport("user32.dll")] internal static partial int SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static partial IntPtr GetWindowLong(IntPtr hWnd, int nIndex);
    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static partial IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] internal static partial IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);
    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")] internal static partial IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [LibraryImport("user32.dll")] internal static partial int GetCursorPos(out POINT lpPoint);
    [LibraryImport("user32.dll")] internal static partial IntPtr GetForegroundWindow();
    [LibraryImport("user32.dll")] internal static partial int GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [LibraryImport("dwmapi.dll")] internal static partial int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);
    [LibraryImport("dwmapi.dll")] internal static partial int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct MARGINS { public int left, right, top, bottom; }

    internal static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOZORDER = 0x0004;
}
