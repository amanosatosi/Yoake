using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;
using Yoake.Core.Automation;
using Yoake.Core.Commands;
using Yoake.UI.Controls;
using Yoake.UI.Services;
using Yoake.UI.ViewModels;
using Yoake.Native.Automation;

namespace Yoake.UI;

public partial class MainWindow
{
    private AutomationProgressWindow? _automationProgress;
    private bool _automationStarted;
    public async Task<string?> OpenAutomationScriptAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "Add Automation script", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Lua / MoonScript") { Patterns = ["*.lua", "*.moon"] }, FilePickerFileTypes.All]
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }
    public Task ShowAutomationManagerAsync(MainWindowViewModel model) => new AutomationManagerWindow(model).ShowDialog(this);
    public Task<IReadOnlyList<string>?> ShowAutomationFileDialogAsync(AutomationFileDialogRequest request, CancellationToken token)
    {
        var owner = _automationProgress is { IsVisible: true } progress ? progress : this;
        return Task.FromResult(WindowsAutomationFilePicker.Pick(request, owner.TryGetPlatformHandle()?.Handle ?? 0, token));
    }
    public Task<AutomationDialogResult> ShowAutomationDialogAsync(AutomationDialogRequest request, CancellationToken cancellationToken) =>
        new AutomationDialog(request, e => _model?.Registry.ReportFailure("automation/dialog", e)).DisplayAsync(_automationProgress is { IsVisible: true } progress ? progress : this, cancellationToken);
    public Task<AutomationExportChoice?> ShowAutomationExportAsync(IReadOnlyList<AutomationExportOption> options, IReadOnlyList<string> selected, string encoding, CancellationToken token) =>
        new AutomationExportWindow(options, selected, encoding, e => _model?.Registry.ReportFailure("automation/export/config", e)).DisplayAsync(this, token);
    public IAutomationProgressSession BeginAutomationProgress(string title)
    {
        var progress = new AutomationProgressWindow(title); _automationProgress = progress;
        progress.Closed += (_, _) => { if (ReferenceEquals(_automationProgress, progress)) _automationProgress = null; };
        _ = progress.ShowDialog(this); return progress;
    }
    private void RefreshAutomationMenu()
    {
        AutomationMacroMenu.Items.Clear();
        if (_model is null) return;
        foreach (var entry in _model.AutomationMacros)
        {
            var parent = AutomationMacroMenu; var parts = entry.Name.Split('/');
            foreach (var name in parts[..^1])
            {
                var group = parent.Items.OfType<MenuItem>().FirstOrDefault(item => Equals(item.Header, name) && item.Command is null);
                if (group is null) { group = new MenuItem { Header = name }; parent.Items.Add(group); }
                parent = group;
            }
            var item = new MenuItem { Header = entry.Active ? "✓ " + parts[^1] : parts[^1], Command = entry.Command };
            ToolTip.SetTip(item, entry.Help); parent.Items.Add(item);
        }
        AutomationMacroMenu.IsEnabled = AutomationMacroMenu.Items.Count > 0;
    }
    private void AutomationMenuOpened(object? sender, RoutedEventArgs args) => _model?.ScheduleAutomationValidation();
    private async void StartAutomation(object? sender, EventArgs args)
    {
        if (_automationStarted || _model is null) return;
        _automationStarted = true;
        // ICommand registry contains failures and owns the startup rescan action.
        await _model.Registry.InvokeAsync(CommandIds.AutomationRescan, new());
    }
}
