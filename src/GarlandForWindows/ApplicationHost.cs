using System.Diagnostics;
using System.IO;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace GarlandForWindows;

internal sealed class ApplicationHost : IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly GarlandOverlay taskbar = new(false);
    private readonly FrameOverlayController frame;
    private readonly DispatcherTimer reconcile, animation, moveReconcile;
    private readonly RefreshScheduler refreshScheduler;
    private readonly MenuStateTracker menuState = new(new MenuNative());
    private IntPtr movingWindow;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly SettingsService settingsService = SettingsService.CreateDefault();
    private Settings settings = new();
    private SettingsWindow? settingsWindow;
    private Forms.ToolStripMenuItem? taskbarItem, frameItem;
    private double phase, lastAnimationTime;
    private readonly Native.WinEventProc callback;
    private readonly HookRegistry hooks = new(Native.UnhookWinEvent, Diagnostics.Write);
    private HwndSource? messages;
    private Forms.NotifyIcon? tray;
    private Forms.ToolStripMenuItem? toggle;
    private bool disposed;
    private bool trayMenuOpen;
    private IntPtr lastForeground;
    private uint taskbarCreated;

    internal ApplicationHost(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        frame = new FrameOverlayController(new WpfFrameEnvironment(dispatcher), QueueRefresh);
        refreshScheduler = new RefreshScheduler(action => dispatcher.BeginInvoke(action, DispatcherPriority.Render), Refresh);
        callback = OnWinEvent;
        reconcile = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => Refresh(), dispatcher);
        animation = new DispatcherTimer(TimeSpan.FromMilliseconds(34), DispatcherPriority.Render, (_, _) => Animate(), dispatcher);
        moveReconcile = new DispatcherTimer(TimeSpan.FromMilliseconds(34), DispatcherPriority.Render, (_, _) => {
            if (disposed || movingWindow == IntPtr.Zero || movingWindow != Native.GetForegroundWindow() || !Native.IsWindow(movingWindow)) { StopMoveReconcile(); return; }
            Refresh(false);
        }, dispatcher);
    }
    internal void Start()
    {
        settings = settingsService.Load();
        taskbar.Configure(settings); frame.Configure(settings);
        var parameters = new HwndSourceParameters("Garland message receiver") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) };
        // A hidden top-level window receives the Explorer broadcast, unlike a message-only window.
        parameters.ParentWindow = IntPtr.Zero;
        messages = new HwndSource(parameters); messages.AddHook(MessageHook);
        taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
        if (taskbarCreated == 0) throw new InvalidOperationException("Could not register TaskbarCreated.");
        var menu = new Forms.ContextMenuStrip();
        menu.Opening += (_, _) => { trayMenuOpen = true; QueueRefresh(); };
        menu.Closed += (_, _) => { trayMenuOpen = false; QueueRefresh(); };
        toggle = new Forms.ToolStripMenuItem("Выключить гирлянды");
        toggle.Click += (_, _) => ApplyTray(settings with { GarlandsEnabled = !settings.GarlandsEnabled });
        taskbarItem = new Forms.ToolStripMenuItem("Панель задач"); taskbarItem.Click += (_, _) => ApplyTray(settings with { TaskbarEnabled = !settings.TaskbarEnabled });
        frameItem = new Forms.ToolStripMenuItem("Рамка активного окна"); frameItem.Click += (_, _) => ApplyTray(settings with { WindowFrameEnabled = !settings.WindowFrameEnabled });
        menu.Items.Add(toggle); menu.Items.Add(taskbarItem); menu.Items.Add(frameItem);
        menu.Items.Add("Настройки…", null, (_, _) => OpenSettings());
        menu.Items.Add("Выход", null, (_, _) => System.Windows.Application.Current.Shutdown());
        tray = new Forms.NotifyIcon { Text = "Garland for Windows", Icon = System.Drawing.SystemIcons.Application, ContextMenuStrip = menu, Visible = true };
        Hook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND);
        Hook(Native.EVENT_SYSTEM_MENUSTART, Native.EVENT_SYSTEM_MENUPOPUPEND);
        Hook(Native.EVENT_SYSTEM_MOVESIZESTART, Native.EVENT_SYSTEM_MOVESIZEEND);
        Hook(Native.EVENT_OBJECT_CREATE, Native.EVENT_OBJECT_HIDE);
        Hook(Native.EVENT_OBJECT_LOCATIONCHANGE, Native.EVENT_OBJECT_LOCATIONCHANGE);
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
        UpdateMenu(); reconcile.Start(); Refresh();
        if (settingsService.FutureVersion) System.Windows.MessageBox.Show("Файл настроек создан более новой версией. Изменения заблокированы до явного сброса в настройках.", "Garland for Windows");
    }
    private void OpenSettings()
    {
        frame.Clear("settings opened");
        if (settingsWindow != null) { if (settingsWindow.WindowState == System.Windows.WindowState.Minimized) settingsWindow.WindowState = System.Windows.WindowState.Normal; settingsWindow.Activate(); return; }
        settingsWindow = new SettingsWindow(settings, settingsService.FutureVersion, ApplySettings);
        settingsWindow.Activated += (_, _) => QueueRefresh();
        settingsWindow.Deactivated += (_, _) => QueueRefresh();
        settingsWindow.Closed += (_, _) => { settingsWindow = null; QueueRefresh(); }; settingsWindow.Show(); QueueRefresh();
    }
    private void UpdateMenu()
    {
        if (toggle != null) toggle.Text = settings.GarlandsEnabled ? "Выключить гирлянды" : "Включить гирлянды";
        if (taskbarItem != null) taskbarItem.Checked = settings.TaskbarEnabled;
        if (frameItem != null) frameItem.Checked = settings.WindowFrameEnabled;
    }
    private void ApplyTray(Settings desired)
    {
        string? error = ApplySettings(desired, false, null);
        if (error != null) System.Windows.MessageBox.Show(error, "Не удалось сохранить настройки");
        else settingsWindow?.SyncTray(settings);
    }
    private string? ApplySettings(Settings desired, bool reset, bool? startup)
    {
        try { settingsService.Save(desired, reset); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { return "Настройки не сохранены: " + error.Message; }
        AdvancePhase(); settings = desired; taskbar.Configure(settings); frame.Configure(settings); UpdateMenu(); Refresh();
        if (startup.HasValue) {
            try { AutoStartService.Apply(startup.Value); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException) {
                var actual = AutoStartService.Read();
                return $"Настройки сохранены, но автозапуск не изменён: {error.Message}. Состояние: {(actual.Error != null ? "недоступно" : actual.Enabled ? "зарегистрирован" : "выключен")}.";
            }
        }
        return null;
    }
    private void Hook(uint min, uint max)
    {
        IntPtr hook = Native.SetWinEventHook(min, max, IntPtr.Zero, callback, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
        hooks.Add(hook);
    }
    private void DisplayChanged(object? sender, EventArgs e) => QueueRefresh();
    private void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int objectId, int childId, uint thread, uint time)
    {
        try {
            var kind = WinEventRouter.Route(ev, objectId, childId);
            if (kind is WinEventRouter.Kind.Foreground or WinEventRouter.Kind.Menu) { QueueRefresh(); return; }
            if (kind == WinEventRouter.Kind.Ignore) return;
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == Environment.ProcessId) return;
            if (kind == WinEventRouter.Kind.MoveSize) {
                dispatcher.BeginInvoke(() => {
                    if (disposed) return;
                    if (ev == Native.EVENT_SYSTEM_MOVESIZESTART && hwnd == Native.GetForegroundWindow() && settings.GarlandsEnabled && settings.WindowFrameEnabled) { movingWindow = hwnd; moveReconcile.Start(); }
                    else if (ev == Native.EVENT_SYSTEM_MOVESIZEEND && movingWindow == hwnd) StopMoveReconcile();
                    QueueRefresh();
                }, DispatcherPriority.Send);
                return;
            }
            if (ev == Native.EVENT_SYSTEM_FOREGROUND || hwnd == Native.GetForegroundWindow() || Native.ClassName(hwnd) == "Shell_TrayWnd") QueueRefresh();
        } catch (Exception ex) { Trace.WriteLine(ex); }
    }
    private void QueueRefresh()
    {
        try { refreshScheduler.Request(); }
        catch (InvalidOperationException) { /* Dispatcher is shutting down. */ }
    }
    private void StopMoveReconcile() { moveReconcile.Stop(); movingWindow = IntPtr.Zero; }
    private IntPtr MessageHook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if ((uint)msg == taskbarCreated && tray != null) { Diagnostics.Write("Explorer taskbar created"); tray.Visible = false; tray.Visible = true; QueueRefresh(); }
        return IntPtr.Zero;
    }
    private void Refresh() => Refresh(true);
    private void Refresh(bool verifyOrder)
    {
        if (disposed) return;
        if (!settings.GarlandsEnabled) { StopMoveReconcile(); taskbar.Hide(); frame.Clear("disabled"); animation.Stop(); return; }
        IntPtr foreground = Native.GetForegroundWindow();
        if (foreground != lastForeground) { if (movingWindow != foreground) StopMoveReconcile(); frame.Clear("foreground changed"); lastForeground = foreground; Diagnostics.Write($"Foreground HWND: 0x{foreground.ToInt64():X}"); }
        if (!settings.WindowFrameEnabled) StopMoveReconcile();
        WindowSnapshot window = WindowTracker.Read(foreground);
        MenuSnapshot menu = menuState.Read();
        bool menuClosed = menu.State == MenuState.Closed && menu.Context.Foreground == foreground && menu.Context.ProcessId == window.ProcessId;
        if (menu.State == MenuState.Unknown) Diagnostics.Error("menu-unknown", $"Menu state unknown foreground=0x{foreground.ToInt64():X} error={menu.Error}");
        var bounds = window.Bounds;
        try { if (VisibilityPolicy.Taskbar(settings, true, window.Fullscreen, true)) RefreshTaskbar(); else taskbar.Hide(); }
        catch (Exception error) { taskbar.Hide(); Diagnostics.Error("taskbar", $"Taskbar overlay error: {error.Message}"); }
        // FrameOverlayController alone decides health after creation, owner, styles and order checks.
        try { if (VisibilityPolicy.MayUpdateFrame(settings, window.Eligible, window.Fullscreen, menuClosed ? MenuState.Closed : MenuState.Unknown, trayMenuOpen)) {
            int margin = Scale(12, foreground);
            bounds.Left -= margin; bounds.Top -= margin; bounds.Right += margin; bounds.Bottom += margin;
            frame.Update(window, bounds, verifyOrder);
        } else frame.Clear(trayMenuOpen ? "tray menu open" : !menuClosed ? "menu open or state unknown" : "hidden by policy"); } catch (Exception error) { frame.Clear("host error"); Diagnostics.Error("frame", $"Frame overlay error: {error.Message}"); }
        Diagnostics.State("visibility", $"Visibility hwnd=0x{foreground.ToInt64():X} fullscreen={window.Fullscreen} eligible={window.Eligible} menu={menu.State} menuClosed={menuClosed} trayOpen={trayMenuOpen} enabled={settings.GarlandsEnabled} taskbarEnabled={settings.TaskbarEnabled} frameEnabled={settings.WindowFrameEnabled} taskbarVisible={taskbar.IsVisible} frameVisible={frame.IsVisible}");
        if (taskbar.IsVisible || frame.IsVisible) { if (!animation.IsEnabled) { lastAnimationTime = clock.Elapsed.TotalSeconds; animation.Start(); } }
        else animation.Stop();
    }
    private void RefreshTaskbar()
    {
        if (TaskbarTracker.TryGetOverlayBounds(out var rect)) taskbar.Place(rect, new IntPtr(-1));
        else taskbar.Hide();
    }
    private static int Scale(int dip, IntPtr h) => VisibilityPolicy.DipToPixels(dip, Native.GetDpiForWindow(h));
    private void AdvancePhase() { double now = clock.Elapsed.TotalSeconds; phase += (now - lastAnimationTime) * settings.SpeedPercent / 100.0; lastAnimationTime = now; }
    private void Animate() { AdvancePhase(); try { if (taskbar.IsVisible) taskbar.Animate(phase); } catch (Exception e) { taskbar.Hide(); Diagnostics.Write($"Taskbar animation: {e.Message}"); } try { if (frame.IsVisible) frame.Animate(phase); } catch (Exception e) { frame.Clear("animation error"); Diagnostics.Write($"Frame animation: {e.Message}"); } }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        refreshScheduler.Dispose(); StopMoveReconcile(); reconcile.Stop(); animation.Stop(); Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        hooks.Dispose();
        foreach (Action close in new Action[] { () => settingsWindow?.Close(), () => tray?.Dispose(), () => messages?.Dispose(), taskbar.Close, frame.Dispose }) { try { close(); } catch (Exception e) { Diagnostics.Write($"Cleanup: {e.Message}"); } }
        GC.KeepAlive(callback);
    }
}
