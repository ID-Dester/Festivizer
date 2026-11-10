using GarlandForWindows;

internal static class ControllerScenarios
{
    internal static void Run()
    {
        void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); }
        var menu = new FakeMenuNative(); var tracker = new MenuStateTracker(menu);
        foreach (int id in new[] { Native.OBJID_WINDOW, Native.OBJID_MENU, Native.OBJID_SYSMENU }) foreach (uint ev in new[] { Native.EVENT_SYSTEM_MENUSTART, Native.EVENT_SYSTEM_MENUEND, Native.EVENT_SYSTEM_MENUPOPUPSTART, Native.EVENT_SYSTEM_MENUPOPUPEND }) Check(WinEventRouter.IsMenuEvent(ev, id, 0), $"menu event {ev:X}/{id}");
        Check(WinEventRouter.Route(Native.EVENT_SYSTEM_MENUPOPUPEND, Native.OBJID_MENU, 0) == WinEventRouter.Kind.Menu, "popup routed before object filter");
        Check(WinEventRouter.Route(Native.EVENT_SYSTEM_FOREGROUND, -99, 0) == WinEventRouter.Kind.Foreground, "foreground routed before object filter");
        Check(WinEventRouter.Route(Native.EVENT_OBJECT_LOCATIONCHANGE, Native.OBJID_MENU, 0) == WinEventRouter.Kind.Ignore, "object event remains filtered");
        Check(!WinEventRouter.IsMenuEvent(Native.EVENT_SYSTEM_MENUSTART, -100, 0) && !WinEventRouter.IsMenuEvent(Native.EVENT_SYSTEM_MENUSTART, Native.OBJID_MENU, 1) && !WinEventRouter.IsMenuEvent(Native.EVENT_SYSTEM_MENUSTART, 0, 1), "invalid menu event rejected");
        menu.Flags = Native.GUI_INMENUMODE; Check(tracker.Read().State == MenuState.Open, "menu start reflected by Windows");
        menu.Flags = Native.GUI_POPUPMENUMODE; Check(tracker.Read().State == MenuState.Open, "submenu end while menu remains open");
        menu.Flags = 0; Check(tracker.Read().State == MenuState.Closed, "lost menu end recovers on reconciliation");
        menu.Fail = true; Check(tracker.Read().State == MenuState.Unknown, "menu read failure is unknown"); menu.Fail = false;
        Check(tracker.Read().State == MenuState.Closed, "menu query recovers after failure");
        menu.ChangeDuringRead = true; Check(tracker.Read().State == MenuState.Unknown, "foreground change during menu query is unknown");
        menu.ChangeDuringRead = false; menu.Current = new(new IntPtr(2), 22, 222); menu.Flags = Native.GUI_SYSTEMMENUMODE;
        Check(tracker.Read().State == MenuState.Open, "new foreground menu remains open independently");
        menu.Flags = 0; Check(tracker.Read().State == MenuState.Closed, "new foreground can return to closed");
        var enabled = new Settings();
        Check(!VisibilityPolicy.MayUpdateFrame(enabled, true, false, MenuState.Open, false), "open menu hides frame");
        Check(!VisibilityPolicy.MayUpdateFrame(enabled, true, false, MenuState.Unknown, false), "unknown menu hides frame");
        Check(!VisibilityPolicy.MayUpdateFrame(enabled, true, false, MenuState.Closed, true), "tray open hides frame independently");
        Check(!VisibilityPolicy.MayUpdateFrame(enabled with { GarlandsEnabled = false }, true, false, MenuState.Closed, false), "closed menu does not re-enable disabled garlands");
        Check(VisibilityPolicy.MayUpdateFrame(enabled, true, false, MenuState.Closed, false), "closed menu permits eligible frame");

        var env = new FakeFrameEnvironment(); int refreshes = 0;
        var controller = new FrameOverlayController(env, () => refreshes++);
        var a = new WindowSnapshot(new IntPtr(1), new Native.Rect { Right = 500, Bottom = 400 }, false, true, 11);
        var b = new WindowSnapshot(new IntPtr(2), new Native.Rect { Left = 10, Right = 510, Bottom = 400 }, false, true, 22);
        env.Current = a.Handle; env.CurrentPid = a.ProcessId;
        controller.Update(a, a.Bounds, true); var old = env.Last!;
        Check(controller.IsVisible && old.Owner == a.Handle && old.Above && old.Safe, "controller first show A");
        controller.Update(a, a.Bounds, true); controller.Configure(new Settings { Theme = "warmWhite" });
        Check(env.Created == 1 && old.Settings.Theme == "warmWhite", "controller reuses A and configures");
        old.RaiseDpi(); env.Current = b.Handle; env.CurrentPid = b.ProcessId; controller.Update(b, b.Bounds, true); var current = env.Last!;
        Check(old.Closed && current.Owner == b.Handle && current.Bounds.Left == b.Bounds.Left && controller.IsVisible, "controller A to B closes old and binds new");
        env.Drain(); Check(refreshes == 0 && controller.IsVisible && !current.Closed, "stale A DPI callback cannot alter B");
        current.RaiseDpi(); env.Drain(); Check(refreshes == 1 && controller.IsVisible, "current DPI callback requests refresh"); refreshes = 0;
        controller.Clear("settings"); Check(!controller.IsVisible && current.Closed, "controller clears for own settings");
        controller.Update(b, b.Bounds, true); Check(controller.IsVisible && env.Last!.Owner == b.Handle, "controller restores current B after settings");
        var dead = env.Last!; dead.Live = false; controller.Update(b, b.Bounds, true);
        Check(dead.Closed && controller.IsVisible && !ReferenceEquals(dead, env.Last), "destroyed native frame replaced");
        env.Current = IntPtr.Zero; controller.Update(b, b.Bounds, true); Check(!controller.IsVisible, "destroyed or nonforeground target clears frame");
        env.Current = b.Handle; env.CurrentPid = b.ProcessId; env.FailCreate = true; controller.Update(b, b.Bounds, true); int beforeRetry = env.Created;
        controller.Update(b, b.Bounds, true); Check(!controller.IsVisible && env.Created == beforeRetry, "creation failure delays retry");
        env.FailCreate = false; env.Now = env.Now.AddSeconds(5); controller.Update(b, b.Bounds, true); Check(controller.IsVisible, "creation recovers after delay");
        env.Last!.RaiseDpi(); controller.Dispose(); env.Drain(); Check(refreshes == 0 && !controller.IsVisible, "disposed controller ignores stale callback");
        controller.Update(b, b.Bounds, true); Check(!controller.IsVisible, "disposed controller refuses new target");
        var env2 = new FakeFrameEnvironment { Current = a.Handle, CurrentPid = a.ProcessId, WrongOrder = true };
        using var failing = new FrameOverlayController(env2, () => { }); failing.Update(a, a.Bounds, true);
        Check(!failing.IsVisible && env2.Last!.Closed && env2.Last.Places == 2, "wrong final order corrected once then hidden");
        var env3 = new FakeFrameEnvironment { Current = a.Handle, CurrentPid = a.ProcessId, FailPlacement = true };
        using var failingPlace = new FrameOverlayController(env3, () => { }); failingPlace.Update(a, a.Bounds, true);
        Check(!failingPlace.IsVisible && env3.Last!.Closed, "position failure closes frame");
        env3.Current = b.Handle; env3.CurrentPid = b.ProcessId; env3.FailPlacement = false; failingPlace.Update(b, b.Bounds, true);
        Check(failingPlace.IsVisible && env3.Last!.Owner == b.Handle, "failure A does not block B");
        var env4 = new FakeFrameEnvironment { Current = a.Handle, CurrentPid = a.ProcessId };
        using var cleanup = new FrameOverlayController(env4, () => { }); cleanup.Update(a, a.Bounds, true); var failedClose = env4.Last!;
        env4.FailClose = true; env4.Current = b.Handle; env4.CurrentPid = b.ProcessId; cleanup.Update(b, b.Bounds, true);
        Check(!failedClose.IsVisible && !cleanup.IsVisible && env4.Created == 1, "failed close hides old frame and blocks replacement");
        env4.FailClose = false; cleanup.Update(b, b.Bounds, true);
        Check(failedClose.Closed && cleanup.IsVisible && env4.Last!.Owner == b.Handle, "cleanup recovers before new target is shown");
        var env5 = new FakeFrameEnvironment { Current = a.Handle, CurrentPid = a.ProcessId, UnsafeOwnerOrStyle = true };
        using var badBinding = new FrameOverlayController(env5, () => { }); badBinding.Update(a, a.Bounds, true);
        Check(!badBinding.IsVisible && env5.Last!.Closed, "invalid owner or styles are hidden and released");
        env5.UnsafeOwnerOrStyle = false; env5.Now = env5.Now.AddSeconds(5); badBinding.Update(a, a.Bounds, true);
        Check(badBinding.IsVisible && env5.Last!.Owner == a.Handle, "owner and style failure can recover");
        var invalidSnapshot = a with { Eligible = false }; badBinding.Update(invalidSnapshot, a.Bounds, true);
        Check(!badBinding.IsVisible, "ineligible window hides frame");
        var fullSnapshot = a with { Fullscreen = true }; badBinding.Update(fullSnapshot, a.Bounds, true);
        Check(!badBinding.IsVisible, "fullscreen conflict hides frame");
        badBinding.Update(a with { ProcessId = 99 }, a.Bounds, true); Check(!badBinding.IsVisible, "invalid PID hides frame");
        badBinding.Update(a, a.Bounds, true); Check(badBinding.IsVisible, "valid restored window recovers frame");
        var env6 = new FakeFrameEnvironment { Current = a.Handle, CurrentPid = a.ProcessId };
        using var hideFailure = new FrameOverlayController(env6, () => { }); hideFailure.Update(a, a.Bounds, true); var hiding = env6.Last!;
        env6.FailHide = true; hideFailure.Clear("test");
        Check(hiding.Closed && !hideFailure.IsVisible, "close still attempted when hide fails");
        var env7 = new FakeFrameEnvironment { Current = a.Handle, CurrentPid = a.ProcessId };
        using var withMenu = new FrameOverlayController(env7, () => { }); var menuNative = new FakeMenuNative();
        menuNative.Current = new(a.Handle, a.ProcessId, 111); var menuTracker = new MenuStateTracker(menuNative); var choices = new Settings();
        void ReconcileMenu() {
            var state = menuTracker.Read();
            if (VisibilityPolicy.MayUpdateFrame(choices, true, false, state.State, false)) withMenu.Update(a, a.Bounds, true);
            else withMenu.Clear("menu or settings");
        }
        menuNative.Flags = Native.GUI_INMENUMODE; ReconcileMenu(); Check(!withMenu.IsVisible, "real controller hidden during menu mode");
        menuNative.Flags = 0; ReconcileMenu(); Check(withMenu.IsVisible, "real controller recovers without menu END");
        choices = choices with { GarlandsEnabled = false }; menuNative.Flags = Native.GUI_INMENUMODE; ReconcileMenu(); menuNative.Flags = 0; ReconcileMenu();
        Check(!withMenu.IsVisible, "menu closing cannot override disabled setting");
        long elapsed = 0; int polls = 0;
        Task Pause() { elapsed += 40; return Task.CompletedTask; }
        var immediate = ValidationWait.UntilAsync(() => FocusState.Ready, () => elapsed, Pause, 5000).GetAwaiter().GetResult();
        Check(immediate == FocusState.Ready && elapsed == 0, "validation waiter immediate foreground");
        elapsed = 0; var later = ValidationWait.UntilAsync(() => ++polls >= 4 ? FocusState.Ready : FocusState.Waiting, () => elapsed, Pause, 5000).GetAwaiter().GetResult();
        Check(later == FocusState.Ready && elapsed == 120, "validation waiter delayed foreground including false activation");
        var falseActivation = new FocusEvidence(false, later, elapsed);
        Check(falseActivation.ActivateResult == false && falseActivation.Result == FocusState.Ready, "false activation recorded while actual foreground can succeed");
        elapsed = 0; var missing = ValidationWait.UntilAsync(() => FocusState.Waiting, () => elapsed, Pause, 5000).GetAwaiter().GetResult();
        Check(missing == FocusState.Blocked && elapsed == 5000, "validation waiter deadline blocks missing foreground");
        Check(ValidationWait.Classify(false, new IntPtr(1), new IntPtr(1), IntPtr.Zero) == FocusState.Blocked, "desktop loss blocks foreground scenario");
        Check(ValidationWait.Classify(true, new IntPtr(1), new IntPtr(2), new IntPtr(2)) == FocusState.Fail, "overlay foreground is failure");
        Check(ValidationWait.ExitCode(new[] { "SKIP", "NOT_RUN" }) == 2, "desktop skip has separate exit code");
        Check(ValidationWait.ExitCode(new[] { "BLOCKED", "NOT_RUN" }) == 3 && ValidationWait.ExitCode(new[] { "FAIL", "BLOCKED" }) == 1, "validation result precedence");
        elapsed = 0; var popupTimeout = ValidationWait.UntilAsync(() => FocusState.Waiting, () => elapsed, Pause, 5000).GetAwaiter().GetResult();
        Check(popupTimeout == FocusState.Blocked && ValidationWait.ExitCode(new[] { "PASS", "BLOCKED", "NOT_RUN", "PASS" }) == 3, "unconfirmed popup deadline cannot pass run");
        Check(ValidationWait.ExitCode(new[] { "PASS", "FAIL", "BLOCKED" }) == 1, "cleanup failure cannot be masked by blocked prerequisite");
    }
}

