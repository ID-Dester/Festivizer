using System.Runtime.InteropServices;

namespace GarlandForWindows;

internal readonly record struct WindowSnapshot(IntPtr Handle, Native.Rect Bounds, bool Fullscreen, bool Eligible, uint ProcessId = 0);

internal static class WindowEligibilityPolicy
{
    internal static bool IsEligible(bool hasBounds, bool hasStyle, bool maximized, bool root, bool owned, long exStyle, int width, int height) =>
        hasBounds && hasStyle && !maximized && root && !owned &&
        (exStyle & (Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST)) == 0 && width >= 150 && height >= 100;
}

internal static class WindowTracker
{
    internal static WindowSnapshot Read(IntPtr h)
    {
        if (h == IntPtr.Zero || !Native.IsWindow(h) || !Native.IsWindowVisible(h) || Native.IsIconic(h)) return new(h, default, false, false);
        uint thread = Native.GetWindowThreadProcessId(h, out uint pid);
        if (thread == 0) return new(h, default, true, false);
        if (pid == Environment.ProcessId || IsShellSurface(Native.ClassName(h))) return new(h, default, false, false);
        int cloakedResult = Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out int cloaked, sizeof(int));
        if (cloakedResult != 0) { Diagnostics.Error("cloaked", $"DWM cloaked query failed: 0x{cloakedResult:X8}"); return new(h, default, true, false); }
        if (cloaked != 0) return new(h, default, false, false);
        bool hasBounds = Native.Bounds(h, out var bounds, out string boundsSource);
        var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
        bool hasMonitor = Native.GetMonitorInfo(Native.MonitorFromWindow(h, 2), ref info);
        bool hasNormalStyle = Native.TryGetWindowLongPtr(h, Native.GWL_STYLE, out long normalStyle);
        bool zoomed = Native.IsZoomed(h);
        string? processName = null; bool processIdentityUnknown = false;
        if (hasBounds && hasMonitor && (info.Flags & 1) != 0 && hasNormalStyle && !zoomed && normalStyle == 0x96CA0000L && GeometryPolicy.CoversMonitor(bounds, info.Monitor)) {
            try { using var process = System.Diagnostics.Process.GetProcessById((int)pid); processName = process.ProcessName; }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { processIdentityUnknown = true; Diagnostics.Error("fullscreen-process", $"Fullscreen process identity unavailable pid={pid}: {e.Message}"); }
        }
        bool cs2Fullscreen = GeometryPolicy.IsObservedCs2Fullscreen(bounds, info.Monitor, normalStyle, zoomed, processName);
        bool fullscreen = !hasBounds || !hasMonitor || ((info.Flags & 1) != 0 && (!hasNormalStyle || processIdentityUnknown || GeometryPolicy.IsFullscreen(bounds, info.Monitor, normalStyle) || cs2Fullscreen));
        bool hasStyle = Native.TryGetWindowLongPtr(h, Native.GWL_EXSTYLE, out long style);
        bool eligible = WindowEligibilityPolicy.IsEligible(hasBounds, hasStyle, zoomed, Native.GetAncestor(h, 2) == h,
            Native.GetWindow(h, 4) != IntPtr.Zero, style, bounds.Width, bounds.Height);
        static string Rect(Native.Rect r) => $"{r.Left},{r.Top},{r.Right},{r.Bottom}";
        Diagnostics.State("foreground-facts", $"ForegroundFacts hwnd=0x{h.ToInt64():X} pid={pid} thread={thread} boundsOk={hasBounds} boundsSource={boundsSource} bounds={Rect(bounds)} monitorOk={hasMonitor} monitor={Rect(info.Monitor)} work={Rect(info.Work)} primary={(info.Flags & 1) != 0} dpi={Native.GetDpiForWindow(h)} styleOk={hasNormalStyle} style=0x{normalStyle:X} exStyleOk={hasStyle} exStyle=0x{style:X} zoomed={zoomed} topmost={(style & Native.WS_EX_TOPMOST) != 0} cloakResult={cloakedResult} cloaked={cloaked} cs2Rule={cs2Fullscreen} fullscreen={fullscreen} eligible={eligible}");
        return new(h, bounds, fullscreen, eligible, pid);
    }
    private static bool IsShellSurface(string name) => name is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW" or "XamlExplorerHostIslandWindow" or "NotifyIconOverflowWindow";
}
