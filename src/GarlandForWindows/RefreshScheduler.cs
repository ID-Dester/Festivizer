namespace GarlandForWindows;

// Coalesce before posting. A queued callback reads current state when it runs.
internal sealed class RefreshScheduler(Action<Action> post, Action refresh) : IDisposable
{
    private readonly object gate = new();
    private bool pending, disposed;

    internal void Request()
    {
        lock (gate) {
            if (disposed || pending) return;
            pending = true;
        }
        try { post(Run); }
        catch {
            lock (gate) pending = false;
            throw;
        }
    }

    private void Run()
    {
        lock (gate) {
            pending = false;
            if (disposed) return;
        }
        refresh();
    }

    public void Dispose() { lock (gate) disposed = true; }
}
