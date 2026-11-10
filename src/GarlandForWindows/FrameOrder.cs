namespace GarlandForWindows;

internal static class FrameOrder
{
    internal static bool IsAbove(IntPtr frame, IntPtr owner, Func<IntPtr, bool> exists, Func<IntPtr, IntPtr> previous)
    {
        var seen = new HashSet<IntPtr>(); IntPtr current = owner;
        for (int i = 0; i < 4096 && current != IntPtr.Zero; i++) {
            if (!exists(current) || !seen.Add(current)) return false;
            current = previous(current);
            if (current == frame) return exists(frame);
        }
        return false;
    }
    internal static bool Ensure(Func<bool> isAbove, Action correct)
    {
        if (isAbove()) return true;
        correct();
        return isAbove();
    }
}
