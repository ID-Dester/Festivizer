using GarlandForWindows;
var monitor = new Native.Rect { Left = -1920, Top = 0, Right = 0, Bottom = 1080 };
void Check(bool expected, Native.Rect rect, long style, string label)
{
    if (GeometryPolicy.IsFullscreen(rect, monitor, style) != expected) throw new Exception(label);
    Console.WriteLine($"PASS {label}");
}
Check(true, monitor, 0, "exact monitor");
foreach (var side in new[] { "left", "top", "right", "bottom" }) {
    var r = monitor;
    switch (side) { case "left": r.Left += 2; break; case "top": r.Top += 2; break; case "right": r.Right -= 2; break; case "bottom": r.Bottom -= 2; break; }
    Check(true, r, 0, side + " 2 pixels");
    switch (side) { case "left": r.Left++; break; case "top": r.Top++; break; case "right": r.Right--; break; case "bottom": r.Bottom--; break; }
    Check(false, r, 0, side + " 3 pixels");
}
Check(false, monitor, Native.WS_CAPTION, "caption");
Check(false, monitor, Native.WS_THICKFRAME, "thick frame");
AssertCs2();
void AssertCs2() {
    const long observedStyle = 0x96CA0000L;
    void Verify(bool expected, Native.Rect bounds, long style, bool zoomed, string? name, string label) {
        if (GeometryPolicy.IsObservedCs2Fullscreen(bounds, monitor, style, zoomed, name) != expected) throw new Exception(label);
        Console.WriteLine("PASS " + label);
    }
    Verify(true, monitor, observedStyle, false, "cs2", "observed CS2 fullscreen style");
    Verify(true, monitor, observedStyle, false, "CS2", "CS2 process name case insensitive");
    Verify(false, monitor, observedStyle, true, "cs2", "CS2 maximized does not trigger exception");
    Verify(false, monitor, observedStyle, false, "other", "other process does not trigger CS2 exception");
    Verify(false, monitor, observedStyle, false, null, "missing process is not identified as CS2");
    Verify(false, monitor, observedStyle ^ Native.WS_THICKFRAME, false, "cs2", "unobserved style does not trigger exception");
    var reduced = monitor; reduced.Bottom -= 3;
    Verify(false, reduced, observedStyle, false, "cs2", "CS2 bounds beyond tolerance excluded");
}
var work = monitor; work.Bottom -= 48; Check(false, work, 0, "work area is not monitor");

