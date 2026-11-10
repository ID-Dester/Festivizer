using System.Runtime.InteropServices;
using System.Text;

namespace GarlandForWindows;

internal static class Native
{
    internal const int GWL_EXSTYLE = -20, GWL_STYLE = -16;
    internal const long WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8, WS_EX_NOACTIVATE = 0x08000000;
    internal const long WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000;
    internal const int SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_NOOWNERZORDER = 0x200;
    internal const uint GW_HWNDPREV = 3, GW_OWNER = 4;
    internal const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;
    internal const uint ABM_GETTASKBARPOS = 5, WINEVENT_OUTOFCONTEXT = 0;
    internal const uint EVENT_SYSTEM_FOREGROUND = 0x0003, EVENT_SYSTEM_MOVESIZESTART = 0x000A, EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    internal const uint EVENT_SYSTEM_MENUSTART = 0x0004, EVENT_SYSTEM_MENUEND = 0x0005, EVENT_SYSTEM_MENUPOPUPSTART = 0x0006, EVENT_SYSTEM_MENUPOPUPEND = 0x0007;
    internal const uint EVENT_OBJECT_CREATE = 0x8000, EVENT_OBJECT_HIDE = 0x8003, EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    internal const int OBJID_WINDOW = 0, OBJID_SYSMENU = -1, OBJID_MENU = -3, CHILDID_SELF = 0;
    internal const uint GUI_INMENUMODE = 0x4, GUI_SYSTEMMENUMODE = 0x8, GUI_POPUPMENUMODE = 0x10;
    internal static readonly IntPtr HWND_TOP = IntPtr.Zero;
    internal delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int objectId, int childId, uint thread, uint time);
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; public readonly int Width => Right - Left; public readonly int Height => Bottom - Top; }
    [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] internal struct AppBarData { public int Size; public IntPtr Hwnd; public uint Callback, Edge; public Rect Rect; public IntPtr Param; }
    [StructLayout(LayoutKind.Sequential)] internal struct GuiThreadInfo { public int Size; public uint Flags; public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret; public Rect CaretRect; }
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW", SetLastError=true)] private static extern IntPtr GetWindowLongPtrNative(IntPtr h, int index);
    [DllImport("user32.dll", EntryPoint="SetWindowLongPtrW", SetLastError=true)] private static extern IntPtr SetWindowLongPtrNative(IntPtr h, int index, IntPtr value);
    internal static bool TryGetWindowLongPtr(IntPtr h, int index, out long value) { Marshal.SetLastPInvokeError(0); IntPtr result = GetWindowLongPtrNative(h, index); int error = Marshal.GetLastPInvokeError(); value = result.ToInt64(); return result != IntPtr.Zero || error == 0; }
    internal static bool TrySetWindowLongPtr(IntPtr h, int index, long value) { Marshal.SetLastPInvokeError(0); IntPtr result = SetWindowLongPtrNative(h, index, new IntPtr(value)); return result != IntPtr.Zero || Marshal.GetLastPInvokeError() == 0; }
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr h, out Rect rect);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr h, int attr, out Rect rect, int size);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
    [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern IntPtr FindWindow(string className, string? title);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern int GetClassName(IntPtr h, StringBuilder name, int max);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr h, out uint process);
    [DllImport("user32.dll", SetLastError=true)] internal static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll", SetLastError=true)] internal static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int width, int height, int flags);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] internal static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string text);
    [DllImport("shell32.dll")] internal static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);
    internal static bool Bounds(IntPtr h, out Rect r) => Bounds(h, out r, out _);
    internal static bool Bounds(IntPtr h, out Rect r, out string source)
    {
        int result = DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf<Rect>());
        if (result == 0) { source = "DWM"; return true; }
        Diagnostics.Error("bounds-fallback", $"DWM bounds failed: 0x{result:X8}; using GetWindowRect fallback");
        bool ok = GetWindowRect(h, out r); source = ok ? $"GetWindowRect(dwm=0x{result:X8})" : $"unavailable(dwm=0x{result:X8})"; return ok;
    }
    internal static string ClassName(IntPtr h) { var b = new StringBuilder(256); GetClassName(h, b, b.Capacity); return b.ToString(); }
}
