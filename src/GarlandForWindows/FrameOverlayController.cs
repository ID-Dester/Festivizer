namespace GarlandForWindows;

internal interface IFrameSurface
{
    IntPtr Handle { get; }
    bool IsVisible { get; }
    bool HasLiveHandle { get; }
    event Action? DpiChanged;
    void Configure(Settings settings);
    void Animate(double phase);
    void Place(Native.Rect bounds, bool preserveZOrder);
    void Hide();
    void Close();
}

internal interface IFrameEnvironment
{
    DateTime UtcNow { get; }
    bool StillCurrent(IntPtr hwnd, uint pid);
    IFrameSurface Create(IntPtr owner);
    bool IsSafeAndOwned(IntPtr frame, IntPtr owner);
    bool IsAbove(IntPtr frame, IntPtr owner);
    void Post(Action action);
    void Write(string message);
    void Error(string key, string message);
}

// The same state machine is used with Win32/WPF in production and a fake environment in tests.
internal sealed class FrameOverlayController : IDisposable
{
    private readonly IFrameEnvironment environment;
    private readonly Action requestRefresh;
    private readonly FrameRetryGate retry;
    private IFrameSurface? overlay;
    private IFrameSurface? pendingCleanup;
    private IntPtr target;
    private uint targetPid;
    private int generation;
    private bool disposed;
    private Settings settings = new();
    internal FrameOverlayController(IFrameEnvironment environment, Action requestRefresh)
    {
        this.environment = environment; this.requestRefresh = requestRefresh;
        retry = new FrameRetryGate(() => environment.UtcNow);
    }
    internal bool IsVisible => overlay is { IsVisible: true, HasLiveHandle: true };
    internal void Configure(Settings value) { if (disposed) return; settings = value; overlay?.Configure(value); }
    internal void Animate(double phase) { if (!disposed && IsVisible) overlay!.Animate(phase); }
    internal void Clear(string reason)
    {
        if (overlay == null) return;
        var old = overlay; overlay = null; target = IntPtr.Zero; targetPid = 0; generation++;
        environment.Write($"Frame clear generation={generation} reason={reason}");
        try { old.Hide(); } catch (Exception e) { environment.Error("frame-hide", $"Frame hide: {e.Message}"); }
        try { old.Close(); } catch (Exception e) { environment.Error("frame-close", $"Frame close: {e.Message}"); if (old.HasLiveHandle) pendingCleanup = old; }
    }
    private bool RetryCleanup()
    {
        var pending = pendingCleanup;
        if (pending == null) return true;
        try { pending.Hide(); } catch (Exception e) { environment.Error("frame-hide", $"Frame hide: {e.Message}"); }
        try { pending.Close(); pendingCleanup = null; return true; }
        catch (Exception e) { environment.Error("frame-cleanup", $"Frame cleanup: {e.Message}"); if (!pending.HasLiveHandle) { pendingCleanup = null; return true; } return false; }
    }
    internal void Update(WindowSnapshot snapshot, Native.Rect bounds, bool verifyOrder)
    {
        if (disposed) return;
        if (!RetryCleanup()) return;
        if (!snapshot.Eligible || snapshot.Fullscreen || !environment.StillCurrent(snapshot.Handle, snapshot.ProcessId)) { Clear("ineligible or foreground changed"); return; }
        if (!retry.ShouldAttempt(snapshot.Handle, snapshot.ProcessId)) return;
        if (overlay != null && (target != snapshot.Handle || targetPid != snapshot.ProcessId || !overlay.HasLiveHandle)) {
            Clear("target or native handle changed");
            if (pendingCleanup != null) return;
        }
        bool createdNow = overlay == null;
        if (overlay == null) {
            target = snapshot.Handle; targetPid = snapshot.ProcessId; int token = ++generation;
            IFrameSurface created;
            try {
                created = environment.Create(target); overlay = created;
                created.Configure(settings);
                created.DpiChanged += () => {
                    try { environment.Post(() => { if (!disposed && generation == token && ReferenceEquals(overlay, created)) requestRefresh(); }); }
                    catch (InvalidOperationException) { /* Dispatcher is shutting down. */ }
                };
            } catch (Exception e) { environment.Error("frame-create", $"Frame creation failed: {e.Message}"); Fail("creation failure"); return; }
            verifyOrder = true;
            environment.Write($"Frame create generation={generation} target=0x{target.ToInt64():X} pid={targetPid}");
        }
        var active = overlay!;
        try {
            if (!environment.StillCurrent(target, targetPid)) { Clear("target changed before placement"); return; }
            active.Place(bounds, preserveZOrder: active.IsVisible);
            if (!environment.StillCurrent(target, targetPid)) { Clear("target changed after placement"); return; }
            if (!environment.IsSafeAndOwned(active.Handle, target)) { Fail("invalid owner, handle or styles"); return; }
            if (createdNow) environment.Write($"Frame shown generation={generation} target=0x{target.ToInt64():X} targetPid={targetPid} overlay=0x{active.Handle.ToInt64():X} bounds={bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom} above={environment.IsAbove(active.Handle, target)}");
            if (verifyOrder && !FrameOrder.Ensure(() => environment.IsAbove(active.Handle, target), () => {
                environment.Error("frame-order", $"Frame order violation generation={generation} target=0x{target.ToInt64():X} overlay=0x{active.Handle.ToInt64():X}");
                active.Place(bounds, preserveZOrder: false);
            })) { Fail("z-order correction failed"); return; }
            if (!environment.StillCurrent(target, targetPid)) Clear("target changed after placement");
            else if (!environment.IsSafeAndOwned(active.Handle, target)) Fail("invalid owner, handle or styles");
            else retry.Reset();
        } catch (Exception e) { environment.Error("frame-update", $"Frame update generation={generation}: {e.Message}"); Fail("update failure"); }
    }
    private void Fail(string reason) { retry.Failed(target, targetPid); Clear(reason); }
    public void Dispose() { if (disposed) return; disposed = true; Clear("shutdown"); RetryCleanup(); }
}
