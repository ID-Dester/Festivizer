using System.IO;

namespace GarlandForWindows;

internal static class Diagnostics
{
    private static readonly string PathName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GarlandForWindows", "diagnostics.log");
    internal static void Write(string message)
    {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
            if (File.Exists(PathName) && new FileInfo(PathName).Length > 256 * 1024) File.WriteAllText(PathName, "");
            File.AppendAllText(PathName, $"{DateTime.Now:O} {message}{Environment.NewLine}");
        } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

