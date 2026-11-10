using System.IO;

namespace GarlandForWindows;

internal static class Diagnostics
{
    private static readonly Dictionary<string, DateTime> RecentErrors = new();
    private static readonly Dictionary<string, (string Value, DateTime Time)> RecentStates = new();
    private static readonly string PathName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GarlandForWindows", "diagnostics.log");
    internal static void WriteBuildIdentity()
    {
        try {
            var assembly = typeof(Diagnostics).Assembly;
            string exe = Environment.ProcessPath ?? "unknown";
            string dll = assembly.Location;
            string Hash(string path) => File.Exists(path) ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))) : "unavailable";
            Write($"Start version={assembly.GetName().Version} pid={Environment.ProcessId} exe={exe} exeSha256={Hash(exe)} assemblySha256={Hash(dll)}");
        } catch (Exception e) { Write($"Start identity unavailable: {e.Message}"); }
    }
    internal static void Write(string message)
    {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
            if (File.Exists(PathName) && new FileInfo(PathName).Length > 256 * 1024) File.WriteAllText(PathName, "");
            File.AppendAllText(PathName, $"{DateTime.Now:O} {message}{Environment.NewLine}");
        } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    internal static void Error(string key, string message)
    {
        lock (RecentErrors) {
            DateTime now = DateTime.UtcNow;
            if (RecentErrors.TryGetValue(key, out var last) && now - last < TimeSpan.FromSeconds(30)) return;
            RecentErrors[key] = now;
        }
        Write(message);
    }
    internal static void State(string key, string message)
    {
        lock (RecentStates) {
            var now = DateTime.UtcNow;
            if (RecentStates.TryGetValue(key, out var last) && last.Value == message && now - last.Time < TimeSpan.FromSeconds(30)) return;
            RecentStates[key] = (message, now);
        }
        Write(message);
    }
}