void Assert(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }
string dir = Path.Combine(Path.GetTempPath(), "garland-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
try {
    string path = Path.Combine(dir, "settings.json"); var service = new SettingsService(path);
    Assert(service.Load() == new Settings(), "missing settings defaults");
    var saved = new Settings { GarlandsEnabled = false, TaskbarEnabled = false, Theme = "warmWhite", BrightnessPercent = 25, SpeedPercent = 200 };
    service.Save(saved); Assert(service.Load() == saved, "settings round trip");
    File.WriteAllText(path, "{\"theme\":\"unknown\",\"brightnessPercent\":0,\"speedPercent\":900,\"extra\":true}");
    var clamped = service.Load(); Assert(clamped.Theme == "classicChristmas" && clamped.BrightnessPercent == 10 && clamped.SpeedPercent == 200 && clamped.TaskbarEnabled, "defaults unknown fields theme and ranges");
    File.WriteAllText(path, "{\"taskbarEnabled\":\"wrong\"}"); Assert(service.Load() == new Settings() && File.Exists(Path.Combine(dir, "settings.invalid.json")), "invalid type backup");
    File.WriteAllText(path, "{broken"); Assert(service.Load() == new Settings(), "invalid JSON");
    string future = "{\"schemaVersion\":99,\"future\":true}"; File.WriteAllText(path, future); var newer = new SettingsService(path);
    Assert(newer.Load() == new Settings() && newer.FutureVersion, "future schema defaults");
    bool blocked = false; try { newer.Save(saved); } catch (InvalidOperationException) { blocked = true; }
    Assert(blocked && File.ReadAllText(path) == future, "future schema protected");
    newer.Save(saved, true); Assert(newer.Load() == saved && !newer.FutureVersion, "explicit future schema reset");
    string badPath = Path.Combine(dir, "target-directory"); Directory.CreateDirectory(badPath); bool failed = false;
    try { new SettingsService(badPath).Save(saved); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failed = true; }
    Assert(failed && Directory.Exists(badPath), "write failure preserves destination"); Directory.Delete(badPath);
    foreach (string file in Directory.GetFiles(dir)) File.Delete(file);
} finally { Directory.Delete(dir); }
foreach (bool all in new[] { false, true }) foreach (bool bar in new[] { false, true }) foreach (bool frameOn in new[] { false, true }) foreach (bool panel in new[] { false, true }) foreach (bool full in new[] { false, true }) foreach (bool healthy in new[] { false, true }) {
    var s = new Settings { GarlandsEnabled = all, TaskbarEnabled = bar, WindowFrameEnabled = frameOn };
    if (VisibilityPolicy.Taskbar(s, panel, full, healthy) != (all && bar && panel && !full && healthy) || VisibilityPolicy.Frame(s, panel, full, healthy) != (all && frameOn && panel && !full && healthy)) throw new Exception("visibility table");
}
Console.WriteLine("PASS visibility combinations");
foreach (var (dpi, expected) in new[] { (96u, 36), (144u, 54), (168u, 63), (192u, 72) }) Assert(VisibilityPolicy.DipToPixels(36, dpi) == expected, $"DPI strip {dpi}");
foreach (var side in new[] { "left", "top", "right", "bottom" }) {
    var r = monitor;
    switch (side) { case "left": r.Left -= 2; break; case "top": r.Top -= 2; break; case "right": r.Right += 2; break; case "bottom": r.Bottom += 2; break; }
    Check(true, r, 0, side + " outward 2 pixels");
    switch (side) { case "left": r.Left--; break; case "top": r.Top--; break; case "right": r.Right++; break; case "bottom": r.Bottom++; break; }
    Check(false, r, 0, side + " outward 3 pixels");
}
Assert(AutoStartCommand.Create(@"C:\Program Files\Garland\app.exe") == "\"C:\\Program Files\\Garland\\app.exe\"", "autostart command quotes spaces");
bool invalidPath = false; try { AutoStartCommand.Create("relative.exe"); } catch (InvalidOperationException) { invalidPath = true; } Assert(invalidPath, "autostart rejects relative path");

var queued = new System.Collections.Concurrent.ConcurrentQueue<Action>();
int refreshCount = 0, currentTarget = 1, observedTarget = 0;
using (var scheduler = new RefreshScheduler(a => queued.Enqueue(a), () => { refreshCount++; observedTarget = currentTarget; })) {
    Parallel.For(0, 1000, _ => scheduler.Request());
    Assert(queued.Count == 1, "concurrent requests coalesced before dispatch");
    currentTarget = 2; Assert(queued.TryDequeue(out var first), "queued refresh exists"); first!();
    Assert(refreshCount == 1 && observedTarget == 2, "refresh reads current state at execution");
    scheduler.Request(); scheduler.Dispose(); Assert(queued.TryDequeue(out var stale), "queued refresh before shutdown"); stale!();
    Assert(refreshCount == 1, "queued refresh cancelled on dispose");
}
var fake = new FakeOverlayInterop { FailSet = true }; var safety = new OverlaySafety(fake, new IntPtr(42));
Assert(!safety.Prepare(), "overlay rejected when style setting fails");
bool shown = false, hidden = false; var area = new Native.Rect { Right = 100, Bottom = 100 };
try { safety.Place(area, IntPtr.Zero, () => { shown = true; return true; }, () => hidden = true); } catch (InvalidOperationException) { }
Assert(!shown && hidden && fake.Positions == 0, "unsafe overlay never positioned or shown");
fake.FailSet = false; safety.Place(area, IntPtr.Zero, () => { shown = true; return true; }, () => hidden = true);
Assert(shown && fake.Positions == 2, "overlay recovers and repositions after show");
fake = new FakeOverlayInterop { DropRequiredStyles = true }; safety = new OverlaySafety(fake, new IntPtr(42));
Assert(!safety.Prepare(), "overlay rejects unconfirmed styles");
fake = new FakeOverlayInterop { FailPosition = true }; safety = new OverlaySafety(fake, new IntPtr(42));
Assert(safety.Prepare(), "overlay styles prepared"); shown = false; hidden = false;
try { safety.Place(area, IntPtr.Zero, () => { shown = true; return true; }, () => hidden = true); } catch (InvalidOperationException) { }
Assert(!shown && hidden && fake.Positions == 1, "position failure hides overlay");
fake.FailPosition = false; safety.Place(area, IntPtr.Zero, () => { shown = true; return true; }, () => hidden = true);
Assert(shown, "overlay can recover on successful retry");
fake.LastPreserveZOrder = false; safety.Place(area, IntPtr.Zero, () => false, () => hidden = true, preserveZOrder: true);
Assert(fake.LastPreserveZOrder, "existing frame moves without changing z-order");
fake = new FakeOverlayInterop { FailOnPosition = 2 }; safety = new OverlaySafety(fake, new IntPtr(42)); safety.Prepare(); shown = false; hidden = false;
try { safety.Place(area, IntPtr.Zero, () => { shown = true; return true; }, () => hidden = true); } catch (InvalidOperationException) { }
Assert(shown && hidden && fake.Positions == 2, "overlay hidden when reposition after showing fails");
var released = new List<IntPtr>(); var hookErrors = new List<string>();
var hooks = new HookRegistry(h => { released.Add(h); if (h == new IntPtr(1)) return false; if (h == new IntPtr(2)) throw new InvalidOperationException("test"); return true; }, hookErrors.Add);
hooks.Add(new IntPtr(1)); hooks.Add(new IntPtr(2)); hooks.Add(new IntPtr(3));
bool rejectedHook = false; try { hooks.Add(IntPtr.Zero); } catch (InvalidOperationException) { rejectedHook = true; }
hooks.Dispose(); hooks.Dispose();
Assert(rejectedHook && released.Count == 3 && hookErrors.Count == 2, "partial hook startup and cleanup failures release other hooks once");

var store = new FakeRunStore(); store.Values["OtherApplication"] = "untouched";
var auto = new AutoStartManager(store, @"C:\Program Files\Garland\app.exe");
Assert(!auto.Read().Enabled && store.Values.Count == 1, "autostart read does not write registry");
auto.Apply(true);
Assert(auto.Read().Enabled && !auto.Read().Moved && store.Values[AutoStartManager.Name] == "\"C:\\Program Files\\Garland\\app.exe\"", "autostart registers current executable");
store.Values[AutoStartManager.Name] = "\"C:\\old\\app.exe\"";
Assert(auto.Read().Moved && store.Values[AutoStartManager.Name].Contains("old"), "moved executable detected without rewrite");
auto.Apply(false); Assert(!store.Values.ContainsKey(AutoStartManager.Name) && store.Values["OtherApplication"] == "untouched", "autostart removes only own value");
store.Deny = true; Assert(auto.Read().Error != null, "autostart read access denied is reported");
bool denied = false; try { auto.Apply(true); } catch (UnauthorizedAccessException) { denied = true; }
Assert(denied && !store.Values.ContainsKey(AutoStartManager.Name), "autostart write access denied is reported");
Assert(WindowEligibilityPolicy.IsEligible(true, true, false, true, false, 0, 800, 600), "ordinary restored window eligible");
Assert(!WindowEligibilityPolicy.IsEligible(true, true, true, true, false, 0, 800, 600), "maximized window excluded");
Assert(!WindowEligibilityPolicy.IsEligible(true, true, false, true, false, Native.WS_EX_TOPMOST, 800, 600), "topmost window excluded");
Assert(!WindowEligibilityPolicy.IsEligible(true, true, false, true, true, 0, 800, 600), "owned window excluded");
Assert(!WindowEligibilityPolicy.IsEligible(true, false, false, true, false, 0, 800, 600), "unknown style excluded");
var targetWindow = new IntPtr(10); var frameWindow = new IntPtr(20); var intermediateWindow = new IntPtr(30);
var order = new Dictionary<IntPtr, IntPtr> { [targetWindow] = intermediateWindow, [intermediateWindow] = frameWindow, [frameWindow] = IntPtr.Zero };
bool Exists(IntPtr h) => order.ContainsKey(h);
IntPtr Previous(IntPtr h) => order.GetValueOrDefault(h);
Assert(FrameOrder.IsAbove(frameWindow, targetWindow, Exists, Previous), "z-order detects non-adjacent frame above target");
order[targetWindow] = IntPtr.Zero; order[frameWindow] = targetWindow;
int corrections = 0;
bool corrected = FrameOrder.Ensure(() => FrameOrder.IsAbove(frameWindow, targetWindow, Exists, Previous), () => { corrections++; order[targetWindow] = frameWindow; order[frameWindow] = IntPtr.Zero; });
Assert(corrected && corrections == 1, "successful positioning does not substitute z-order verification");
order[targetWindow] = IntPtr.Zero; corrections = 0;
Assert(!FrameOrder.Ensure(() => FrameOrder.IsAbove(frameWindow, targetWindow, Exists, Previous), () => corrections++) && corrections == 1, "persistent wrong order fails after one correction");
order[targetWindow] = intermediateWindow; order[intermediateWindow] = targetWindow;
Assert(!FrameOrder.IsAbove(frameWindow, targetWindow, Exists, Previous), "cyclic z-order terminates safely");
order.Remove(frameWindow); order[targetWindow] = frameWindow;
Assert(!FrameOrder.IsAbove(frameWindow, targetWindow, Exists, Previous), "destroyed overlay fails verification");
var fakeNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc); var retryGate = new FrameRetryGate(() => fakeNow);
retryGate.Failed(targetWindow, 7);
Assert(!retryGate.ShouldAttempt(targetWindow, 7), "failed target retry is delayed");
fakeNow = fakeNow.AddSeconds(5);
Assert(retryGate.ShouldAttempt(targetWindow, 7), "failed target can recover after delay");
retryGate.Failed(targetWindow, 7);
Assert(retryGate.ShouldAttempt(new IntPtr(11), 8), "new target not blocked by prior failure");

