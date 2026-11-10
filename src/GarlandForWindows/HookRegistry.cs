namespace GarlandForWindows;

// Owned and disposed on the dispatcher thread that registered the hooks.
internal sealed class HookRegistry(Func<IntPtr, bool> unhook, Action<string> log) : IDisposable
{
    private readonly List<IntPtr> hooks = [];
    private bool disposed;
    internal void Add(IntPtr hook)
    {
        if (disposed) throw new ObjectDisposedException(nameof(HookRegistry));
        if (hook == IntPtr.Zero) throw new InvalidOperationException("Unable to register window event hook.");
        hooks.Add(hook);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var hook in hooks) {
            try { if (!unhook(hook)) log("UnhookWinEvent failed"); }
            catch (Exception e) { log($"Unhook: {e.Message}"); }
        }
        hooks.Clear();
    }
}
