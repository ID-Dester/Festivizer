namespace GarlandForWindows;

internal static class AutoStartCommand
{
    internal static string Create(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('"') || !System.IO.Path.IsPathFullyQualified(path) || path.Length + 2 > 260)
            throw new InvalidOperationException("Путь приложения недопустим или слишком длинен для автозапуска.");
        return $"\"{path}\"";
    }
}
