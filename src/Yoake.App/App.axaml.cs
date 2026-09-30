using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settingsPath = SettingsPathResolver.Resolve(
                AppContext.BaseDirectory,
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            var settingsStore = new SettingsStore(settingsPath);
            AppSettings settings;
            try { settings = settingsStore.Load(); }
            catch { settings = new AppSettings(); }

            var logPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(settingsPath) ?? AppContext.BaseDirectory,
                "logs", "yoake.log");
            IAppLog log = new FileAppLog(logPath);
            log.Info("Yoake foundation starting.");

            var registry = new CommandRegistry(log);
            var workspace = new WorkspaceManager();
            var undo = new UndoManager();
            _backgroundJobs = new BackgroundJobService();
            var theme = new ThemeService();
            theme.Apply(settings.Theme);

            var viewModel = new MainWindowViewModel(registry, workspace, undo, theme, settingsStore, settings);
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            desktop.Exit += (_, _) => _backgroundJobs?.CancelAll();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
