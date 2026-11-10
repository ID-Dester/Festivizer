namespace GarlandForWindows;

internal enum FocusState { Ready, Waiting, Blocked, Fail }
internal readonly record struct FocusEvidence(bool? ActivateResult, FocusState Result, long ElapsedMs);
internal sealed class MenuObservation(MenuState expected, MenuContext context)
{
    private bool unknown;
    private MenuState? last;
    internal readonly List<string> History = [];
    internal string? Status { get; private set; }
    internal string Reason { get; private set; } = "waiting";

    internal void Observe(FocusState focus, MenuSnapshot reading, long elapsedMs, bool deadline)
    {
        if (Status != null) return;
        if (focus != FocusState.Ready) {
            Status = focus == FocusState.Fail ? "FAIL" : "BLOCKED";
            Reason = focus == FocusState.Fail ? "overlay became foreground" : "desktop or foreground lost";
            return;
        }
        MenuState state = reading.Context == context ? reading.State : MenuState.Unknown;
        if (last != state) { History.Add($"{elapsedMs}ms: {state} (error={reading.Error}, context={reading.Context})"); last = state; }
        if (state == MenuState.Unknown) unknown = true;
        if (state == expected && !deadline) { Status = "PASS"; Reason = $"confirmed {expected}"; }
        else if (deadline) {
            if (state == expected) { Status = "BLOCKED"; Reason = $"{expected} first observed after deadline"; return; }
            Status = unknown ? "BLOCKED" : "FAIL";
            Reason = unknown ? "unreliable or inconsistent menu observation before deadline" : $"all samples were {state} until deadline while waiting for {expected}";
        }
    }
}
internal static class ValidationWait
{
    internal static FocusState Classify(bool desktopAvailable, IntPtr expected, IntPtr foreground, IntPtr overlay)
    {
        if (!desktopAvailable) return FocusState.Blocked;
        if (overlay != IntPtr.Zero && foreground == overlay) return FocusState.Fail;
        return foreground == expected && expected != IntPtr.Zero ? FocusState.Ready : FocusState.Waiting;
    }
    internal static async Task<FocusState> UntilAsync(Func<FocusState> probe, Func<long> elapsedMs, Func<Task> pause, long deadlineMs)
    {
        while (true) {
            var state = probe();
            if (state != FocusState.Waiting) return state;
            if (elapsedMs() >= deadlineMs) return FocusState.Blocked;
            await pause();
        }
    }
    internal static int ExitCode(IEnumerable<string> statuses) {
        var list = statuses.ToArray();
        if (list.Contains("FAIL")) return 1;
        if (list.Contains("BLOCKED")) return 3;
        if (list.Contains("SKIP")) return 2;
        if (list.Contains("NOT_RUN")) return 3;
        return list.Length > 0 && list.All(x => x == "PASS") ? 0 : 1;
    }
}
