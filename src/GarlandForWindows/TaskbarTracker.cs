using System.Runtime.InteropServices;
namespace GarlandForWindows;

internal static class TaskbarTracker
{
    internal static bool TryGetOverlayBounds(out Native.Rect rect)
    {
        rect = default;
        IntPtr h = Native.FindWindow("Shell_TrayWnd", null);
        var data = new Native.AppBarData { Size = Marshal.SizeOf<Native.AppBarData>(), Hwnd = h };
        var monitor = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
        if (h == IntPtr.Zero || !Native.IsWindowVisible(h) || Native.SHAppBarMessage(Native.ABM_GETTASKBARPOS, ref data) == UIntPtr.Zero || data.Edge != 3 ||
            !Native.GetWindowRect(h, out var actual) || !Native.GetMonitorInfo(Native.MonitorFromWindow(h, 2), ref monitor)) return false;
        if (actual.Top >= monitor.Monitor.Bottom - 2 || actual.Height < 10 || actual.Top > data.Rect.Top + 4) return false;
        uint dpi = Native.GetDpiForWindow(h);
        rect = new Native.Rect { Left = actual.Left, Right = actual.Right, Top = actual.Top - VisibilityPolicy.DipToPixels(16, dpi), Bottom = actual.Top + VisibilityPolicy.DipToPixels(20, dpi) };
        return true;
    }
}
