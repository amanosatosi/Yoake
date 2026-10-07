using System.Reflection;
using Avalonia;
using Avalonia.Logging;
using Avalonia.Threading;
using Yoake.Core.Logging;
using Yoake.Native;

namespace Yoake.App;

internal static class Program
{
    private static StartupOptions _options = new();
    private static int _fatalPresented;

    [STAThread]
    public static int Main(string[] args)
    {
        var assembly = typeof(Program).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString() ?? "unknown";
        StartupDiagnostics.Initialize(version);
        try
        {
            _options = StartupOptions.Parse(args);
            if (_options.DiagnosticsDirectory is { } directory) StartupDiagnostics.Initialize(version, directory);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Fatal("AppDomain unhandled exception", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
            TaskScheduler.UnobservedTaskException += (_, e) => StartupDiagnostics.ReportFatal("Unobserved task exception (runtime policy unchanged)", e.Exception);
            WindowsNativeRuntime.ConfigurePackagedRuntime(AppContext.BaseDirectory);
            StartupDiagnostics.Checkpoint("Packaged native runtime configured");
            if (args.Length == 3 && args[0] == "--verify-editor") return EditorVerification.Run(args[1], args[2]);
            var builder = BuildAvaloniaApp(_options);
            StartupDiagnostics.Checkpoint("Avalonia builder created");
            if (_options.VerificationReport is not null) ArmVerificationTimeout();
            return builder.StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            Fatal("Desktop startup/dispatcher", exception);
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(new StartupOptions());
    private static AppBuilder BuildAvaloniaApp(StartupOptions options) => AppBuilder.Configure(() => new App(options))
        .UsePlatformDetect().LogToTrace()
        .AfterPlatformServicesSetup(_ => Logger.Sink = new StartupLogSink(Logger.Sink))
        .AfterSetup(_ =>
        {
            StartupDiagnostics.Checkpoint("Avalonia framework initialized");
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                Fatal("UI dispatcher unhandled exception", e.Exception);
                // Leave Handled false. A fatal UI exception must still terminate.
            };
        });

    internal static void Fatal(string context, Exception exception)
    {
        var path = StartupDiagnostics.ReportFatal(context, exception);
        try { _options.FinishVerification($"FAIL\n{context}\n{exception}\nCrash report: {path}\n"); } catch (Exception) { }
        if (!_options.NonInteractive && Interlocked.Exchange(ref _fatalPresented, 1) == 0)
        {
            try { WindowsNativeRuntime.ShowFatalError($"Yoake could not start. A crash report was written to:\n{path}\n\n{exception.Message}"); }
            catch (Exception) { /* The independent crash log is still available. */ }
        }
    }

    private static void ArmVerificationTimeout() => _ = Task.Run(async () =>
    {
        await Task.Delay(TimeSpan.FromSeconds(25));
        if (_options.VerificationFinished) return;
        Fatal("UI startup verification timeout", new TimeoutException("The real MainWindow did not complete opened/layout/dispatcher verification in 25 seconds."));
        Environment.Exit(2);
    });
}
