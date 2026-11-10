namespace GarlandForWindows;

internal sealed class FrameRetryGate(Func<DateTime> now)
{
    private IntPtr target;
    private uint pid;
    private DateTime retryAfter;
    internal bool ShouldAttempt(IntPtr candidate, uint candidatePid)
    {
        if (candidate != target || candidatePid != pid) Reset();
        return now() >= retryAfter;
    }
    internal void Failed(IntPtr candidate, uint candidatePid) { target = candidate; pid = candidatePid; retryAfter = now().AddSeconds(5); }
    internal void Reset() { target = IntPtr.Zero; pid = 0; retryAfter = default; }
}
