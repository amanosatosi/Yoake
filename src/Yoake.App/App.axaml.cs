using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Yoake.Core.Commands;
using Yoake.Core.Jobs;
using Yoake.Core.Logging;
using Yoake.Core.Settings;
using Yoake.Core.Undo;
using Yoake.Core.Workspace;
using Yoake.UI;
using Yoake.UI.Services;
using Yoake.UI.ViewModels;

namespace Yoake.App;

public partial class App : Application
{
    private BackgroundJobService? _backgroundJobs;
    private readonly StartupOptions _startup;
    public App() : this(new StartupOptions()) { }
    internal App(StartupOptions startup) => _startup = startup;

    public override void Initialize()
    {
        StartupDiagnostics.Checkpoint("App XAML initializing");
        AvaloniaXamlLoader.Load(this);
        StartupDiagnostics.Checkpoint("App XAML initialized");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settingsPath = _startup.ProfileDirectory is { } profile ? System.IO.Path.Combine(profile, "settings.json") : SettingsPathResolver.Resolve(
                AppContext.BaseDirectory,
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            StartupDiagnostics.Checkpoint($"Settings path resolved: {settingsPath}");
            var settingsStore = new SettingsStore(settingsPath);
            var settingsWarnings = new List<string>();
            var settings = settingsStore.Load(message => { settingsWarnings.Add(message); StartupDiagnostics.Checkpoint(message); });
            StartupDiagnostics.Checkpoint("Settings loaded and normalized");

            var logPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(settingsPath) ?? AppContext.BaseDirectory,
                "logs", "yoake.log");
            IAppLog log = new FileAppLog(logPath);
            log.Info("Yoake foundation starting.");
            StartupDiagnostics.Checkpoint("Logger initialized");
            foreach (var warning in settingsWarnings) log.Info(warning);

            var registry = new CommandRegistry(log);
            StartupDiagnostics.Checkpoint("Command registry created");
            var workspace = new WorkspaceManager();
            StartupDiagnostics.Checkpoint("Workspace created");
            var undo = new UndoManager();
            StartupDiagnostics.Checkpoint("Undo constructed");
            _backgroundJobs = new BackgroundJobService();
            var theme = new ThemeService();
            theme.Apply(settings.Theme);
            StartupDiagnostics.Checkpoint($"Theme applied: {settings.Theme}");

            StartupDiagnostics.Checkpoint("MainWindowViewModel constructing");
            var viewModel = new MainWindowViewModel(registry, workspace, undo, theme, settingsStore, settings);
            StartupDiagnostics.Checkpoint("MainWindowViewModel created; initial document created; commands registered");
            StartupDiagnostics.Checkpoint("MainWindow constructing");
            var window = new MainWindow();
            window.DataContext = viewModel;
            StartupDiagnostics.Checkpoint("MainWindow DataContext attached");
            window.Opened += (_, _) =>
            {
                StartupDiagnostics.Checkpoint("MainWindow opened");
            };
            var firstLayout = true;
            window.LayoutUpdated += (_, _) =>
            {
                if (!firstLayout) return;
                firstLayout = false;
                StartupDiagnostics.Checkpoint("MainWindow first layout completed");
            };
            window.Loaded += (_, _) =>
            {
                StartupDiagnostics.Checkpoint("MainWindow loaded");
                Dispatcher.UIThread.Post(() =>
                {
                    StartupDiagnostics.Checkpoint("Dispatcher entered");
                    if (_startup.VerificationReport is null) StartupDiagnostics.Complete();
                }, DispatcherPriority.Background);
            };
            if (_startup.VerificationReport is not null) new UiStartupVerification(desktop, window, viewModel, _startup).Start();
            desktop.MainWindow = window;
            StartupDiagnostics.Checkpoint("MainWindow assigned");
            desktop.Exit += (_, _) => _backgroundJobs?.CancelAll();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
