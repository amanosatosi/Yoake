using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Yoake.Core.Logging;

// Process bootstrap diagnostics, independent of settings and the application
// logger. This is deliberately not document/editor state.
public static class StartupDiagnostics
{
    private static readonly object Gate = new();
    private static string? _directory, _fatalPath;
    private static string _version = "unknown";
    private static bool _startupComplete;
    public static int FrameworkErrorCount { get; private set; }
    public static string? LogDirectory => _directory;

    public static void Initialize(string version, string? directory = null)
    {
        _version = version;
        foreach (var candidate in new[] { directory,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Yoake", "logs"),
            Path.Combine(Path.GetTempPath(), "Yoake", "logs") })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            try { Directory.CreateDirectory(candidate); _directory = candidate; Checkpoint("Program entered\n" + EnvironmentDetails()); return; }
            catch (Exception) { /* Try the independent user-writable fallback. */ }
        }
    }

    private static string EnvironmentDetails() =>
        $"Application: {_version}\nOS: {RuntimeInformation.OSDescription}\nRuntime: {RuntimeInformation.FrameworkDescription}" +
        $"\nOS architecture: {RuntimeInformation.OSArchitecture}\nProcess architecture: {RuntimeInformation.ProcessArchitecture}" +
        $"\nDynamic code supported: {RuntimeFeature.IsDynamicCodeSupported} (false in NativeAOT)\nProcess: {Environment.ProcessId}";

    public static void Checkpoint(string message)
    {
        lock (Gate)
        {
            if (_startupComplete || _directory is null) return;
            try { File.AppendAllText(Path.Combine(_directory, "startup.log"), $"{DateTimeOffset.Now:O} [{Environment.ProcessId}] {message}{Environment.NewLine}"); }
            catch (Exception) { /* Diagnostics must never become the startup failure. */ }
        }
    }

    public static void FrameworkMessage(string message, bool error)
    {
        lock (Gate)
        {
            if (_startupComplete) return;
            if (error) FrameworkErrorCount++;
            Checkpoint("Avalonia: " + message);
        }
    }

    public static void Complete() { Checkpoint("Startup completed"); _startupComplete = true; }

    public static string ReportFatal(string context, Exception exception)
    {
        lock (Gate)
        {
            var report = $"{DateTimeOffset.Now:O}\n{EnvironmentDetails()}\nContext: {context}\n" +
                $"Exception type: {exception.GetType().FullName}\nMessage: {exception.Message}\n\n{exception}\n";
            if (_fatalPath is not null)
            {
                try { File.AppendAllText(_fatalPath, "\nAdditional exception:\n" + report); return _fatalPath; }
                catch (Exception) { _fatalPath = null; }
            }
            foreach (var directory in new[] { _directory, Path.Combine(Path.GetTempPath(), "Yoake", "logs") })
            {
                if (directory is null) continue;
                try
                {
                    Directory.CreateDirectory(directory);
                    var path = Path.Combine(directory, $"crash-{Environment.ProcessId}.log");
                    File.WriteAllText(path, report, new UTF8Encoding(false));
                    _fatalPath = path;
                    Checkpoint($"FATAL {context}: {exception.Message}; report: {path}");
                    return path;
                }
                catch (Exception) { }
            }
            return "Crash report could not be written to either the user log directory or the temporary directory.";
        }
    }
}