internal sealed class FakeMenuNative : IMenuNative
{
    internal MenuContext Current = new(new IntPtr(1), 11, 111);
    internal uint Flags; internal bool Fail, ChangeDuringRead;
    public MenuContext ReadContext() => Current;
    public bool TryGetMenuInfo(uint thread, out uint flags, out IntPtr owner, out int error) { flags = Flags; owner = new IntPtr(123); error = Fail ? 5 : 0; if (ChangeDuringRead) Current = new(new IntPtr(9), 99, 999); return !Fail; }
}
internal sealed class FakeFrameEnvironment : IFrameEnvironment
{
    internal IntPtr Current; internal uint CurrentPid; internal DateTime Now = new(2026,1,1,0,0,0,DateTimeKind.Utc);
    internal int Created; internal bool FailCreate, FailPlacement, FailClose, FailHide, WrongOrder, UnsafeOwnerOrStyle; internal FakeFrameSurface? Last;
    private readonly Queue<Action> queue = new();
    public DateTime UtcNow => Now;
    public bool StillCurrent(IntPtr hwnd, uint pid) => Current == hwnd && CurrentPid == pid && hwnd != IntPtr.Zero;
    public IFrameSurface Create(IntPtr owner) { if (FailCreate) throw new InvalidOperationException("create failed"); Created++; return Last = new FakeFrameSurface(this, owner, new IntPtr(100 + Created)); }
    public bool IsSafeAndOwned(IntPtr frame, IntPtr owner) => !UnsafeOwnerOrStyle && Last is { Live: true, Safe: true, Closed: false } && Last.Handle == frame && Last.Owner == owner;
    public bool IsAbove(IntPtr frame, IntPtr owner) => Last is { Above: true, Live: true, Closed: false } && Last.Handle == frame && Last.Owner == owner;
    public void Post(Action action) => queue.Enqueue(action);
    internal void Drain() { while (queue.TryDequeue(out var action)) action(); }
    public void Write(string message) { }
    public void Error(string key, string message) { }
}
internal sealed class FakeFrameSurface(FakeFrameEnvironment env, IntPtr owner, IntPtr handle) : IFrameSurface
{
    internal readonly IntPtr Owner = owner; internal bool Live = true, Safe = true, Above, Closed; internal int Places; internal Native.Rect Bounds; internal Settings Settings = new();
    public IntPtr Handle => handle; public bool IsVisible { get; private set; } public bool HasLiveHandle => Live && !Closed;
    public event Action? DpiChanged;
    internal void RaiseDpi() => DpiChanged?.Invoke();
    public void Configure(Settings settings) => Settings = settings;
    public void Animate(double phase) { }
    public void Place(Native.Rect bounds, bool preserveZOrder) { Places++; if (env.FailPlacement) throw new InvalidOperationException("position failed"); Bounds = bounds; IsVisible = true; Above = !env.WrongOrder; }
    public void Hide() { if (env.FailHide) throw new InvalidOperationException("hide failed"); IsVisible = false; }
    public void Close() { if (env.FailClose) throw new InvalidOperationException("close failed"); Closed = true; IsVisible = false; Live = false; }
}
