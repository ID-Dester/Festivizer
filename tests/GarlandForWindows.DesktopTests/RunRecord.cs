using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

internal sealed class RunRecord : IDisposable
{
    internal readonly Dictionary<string, string> Results = new() { ["desktop"]="NOT_RUN", ["low_level"]="NOT_RUN", ["foreground"]="NOT_RUN", ["popup"]="NOT_RUN", ["controller"]="NOT_RUN", ["cleanup"]="NOT_RUN" };
    internal readonly Dictionary<string, object?> Details = new();
    private readonly DateTimeOffset started = DateTimeOffset.Now;
    private readonly Stopwatch watch = Stopwatch.StartNew();
    private readonly TextWriter originalOut = Console.Out, originalError = Console.Error;
    private readonly StreamWriter output, error;
    private readonly string root;
    internal string RunId { get; }
    internal string RunDirectory { get; }
    internal RunRecord(string[] args)
    {
        root = FindRoot(); RunId = Environment.GetEnvironmentVariable("FESTIVIZER_RUN_ID") ?? $"{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}";
        RunDirectory = Environment.GetEnvironmentVariable("FESTIVIZER_RUN_DIR") ?? Path.Combine(root, "artifacts", "validation", "0.2.3", RunId); Directory.CreateDirectory(RunDirectory);
        output = new StreamWriter(Path.Combine(RunDirectory, "stdout.txt")) { AutoFlush = true };
        error = new StreamWriter(Path.Combine(RunDirectory, "stderr.txt")) { AutoFlush = true };
        Console.SetOut(new Tee(originalOut, output)); Console.SetError(new Tee(originalError, error));
        Details["arguments"] = args; Console.WriteLine($"RUN {RunId} directory={RunDirectory}");
    }
    private static string FindRoot()
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d != null; d = d.Parent) if (File.Exists(Path.Combine(d.FullName, "GarlandForWindows.sln"))) return d.FullName;
        throw new InvalidOperationException("Workspace root not found; cannot preserve test results.");
    }
    internal void Mark(string name, string status, string detail = "") { if (Results.GetValueOrDefault(name) != "FAIL") Results[name] = status; Console.WriteLine($"{status} {name} {detail}"); File.WriteAllText(Path.Combine(RunDirectory, "progress.json"), JsonSerializer.Serialize(new { phase = name, status = Results[name], detail, time = DateTimeOffset.Now })); }
    internal void Complete(int code)
    {
        watch.Stop();
        string dll = Assembly.GetExecutingAssembly().Location;
        var source = Directory.EnumerateFiles(Path.Combine(root, "src", "GarlandForWindows"), "*.cs", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "tests", "GarlandForWindows.DesktopTests"), "*.cs", SearchOption.TopDirectoryOnly))
            .Append(Path.Combine(root, "tests", "GarlandForWindows.DesktopTests", "GarlandForWindows.DesktopTests.csproj"))
            .Append(Path.Combine(root, "src", "GarlandForWindows", "app.manifest"))
            .OrderBy(x => x).ToDictionary(x => Path.GetRelativePath(root, x), Hash);
        var summary = new {
            runId = RunId, started, ended = DateTimeOffset.Now, durationMs = watch.ElapsedMilliseconds,
            os = RuntimeInformation.OSDescription, runtime = RuntimeInformation.FrameworkDescription, sdk = SdkVersion(),
#if DEBUG
            configuration = "Debug",
#else
            configuration = "Release",
#endif
            command = Environment.CommandLine, processId = Environment.ProcessId, testDll = dll,
            testVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(), testDllSha256 = Hash(dll),
            sourceHashes = source, results = Results, counts = Results.Values.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count()), exitCode = code, details = Details
        };
        Console.WriteLine($"RESULT exit={code} " + string.Join(" ", Results.Select(x => $"{x.Key}={x.Value}")));
        File.WriteAllText(Path.Combine(RunDirectory, "summary.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static string SdkVersion()
    {
        try {
            using var p = Process.Start(new ProcessStartInfo("dotnet", "--version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
            if (p == null || !p.WaitForExit(3000)) return "unavailable";
            return p.StandardOutput.ReadToEnd().Trim();
        } catch { return "unavailable"; }
    }
    public void Dispose() { Console.SetOut(originalOut); Console.SetError(originalError); output.Dispose(); error.Dispose(); }
    private sealed class Tee(TextWriter a, TextWriter b) : TextWriter
    {
        public override System.Text.Encoding Encoding => a.Encoding;
        public override void Write(char c) { a.Write(c); b.Write(c); }
        public override void WriteLine(string? s) { a.WriteLine(s); b.WriteLine(s); }
        public override void Flush() { a.Flush(); b.Flush(); }
    }
}
