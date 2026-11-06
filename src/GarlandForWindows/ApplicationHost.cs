using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace GarlandForWindows;

internal sealed class ApplicationHost : IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly GarlandOverlay taskbar = new(false), frame = new(true);
    private readonly DispatcherTimer reconcile, animation;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Native.WinEventProc callback;
    private readonly List<IntPtr> hooks = [];
    private HwndSource? messages;
    private Forms.NotifyIcon? tray;
    private Forms.ToolStripMenuItem? toggle;
    private bool enabled = true, disposed, pending;
    private IntPtr lastForeground;
    private uint taskbarCreated;

    internal ApplicationHost(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        callback = OnWinEvent;
        reconcile = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => Refresh(), dispatcher);
        animation = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => Animate(), dispatcher);
    }
    internal void Start()
    {
        var parameters = new HwndSourceParameters("Garland message receiver") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) };
        // A hidden top-level window receives the Explorer broadcast, unlike a message-only window.
        parameters.ParentWindow = IntPtr.Zero;
        messages = new HwndSource(parameters); messages.AddHook(MessageHook);
        taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
        var menu = new Forms.ContextMenuStrip();
        toggle = new Forms.ToolStripMenuItem("Выключить гирлянды");
        toggle.Click += (_, _) => { enabled = !enabled; toggle.Text = enabled ? "Выключить гирлянды" : "Включить гирлянды"; Refresh(); };
        menu.Items.Add(toggle); menu.Items.Add("Выход", null, (_, _) => System.Windows.Application.Current.Shutdown());
        tray = new Forms.NotifyIcon { Text = "Garland for Windows", Icon = System.Drawing.SystemIcons.Application, ContextMenuStrip = menu, Visible = true };
        Hook(0x0003, 0x0003); // foreground
        Hook(0x000A, 0x000B); // move/size
        Hook(0x8000, 0x8003); // create, destroy, show, hide
        Hook(0x800B, 0x800B); // location
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
        reconcile.Start(); Refresh();
    }
    private void Hook(uint min, uint max)
    {
        IntPtr hook = Native.SetWinEventHook(min, max, IntPtr.Zero, callback, 0, 0, 0);
        if (hook == IntPtr.Zero) throw new InvalidOperationException("Unable to register window event hook.");
        hooks.Add(hook);
    }
    private void DisplayChanged(object? sender, EventArgs e) => QueueRefresh();
    private void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int objectId, int childId, uint thread, uint time)
    {
        try {
            if (objectId != 0 || childId != 0) return;
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == Environment.ProcessId) return;
            if (ev == 3 || hwnd == Native.GetForegroundWindow() || Native.ClassName(hwnd) == "Shell_TrayWnd") QueueRefresh();
        } catch (Exception ex) { Trace.WriteLine(ex); }
    }
    private void QueueRefresh()
    {
        if (disposed) return;
        dispatcher.BeginInvoke(() => { if (disposed || pending) return; pending = true; dispatcher.BeginInvoke(() => { pending = false; if (!disposed) Refresh(); }, DispatcherPriority.Background); });
    }
    private IntPtr MessageHook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if ((uint)msg == taskbarCreated && tray != null) { Diagnostics.Write("Explorer taskbar created"); tray.Visible = false; tray.Visible = true; QueueRefresh(); }
        return IntPtr.Zero;
    }
    private static bool IsCandidate(IntPtr h)
    {
        if (h == IntPtr.Zero || !Native.IsWindow(h) || !Native.IsWindowVisible(h) || Native.IsIconic(h)) return false;
        if (Native.DwmGetWindowAttribute(h, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        Native.GetWindowThreadProcessId(h, out uint pid);
        if (pid == Environment.ProcessId) return false;
        string name = Native.ClassName(h);
        return name is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW" or "Windows.UI.Core.CoreWindow" or "XamlExplorerHostIslandWindow" or "NotifyIconOverflowWindow");
    }
    private static bool Fullscreen(IntPtr h, Native.Rect rect)
    {
        var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
        if (!Native.GetMonitorInfo(Native.MonitorFromWindow(h, 2), ref info) || (info.Flags & 1) == 0) return false;
        long style = Native.GetWindowLongPtr(h, Native.GWL_STYLE).ToInt64();
        return GeometryPolicy.IsFullscreen(rect, info.Monitor, style);
    }
    private void Refresh()
    {
        if (disposed) return;
        if (!enabled) { taskbar.Hide(); frame.Hide(); animation.Stop(); return; }
        IntPtr foreground = Native.GetForegroundWindow();
        if (foreground != lastForeground) { lastForeground = foreground; Diagnostics.Write($"Foreground HWND: 0x{foreground.ToInt64():X}"); }
        bool candidate = IsCandidate(foreground);
        Native.Rect bounds = default;
        bool hasBounds = candidate && Native.Bounds(foreground, out bounds);
        bool fullscreen = hasBounds && Fullscreen(foreground, bounds);
        try { if (!fullscreen) RefreshTaskbar(); else taskbar.Hide(); }
        catch (Exception error) { taskbar.Hide(); Diagnostics.Write($"Taskbar overlay error: {error.Message}"); }
        long ex = candidate ? Native.GetWindowLongPtr(foreground, Native.GWL_EXSTYLE).ToInt64() : 0;
        bool eligible = hasBounds && !fullscreen && !Native.IsZoomed(foreground) &&
            Native.GetAncestor(foreground, 2) == foreground && Native.GetWindow(foreground, 4) == IntPtr.Zero &&
            (ex & Native.WS_EX_TOOLWINDOW) == 0 && bounds.Width >= 150 && bounds.Height >= 100;
        if (eligible) {
            const int margin = 12;
            bounds.Left -= margin; bounds.Top -= margin; bounds.Right += margin; bounds.Bottom += margin;
            // Put the decoration immediately above the active window, never in the global topmost band.
            frame.Place(bounds, Native.HWND_TOP);
        } else frame.Hide();
        if (taskbar.IsVisible || frame.IsVisible) { if (!animation.IsEnabled) animation.Start(); }
        else animation.Stop();
    }
    private void RefreshTaskbar()
    {
        IntPtr h = Native.FindWindow("Shell_TrayWnd", null);
        var data = new Native.AppBarData { Size = Marshal.SizeOf<Native.AppBarData>(), Hwnd = h };
        var monitor = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
        if (h == IntPtr.Zero || !Native.IsWindowVisible(h) || Native.SHAppBarMessage(5, ref data) == UIntPtr.Zero || data.Edge != 3 ||
            !Native.GetWindowRect(h, out var actual) || !Native.GetMonitorInfo(Native.MonitorFromWindow(h, 2), ref monitor)) { taskbar.Hide(); return; }
        // Auto-hidden taskbars move below the monitor; the two-pixel reveal strip is not a visible panel.
        if (actual.Top >= monitor.Monitor.Bottom - 2 || actual.Height < 10 || actual.Top > data.Rect.Top + 4) { taskbar.Hide(); return; }
        var rect = new Native.Rect { Left = actual.Left, Right = actual.Right, Top = actual.Top - 14, Bottom = actual.Top + 16 };
        taskbar.Place(rect, new IntPtr(-1)); // taskbar decoration follows the shell's topmost band
    }
    private void Animate() { double t = clock.Elapsed.TotalSeconds; if (taskbar.IsVisible) taskbar.Animate(t); if (frame.IsVisible) frame.Animate(t); }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        reconcile.Stop(); animation.Stop(); Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        foreach (IntPtr hook in hooks) Native.UnhookWinEvent(hook); hooks.Clear();
        tray?.Dispose(); messages?.Dispose(); taskbar.Close(); frame.Close();
        GC.KeepAlive(callback);
    }
}

