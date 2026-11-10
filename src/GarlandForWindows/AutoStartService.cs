using Microsoft.Win32;
namespace GarlandForWindows;

internal sealed class RegistryRunStore : IRunStore
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public string? Read(string name) { using var key = Registry.CurrentUser.OpenSubKey(Key); return key?.GetValue(name) as string; }
    public void Write(string name, string value) { using var key = Registry.CurrentUser.CreateSubKey(Key, true) ?? throw new InvalidOperationException("Не удалось открыть раздел автозапуска."); key.SetValue(name, value, RegistryValueKind.String); }
    public void Delete(string name) { using var key = Registry.CurrentUser.OpenSubKey(Key, true); key?.DeleteValue(name, false); }
}

internal static class AutoStartService
{
    private static AutoStartManager Create() => new(new RegistryRunStore(), Environment.ProcessPath!);
    internal static (bool Enabled, bool Moved, string? Error) Read() => Create().Read();
    internal static void Apply(bool enabled) => Create().Apply(enabled);
}

