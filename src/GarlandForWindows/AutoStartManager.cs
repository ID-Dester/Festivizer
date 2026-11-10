using System.IO;
namespace GarlandForWindows;

internal interface IRunStore
{
    string? Read(string name);
    void Write(string name, string value);
    void Delete(string name);
}

internal sealed class AutoStartManager(IRunStore store, string executable)
{
    internal const string Name = "GarlandForWindows";
    internal (bool Enabled, bool Moved, string? Error) Read()
    {
        try {
            string? value = store.Read(Name);
            return (value != null, value != null && !string.Equals(value, AutoStartCommand.Create(executable), StringComparison.OrdinalIgnoreCase), null);
        } catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException or InvalidOperationException) { return (false, false, e.Message); }
    }
    internal void Apply(bool enabled)
    {
        if (enabled) store.Write(Name, AutoStartCommand.Create(executable));
        else store.Delete(Name);
    }
}


