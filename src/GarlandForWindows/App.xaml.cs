using System.Threading;
using System.Windows;
namespace GarlandForWindows;
public partial class App : System.Windows.Application
{
    private Mutex? mutex;
    private ApplicationHost? host;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        mutex = new Mutex(true, @"Local\GarlandForWindows.MVP", out bool created);
        if (!created) { mutex.Dispose(); mutex = null; Shutdown(); return; }
        try { Diagnostics.Write("Start"); host = new ApplicationHost(Dispatcher); host.Start(); }
        catch (Exception ex) { Diagnostics.Write($"Startup error: {ex}"); host?.Dispose(); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        host?.Dispose();
        Diagnostics.Write("Exit");
        if (mutex != null) { mutex.ReleaseMutex(); mutex.Dispose(); }
        base.OnExit(e);
    }
}

