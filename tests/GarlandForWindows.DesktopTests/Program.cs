using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using GarlandForWindows;

internal sealed class ScenarioException(string status, string phase, string detail) : Exception(detail)
{ internal string Status { get; } = status; internal string Phase { get; } = phase; }

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        using var record = new RunRecord(args);
        if (args.Contains("--supervisor-test-hang")) { record.Mark("supervisor-test", "PASS", "intentional hang without windows"); Thread.Sleep(Timeout.Infinite); }
        if (args.Contains("--supervisor-test-crash")) return 9;
        if (args.Contains("--supervisor-test-pass")) { foreach (var key in record.Results.Keys.ToArray()) record.Mark(key, "PASS", "supervisor self-test"); record.Complete(0); return 0; }
        if (!Environment.UserInteractive || !NativeMenu.HasDefaultInputDesktop()) {
            record.Mark("desktop", "SKIP", "default input desktop unavailable");
            int skipped = ValidationWait.ExitCode(record.Results.Values); record.Complete(skipped); return skipped;
        }
        record.Mark("desktop", "PASS");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(async () => {
            Window? target = null; GarlandOverlay? overlay = null; FrameOverlayController? controller = null; ObservingEnvironment? env = null;
            DispatcherTimer? timer = null; IntPtr popup = IntPtr.Zero, owner = IntPtr.Zero, frame = IntPtr.Zero, second = IntPtr.Zero;
            string phase = "low_level"; string cleanupError = ""; bool popupActive = false;
            void Check(bool condition, string checkPhase, string label) {
                string id = checkPhase + ":" + label;
                record.Mark(id, condition ? "PASS" : "FAIL");
                if (!condition) throw new ScenarioException("FAIL", checkPhase, label);
            }
            try {
                target = new Window { Title = "Garland validation target", Width = 400, Height = 250, WindowStartupLocation = WindowStartupLocation.CenterScreen };
                target.Show(); owner = new WindowInteropHelper(target).Handle;
                overlay = new GarlandOverlay(true, owner);
                Check(Native.Bounds(owner, out var rect), phase, "target bounds");
                rect.Left -= 12; rect.Top -= 12; rect.Right += 12; rect.Bottom += 12;
                overlay.Place(rect, Native.HWND_TOP); frame = overlay.Handle;
                bool Above() => FrameOrder.IsAbove(frame, owner, Native.IsWindow, h => Native.GetWindow(h, Native.GW_HWNDPREV));
                Check(Native.IsWindow(frame) && Native.GetWindow(frame, Native.GW_OWNER) == owner, phase, "owner relation");
                Check(Native.IsWindowVisible(frame) && Above(), phase, "frame above target");
                Check(Native.GetForegroundWindow() != frame, phase, "frame not foreground");
                Check(Native.SetWindowPos(frame, new IntPtr(1), rect.Left, rect.Top, rect.Width, rect.Height, Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER), phase, "change z-order");
                overlay.Place(rect, Native.HWND_TOP); Check(Native.IsWindowVisible(frame) && Above(), phase, "frame above after order change");
                overlay.Hide(); Check(!Native.IsWindowVisible(frame), phase, "hide");
                overlay.Place(rect, Native.HWND_TOP); Check(Native.IsWindowVisible(frame) && Above(), phase, "reshow");
                target.Close(); target = null; Check(!Native.IsWindow(owner), phase, "target handle destroyed");
                Check(!Native.IsWindow(frame) || !Native.IsWindowVisible(frame), phase, "frame hidden after owner close");
                overlay.Close(); overlay = null; record.Mark(phase, "PASS");

                phase = "foreground";
                target = new Window { Title = args.Contains("--interactive") ? "Garland test: select this window" : "Garland controller test target", Width = 400, Height = 250, WindowStartupLocation = WindowStartupLocation.CenterScreen };
                target.Show(); second = new WindowInteropHelper(target).Handle;
                Native.GetWindowThreadProcessId(second, out uint expectedPid);
                uint expectedThread = Native.GetWindowThreadProcessId(second, out _);
                bool? activated = args.Contains("--interactive") ? null : target.Activate();
                if (activated == null) Console.WriteLine("Select the Garland test window to begin (up to 60 seconds).");
                var watch = Stopwatch.StartNew();
                FocusState Probe() => ValidationWait.Classify(NativeMenu.HasDefaultInputDesktop(), second, Native.GetForegroundWindow(), IntPtr.Zero);
                var focus = await ValidationWait.UntilAsync(Probe, () => watch.ElapsedMilliseconds, () => Task.Delay(40), activated == null ? 60000 : 5000);
                IntPtr observed = Native.GetForegroundWindow(); Native.GetWindowThreadProcessId(observed, out uint observedPid);
                var evidence = new FocusEvidence(activated, focus, watch.ElapsedMilliseconds);
                record.Details["foreground"] = new { phase, activateResult = evidence.ActivateResult, expectedHwnd = $"0x{second.ToInt64():X}", expectedPid, expectedThread, observedHwnd = $"0x{observed.ToInt64():X}", observedPid, elapsedMs = evidence.ElapsedMs, result = evidence.Result.ToString() };
                if (focus != FocusState.Ready) throw new ScenarioException(focus == FocusState.Fail ? "FAIL" : "BLOCKED", phase, "foreground prerequisite not obtained");
                record.Mark(phase, "PASS", $"activate={activated} elapsedMs={watch.ElapsedMilliseconds}");
                env = new ObservingEnvironment(app.Dispatcher);
                void RequireFocus(string at) {
                    var state = ValidationWait.Classify(NativeMenu.HasDefaultInputDesktop(), second, Native.GetForegroundWindow(), env?.Last?.Handle ?? IntPtr.Zero);
                    if (state != FocusState.Ready) {
                        IntPtr actual = Native.GetForegroundWindow(); Native.GetWindowThreadProcessId(actual, out uint pid);
                        record.Details["focusLoss"] = new { phase = at, expectedHwnd = $"0x{second.ToInt64():X}", observedHwnd = $"0x{actual.ToInt64():X}", observedPid = pid, state = state.ToString() };
                        throw new ScenarioException(state == FocusState.Fail ? "FAIL" : "BLOCKED", at, "foreground lost");
                    }
                }
                phase = "popup"; RequireFocus(phase);
                var menu = new MenuStateTracker(new MenuNative()).Read(); Check(menu.Context.Foreground == second && menu.State == MenuState.Closed, phase, "initial menu closed");
                popup = NativeMenu.CreatePopupMenu(); Check(popup != IntPtr.Zero && NativeMenu.AppendMenu(popup, 0, new UIntPtr(1), "Test item"), phase, "popup creation");
                bool endMenuFailed = false; FocusState popupFocus = FocusState.Ready; IntPtr popupLostFocus = IntPtr.Zero; uint popupLostPid = 0; var menuWatch = Stopwatch.StartNew();
                var expectedContext = new MenuContext(second, expectedPid, expectedThread);
                var opening = new MenuObservation(MenuState.Open, expectedContext);
                timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
                timer.Tick += (_, _) => {
                    popupFocus = ValidationWait.Classify(NativeMenu.HasDefaultInputDesktop(), second, Native.GetForegroundWindow(), env?.Last?.Handle ?? IntPtr.Zero);
                    var reading = new MenuStateTracker(new MenuNative()).Read();
                    opening.Observe(popupFocus, reading, menuWatch.ElapsedMilliseconds, menuWatch.ElapsedMilliseconds >= 5000);
                    if (popupFocus != FocusState.Ready) { popupLostFocus = Native.GetForegroundWindow(); Native.GetWindowThreadProcessId(popupLostFocus, out popupLostPid); }
                    if (opening.Status != null) { timer.Stop(); endMenuFailed = !NativeMenu.EndMenu(); record.Details["endMenu"] = new { attempted = true, success = !endMenuFailed, elapsedMs = menuWatch.ElapsedMilliseconds, target = $"0x{second.ToInt64():X}", pid = expectedPid }; }
                };
                using var watchdog = new CancellationTokenSource();
                object fallback = new { attempted = false, reason = "cancelled before deadline" };
                var watchdogTask = Task.Run(async () => { try { await Task.Delay(5500, watchdog.Token); if (Native.GetWindowThreadProcessId(second, out uint pid) != 0 && pid == expectedPid) { bool sent = NativeMenu.PostCancelMode(second); fallback = new { attempted = true, sent, target = $"0x{second.ToInt64():X}", pid, elapsedMs = menuWatch.ElapsedMilliseconds, error = sent ? 0 : System.Runtime.InteropServices.Marshal.GetLastPInvokeError() }; } else fallback = new { attempted = false, reason = "target PID changed or unavailable" }; } catch (OperationCanceledException) { } });
                timer.Start(); popupActive = true;
                try { NativeMenu.TrackPopupMenu(popup, 0x0100, 100, 100, 0, second, IntPtr.Zero); popupActive = false; }
                finally { timer.Stop(); watchdog.Cancel(); try { await watchdogTask; record.Details["cancelFallback"] = fallback; } catch (Exception e) { record.Details["cancelFallback"] = new { error = e.ToString() }; throw; } }
                if (popupFocus != FocusState.Ready) {
                    record.Details["focusLoss"] = new { phase, expectedHwnd = $"0x{second.ToInt64():X}", observedHwnd = $"0x{popupLostFocus.ToInt64():X}", observedPid = popupLostPid, state = popupFocus.ToString(), elapsedMs = menuWatch.ElapsedMilliseconds };
                    throw new ScenarioException(popupFocus == FocusState.Fail ? "FAIL" : "BLOCKED", phase, "foreground lost while menu open");
                }
                Check(!endMenuFailed, phase, "EndMenu succeeded");
                record.Details["opening"] = new { opening.Status, opening.Reason, opening.History };
                if (opening.Status != "PASS") throw new ScenarioException(opening.Status ?? "BLOCKED", phase, opening.Reason);
                RequireFocus(phase); Check(true, phase, "popup confirmed open before deadline");
                var closedWatch = Stopwatch.StartNew();
                var closing = new MenuObservation(MenuState.Closed, expectedContext);
                while (closing.Status == null) {
                    var f = ValidationWait.Classify(NativeMenu.HasDefaultInputDesktop(), second, Native.GetForegroundWindow(), env?.Last?.Handle ?? IntPtr.Zero);
                    closing.Observe(f, new MenuStateTracker(new MenuNative()).Read(), closedWatch.ElapsedMilliseconds, closedWatch.ElapsedMilliseconds >= 5000);
                    if (closing.Status == null) await Task.Delay(40);
                }
                record.Details["closing"] = new { closing.Status, closing.Reason, closing.History };
                if (closing.Status != "PASS") throw new ScenarioException(closing.Status!, phase, closing.Reason);
                Check(NativeMenu.DestroyMenu(popup), phase, "popup destroyed"); popup = IntPtr.Zero; record.Mark(phase, "PASS", $"openMs={menuWatch.ElapsedMilliseconds} closeMs={closedWatch.ElapsedMilliseconds}");

                phase = "controller"; RequireFocus(phase);
                Check(Native.Bounds(second, out var secondRect), phase, "target bounds");
                var snapshot = new WindowSnapshot(second, secondRect, false, true, expectedPid);
                controller = new FrameOverlayController(env!, () => { });
                controller.Update(snapshot, secondRect, true); RequireFocus(phase); Check(controller.IsVisible, phase, "controller shows target");
                controller.Clear("test hide"); RequireFocus(phase); Check(!controller.IsVisible, phase, "controller clears target");
                controller.Update(snapshot, secondRect, true); RequireFocus(phase); Check(controller.IsVisible, phase, "controller restores target");
                target.Close(); target = null; controller.Update(snapshot, secondRect, true); Check(!controller.IsVisible, phase, "controller clears destroyed target");
                record.Mark(phase, "PASS");
            } catch (ScenarioException e) { record.Mark(e.Phase, e.Status, e.Message); Console.Error.WriteLine($"{e.Status} {e.Phase}: {e.Message}"); }
            catch (Exception e) { record.Mark(phase, "FAIL", e.Message); Console.Error.WriteLine($"FAIL {phase}: {e}"); }
            finally {
                try { timer?.Stop(); } catch (Exception e) { cleanupError += $"timer: {e.Message}; "; }
                try { if (popupActive && !NativeMenu.EndMenu()) cleanupError += "EndMenu failed; "; } catch (Exception e) { cleanupError += $"EndMenu: {e.Message}; "; }
                try { if (popup != IntPtr.Zero && !NativeMenu.DestroyMenu(popup)) cleanupError += "DestroyMenu failed; "; } catch (Exception e) { cleanupError += $"DestroyMenu: {e.Message}; "; }
                try { controller?.Dispose(); } catch (Exception e) { cleanupError += $"controller: {e.Message}; "; }
                try { overlay?.Close(); } catch (Exception e) { cleanupError += $"overlay: {e.Message}; "; }
                try { target?.Close(); } catch (Exception e) { cleanupError += $"target: {e.Message}; "; }
                bool targetAlive = second != IntPtr.Zero && Native.IsWindow(second); bool ownerAlive = owner != IntPtr.Zero && Native.IsWindow(owner);
                bool frameAlive = frame != IntPtr.Zero && Native.IsWindow(frame); bool controllerFrameAlive = env?.Last is { HasLiveHandle: true };
                record.Details["cleanup"] = new { targetAlive, ownerAlive, frameAlive, controllerFrameAlive, popupRemaining = popup != IntPtr.Zero && cleanupError.Contains("DestroyMenu"), errors = cleanupError };
                if (targetAlive || ownerAlive || frameAlive || controllerFrameAlive || cleanupError.Length != 0) record.Mark("cleanup", "FAIL", cleanupError + $" targetAlive={targetAlive} ownerAlive={ownerAlive} frameAlive={frameAlive} controllerFrameAlive={controllerFrameAlive}");
                else record.Mark("cleanup", "PASS");
                app.Shutdown();
            }
        }, DispatcherPriority.ApplicationIdle);
        try { app.Run(); }
        catch (Exception e) { record.Mark("cleanup", "FAIL", "dispatcher exception"); Console.Error.WriteLine(e); }
        int code = ValidationWait.ExitCode(record.Results.Values); record.Complete(code); return code;
    }
}

internal sealed class ObservingEnvironment(Dispatcher dispatcher) : IFrameEnvironment
{
    private readonly WpfFrameEnvironment inner = new(dispatcher);
    internal IFrameSurface? Last;
    public DateTime UtcNow => inner.UtcNow;
    public bool StillCurrent(IntPtr hwnd, uint pid) => inner.StillCurrent(hwnd, pid);
    public IFrameSurface Create(IntPtr owner) => Last = inner.Create(owner);
    public bool IsSafeAndOwned(IntPtr frame, IntPtr owner) => inner.IsSafeAndOwned(frame, owner);
    public bool IsAbove(IntPtr frame, IntPtr owner) => inner.IsAbove(frame, owner);
    public void Post(Action action) => inner.Post(action);
    public void Write(string message) => inner.Write(message);
    public void Error(string key, string message) => inner.Error(key, message);
}
