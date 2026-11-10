namespace GarlandForWindows;

internal static class GeometryPolicy
{
    internal static bool IsFullscreen(Native.Rect bounds, Native.Rect monitor, long style) =>
        (style & (Native.WS_CAPTION | Native.WS_THICKFRAME)) == 0 &&
        CoversMonitor(bounds, monitor);

    internal static bool CoversMonitor(Native.Rect bounds, Native.Rect monitor) =>
        Math.Abs((long)bounds.Left - monitor.Left) <= 2 && Math.Abs((long)bounds.Top - monitor.Top) <= 2 &&
        Math.Abs((long)bounds.Right - monitor.Right) <= 2 && Math.Abs((long)bounds.Bottom - monitor.Bottom) <= 2;

    // CS2 was observed with this exact framed style while its content covered the monitor.
    // Keep the exception constrained to the observed process, style and window state.
    internal static bool IsObservedCs2Fullscreen(Native.Rect bounds, Native.Rect monitor, long style, bool zoomed, string? processName) =>
        !zoomed && style == 0x96CA0000L && string.Equals(processName, "cs2", StringComparison.OrdinalIgnoreCase) && CoversMonitor(bounds, monitor);
}
