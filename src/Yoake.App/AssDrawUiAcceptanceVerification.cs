using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Yoake.Core.Logging;
using Yoake.UI;
using Yoake.UI.ViewModels;

namespace Yoake.App;

// One packaged smoke test: Tools -> ASSDraw3 launches our bundled, responsive EXE.
// Path discovery, error handling and settings already have focused Core/UI tests.
internal sealed class AssDrawUiAcceptanceVerification(
    IClassicDesktopStyleApplicationLifetime desktop,
    MainWindow window,
    MainWindowViewModel model,
    StartupOptions options)
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool _ready;
    private int _stage;
    private long _launchAt;
    private string _executable = "";
    private Process? _process;

    public void Start()
    {
        window.LayoutUpdated += (_, _) => _ready = window.IsVisible && window.Bounds.Width > 0;
        _timer.Tick += Tick;
        _timer.Start();
    }

    private void Tick(object? sender, EventArgs args)
    {
        if (!_ready) return;
        options.MarkStartupVerified();
        try
        {
            if (_stage == 0)
            {
                _executable = Path.Combine(AppContext.BaseDirectory, "ASSDraw3.exe");
                Require(File.Exists(_executable), "The packaged ASSDraw3.exe is missing.");
                Require(model.AssDrawExecutable is null, "Default launch must not need configuration.");
                // Allow the native Tools menu to materialize and bind its
                // command before checking the actual menu item.
                var toolsMenu = window.FindControl<MenuItem>("ToolsMenu");
                Require(toolsMenu is not null, "The packaged Tools menu is missing.");
                toolsMenu.IsSubMenuOpen = true;
                _stage = 1;
                return;
            }

            if (_stage == 1)
            {
                var menu = window.FindControl<MenuItem>("AssDrawMenu");
                var command = menu?.Command;
                Require(command is not null && command.CanExecute(menu!.CommandParameter),
                    "Tools -> ASSDraw3 must be wired to an enabled command.");
                // Exercise the shipping menu's bound command, not a synthetic
                // KeyDown or unrelated dialog / subtitle workflow.
                command!.Execute(menu!.CommandParameter);
                _launchAt = Stopwatch.GetTimestamp();
                _stage = 2;
                return;
            }

            if (_stage == 2)
            {
                foreach (var candidate in Process.GetProcessesByName("ASSDraw3"))
                {
                    try
                    {
                        if (_process is null && !candidate.HasExited &&
                            string.Equals(candidate.MainModule?.FileName, _executable,
                                StringComparison.OrdinalIgnoreCase) &&
                            candidate.MainWindowHandle != 0 && candidate.Responding)
                            _process = candidate;
                    }
                    catch (Win32Exception) { /* Ignore unrelated inaccessible processes. */ }
                    catch (InvalidOperationException) { /* A process may exit during inspection. */ }
                    finally { if (!ReferenceEquals(candidate, _process)) candidate.Dispose(); }
                }
                if (_process is null)
                {
                    Require(Stopwatch.GetElapsedTime(_launchAt) < TimeSpan.FromSeconds(20),
                        "Tools -> ASSDraw3 did not open the bundled executable. Status: " + model.SubtitleStatus);
                    return;
                }
                // The process may create its window before the async command
                // has published SubtitleStatus. Window identity is the proof.
                _stage = 3;
                return;
            }

            Require(_process is not null && !_process.HasExited && _process.Responding,
                "The bundled ASSDraw window closed or stopped responding.");
            Cleanup();
            _timer.Stop();
            options.FinishVerification("PASS: Tools -> ASSDraw3 launches the real bundled, responsive ASSDraw3.exe without configuration.\n");
            StartupDiagnostics.Complete();
            desktop.Shutdown(0);
        }
        catch (Exception error)
        {
            _timer.Stop();
            Cleanup();
            Program.Fatal("ASSDraw launch smoke test at stage " + _stage, error);
            desktop.Shutdown(1);
        }
    }

    private void Cleanup()
    {
        if (_process is null) return;
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill();
                _process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException) { /* The window exited during cleanup. */ }
        catch (Win32Exception) { /* The runner will reap the process on job exit. */ }
        finally { _process.Dispose(); _process = null; }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
