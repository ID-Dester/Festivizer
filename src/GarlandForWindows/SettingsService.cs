using System.Text.Json;
using System.IO;

namespace GarlandForWindows;

internal sealed record Settings
{
    public int SchemaVersion { get; init; } = 1;
    public bool GarlandsEnabled { get; init; } = true;
    public bool TaskbarEnabled { get; init; } = true;
    public bool WindowFrameEnabled { get; init; } = true;
    public string Theme { get; init; } = "classicChristmas";
    public int BrightnessPercent { get; init; } = 100;
    public int SpeedPercent { get; init; } = 100;
    internal static readonly string[] Themes = ["classicChristmas", "warmWhite", "coldWhite", "multicolor"];
}

internal sealed class SettingsService(string path)
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    internal bool FutureVersion { get; private set; }
    internal static SettingsService CreateDefault() => new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GarlandForWindows", "settings.json"));
    internal Settings Load()
    {
        if (!File.Exists(path)) return new();
        try {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Expected an object.");
            if (root.TryGetProperty("schemaVersion", out var v) && v.GetInt32() > 1) { FutureVersion = true; return new(); }
            var s = JsonSerializer.Deserialize<Settings>(root, Options) ?? throw new JsonException("Empty settings.");
            return s with { SchemaVersion = 1, Theme = Settings.Themes.Contains(s.Theme) ? s.Theme : "classicChristmas", BrightnessPercent = Math.Clamp(s.BrightnessPercent, 10, 100), SpeedPercent = Math.Clamp(s.SpeedPercent, 25, 200) };
        } catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or OverflowException) {
            try { File.Copy(path, Path.Combine(Path.GetDirectoryName(path)!, "settings.invalid.json"), true); } catch (Exception copyError) when (copyError is IOException or UnauthorizedAccessException) { Diagnostics.Write($"Could not back up settings: {copyError.Message}"); }
            Diagnostics.Write($"Invalid settings: {ex.Message}"); return new();
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Diagnostics.Write($"Could not read settings: {ex.Message}"); return new(); }
    }
    internal void Save(Settings value, bool allowReset = false)
    {
        if (FutureVersion && !allowReset) throw new InvalidOperationException("Файл настроек создан более новой версией. Для сохранения выберите «По умолчанию» и примените сброс.");
        string dir = Path.GetDirectoryName(path)!; Directory.CreateDirectory(dir);
        string temp = Path.Combine(dir, $"settings.{Guid.NewGuid():N}.tmp");
        try { File.WriteAllText(temp, JsonSerializer.Serialize(value, Options)); File.Move(temp, path, true); FutureVersion = false; }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
