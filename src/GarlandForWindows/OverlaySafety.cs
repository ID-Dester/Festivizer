namespace GarlandForWindows;

internal interface IOverlayInterop
{
    bool TryGetStyle(IntPtr handle, out long style);
    bool TrySetStyle(IntPtr handle, long style);
    bool TryPosition(IntPtr handle, IntPtr after, Native.Rect bounds, bool preserveZOrder, out int error);
}

internal sealed class OverlayInterop(bool preserveOwner = false) : IOverlayInterop
{
    public bool TryGetStyle(IntPtr handle, out long style) => Native.TryGetWindowLongPtr(handle, Native.GWL_EXSTYLE, out style);
    public bool TrySetStyle(IntPtr handle, long style) => Native.TrySetWindowLongPtr(handle, Native.GWL_EXSTYLE, style);
    public bool TryPosition(IntPtr handle, IntPtr after, Native.Rect bounds, bool preserveZOrder, out int error)
    {
        bool success = Native.SetWindowPos(handle, after, bounds.Left, bounds.Top, bounds.Width, bounds.Height, Native.SWP_NOACTIVATE | (preserveOwner ? Native.SWP_NOOWNERZORDER : 0) | (preserveZOrder ? Native.SWP_NOZORDER : 0));
        error = success ? 0 : System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
        return success;
    }
}

internal sealed class OverlaySafety(IOverlayInterop interop, IntPtr handle)
{
    private const long Required = Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
    private bool safe;
    internal bool Prepare() => safe = interop.TryGetStyle(handle, out long style) &&
        interop.TrySetStyle(handle, style | Required) &&
        interop.TryGetStyle(handle, out long applied) && (applied & Required) == Required;

    // Never invoke show before both style verification and positioning succeed.
    internal void Place(Native.Rect bounds, IntPtr after, Func<bool> show, Action hide, bool preserveZOrder = false)
    {
        if (!safe && !Prepare()) { hide(); throw new InvalidOperationException("Could not verify safe overlay styles."); }
        if (!interop.TryPosition(handle, after, bounds, preserveZOrder, out int error)) {
            hide(); throw new InvalidOperationException($"Could not position overlay: {error}.");
        }
        // WPF Show may change the Z-order after the initial placement.
        if (show() && !interop.TryPosition(handle, after, bounds, false, out error)) {
            hide(); throw new InvalidOperationException($"Could not position overlay after showing: {error}.");
        }
    }
}
