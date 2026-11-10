using GarlandForWindows;

internal static class NativeMenu
{
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "GetUserObjectInformationW")] private static extern bool GetUserObjectInformation(IntPtr handle, int index, System.Text.StringBuilder value, int length, out int needed);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    internal static bool HasDefaultInputDesktop()
    {
        IntPtr desktop = OpenInputDesktop(0, false, 1);
        if (desktop == IntPtr.Zero) return false;
        try { var name = new System.Text.StringBuilder(256); return GetUserObjectInformation(desktop, 2, name, name.Capacity * 2, out _) && string.Equals(name.ToString(), "Default", StringComparison.OrdinalIgnoreCase); }
        finally { CloseDesktop(desktop); }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] internal static extern IntPtr CreatePopupMenu();
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "AppendMenuW")] internal static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string text);
    [System.Runtime.InteropServices.DllImport("user32.dll")] internal static extern bool TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr owner, IntPtr rect);
    [System.Runtime.InteropServices.DllImport("user32.dll")] internal static extern bool EndMenu();
    [System.Runtime.InteropServices.DllImport("user32.dll")] internal static extern bool DestroyMenu(IntPtr menu);
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    internal static bool PostCancelMode(IntPtr target) => PostMessage(target, 0x001F, IntPtr.Zero, IntPtr.Zero);
}

