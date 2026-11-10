using System.Windows.Threading;
namespace GarlandForWindows;

internal sealed class WpfFrameSurface : IFrameSurface
{
    private readonly GarlandOverlay overlay;
    internal WpfFrameSurface(IntPtr owner) { overlay = new GarlandOverlay(true, owner); overlay.OverlayDpiChanged += () => DpiChanged?.Invoke(); }
    public IntPtr Handle => overlay.Handle;
    public bool IsVisible => overlay.IsVisible;
    public bool HasLiveHandle => overlay.HasLiveHandle;
    public event Action? DpiChanged;
    public void Configure(Settings settings) => overlay.Configure(settings);
    public void Animate(double phase) => overlay.Animate(phase);
    public void Place(Native.Rect bounds, bool preserveZOrder) => overlay.Place(bounds, Native.HWND_TOP, preserveZOrder);
    public void Hide() => overlay.Hide();
    public void Close() => overlay.Close();
}

internal sealed class WpfFrameEnvironment(Dispatcher dispatcher) : IFrameEnvironment
{
    public DateTime UtcNow => DateTime.UtcNow;
    public bool StillCurrent(IntPtr hwnd, uint pid)
    {
        if (hwnd == IntPtr.Zero || pid == 0 || !Native.IsWindow(hwnd) || !Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd) || Native.IsZoomed(hwnd) || Native.GetForegroundWindow() != hwnd || Native.GetWindowThreadProcessId(hwnd, out uint currentPid) == 0 || currentPid != pid) return false;
        if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out int cloaked, sizeof(int)) != 0 || cloaked != 0) return false;
        return Native.TryGetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, out long style) && (style & Native.WS_EX_TOPMOST) == 0;
    }
    public IFrameSurface Create(IntPtr owner) => new WpfFrameSurface(owner);
    public bool IsSafeAndOwned(IntPtr frame, IntPtr owner)
    {
        const long required = Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
        return Native.IsWindow(frame) && Native.IsWindowVisible(frame) && Native.IsWindow(owner) && Native.GetWindow(frame, Native.GW_OWNER) == owner &&
            Native.TryGetWindowLongPtr(frame, Native.GWL_EXSTYLE, out long style) && (style & required) == required && (style & Native.WS_EX_TOPMOST) == 0;
    }
    public bool IsAbove(IntPtr frame, IntPtr owner) => FrameOrder.IsAbove(frame, owner, Native.IsWindow, h => Native.GetWindow(h, Native.GW_HWNDPREV));
    public void Post(Action action) => dispatcher.BeginInvoke(action, DispatcherPriority.Loaded);
    public void Write(string message) => Diagnostics.Write(message);
    public void Error(string key, string message) => Diagnostics.Error(key, message);
}
