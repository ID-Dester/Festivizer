namespace GarlandForWindows;

internal static class GeometryPolicy
{
    internal static bool IsFullscreen(Native.Rect bounds, Native.Rect monitor, long style) =>
        (style & (Native.WS_CAPTION | Native.WS_THICKFRAME)) == 0 &&
        Math.Abs((long)bounds.Left - monitor.Left) <= 2 && Math.Abs((long)bounds.Top - monitor.Top) <= 2 &&
        Math.Abs((long)bounds.Right - monitor.Right) <= 2 && Math.Abs((long)bounds.Bottom - monitor.Bottom) <= 2;
}