ControllerScenarios.Run();

var menuContext = new MenuContext(new IntPtr(10), 20, 30);
MenuSnapshot Reading(MenuState state) => new(menuContext, state, IntPtr.Zero, 0);
var observed = new MenuObservation(MenuState.Closed, menuContext);
observed.Observe(FocusState.Ready, Reading(MenuState.Open), 0, false);
observed.Observe(FocusState.Ready, Reading(MenuState.Open), 5000, true);
Assert(observed.Status == "FAIL", "confirmed open at closing deadline fails");
observed = new(MenuState.Open, menuContext);
observed.Observe(FocusState.Ready, Reading(MenuState.Closed), 5000, true);
Assert(observed.Status == "FAIL", "confirmed closed at opening deadline fails");
observed = new(MenuState.Closed, menuContext);
observed.Observe(FocusState.Ready, Reading(MenuState.Unknown), 0, false);
observed.Observe(FocusState.Ready, Reading(MenuState.Open), 5000, true);
Assert(observed.Status == "BLOCKED", "unknown observation cannot prove continuous open");
observed = new(MenuState.Closed, menuContext);
observed.Observe(FocusState.Ready, Reading(MenuState.Unknown), 0, false);
observed.Observe(FocusState.Ready, Reading(MenuState.Closed), 1, false);
Assert(observed.Status == "PASS", "closed after unknown is confirmed");
observed = new(MenuState.Closed, menuContext);
observed.Observe(FocusState.Ready, new MenuSnapshot(new MenuContext(new IntPtr(11), 20, 30), MenuState.Closed, IntPtr.Zero, 0), 5000, true);
Assert(observed.Status == "BLOCKED", "changed menu context is unreliable");
observed = new(MenuState.Closed, menuContext);
observed.Observe(FocusState.Fail, Reading(MenuState.Closed), 0, false);
observed.Observe(FocusState.Ready, Reading(MenuState.Closed), 1, false);
Assert(observed.Status == "FAIL", "overlay focus failure remains failure");
observed = new(MenuState.Closed, menuContext);
observed.Observe(FocusState.Blocked, Reading(MenuState.Closed), 0, false);
Assert(observed.Status == "BLOCKED" && ValidationWait.ExitCode(new[] { observed.Status, "NOT_RUN", "FAIL" }) == 1, "cleanup failure overrides blocked outcome");

sealed class FakeOverlayInterop : IOverlayInterop
{
    internal bool FailSet, FailPosition, DropRequiredStyles, LastPreserveZOrder;
    internal int Positions, FailOnPosition;
    private long style;
    public bool TryGetStyle(IntPtr handle, out long result) { result = DropRequiredStyles ? 0 : style; return true; }
    public bool TrySetStyle(IntPtr handle, long value) { if (FailSet) return false; style = value; return true; }
    public bool TryPosition(IntPtr handle, IntPtr after, Native.Rect bounds, bool preserveZOrder, out int error) { Positions++; LastPreserveZOrder = preserveZOrder; bool failure = FailPosition || Positions == FailOnPosition; error = failure ? 5 : 0; return !failure; }
}

sealed class FakeRunStore : IRunStore
{
    internal readonly Dictionary<string, string> Values = [];
    internal bool Deny;
    public string? Read(string name) { if (Deny) throw new UnauthorizedAccessException("denied"); return Values.GetValueOrDefault(name); }
    public void Write(string name, string value) { if (Deny) throw new UnauthorizedAccessException("denied"); Values[name] = value; }
    public void Delete(string name) { if (Deny) throw new UnauthorizedAccessException("denied"); Values.Remove(name); }
}

