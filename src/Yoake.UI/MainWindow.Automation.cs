using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;
using Yoake.UI.Commands;
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
    private (string Id, string Name)[] _automationMenuStructure = [];
    private bool _automationMenuInitialized;
    private readonly Dictionary<string, MenuItem> _automationMenuItems = [];
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
    public Task<AutomationSearchPaths?> EditAutomationPathsAsync(IReadOnlyList<string> autoload, IReadOnlyList<string> includes) => new AutomationPathsWindow(autoload, includes).ShowDialog<AutomationSearchPaths?>(this);
    public Task<IReadOnlyList<string>?> ShowAutomationFileDialogAsync(AutomationFileDialogRequest request, CancellationToken token)
    {
        Window owner = _automationProgress is { IsVisible: true } progress ? progress : this;
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
        var entries = _model?.AutomationMacros ?? [];
        var structure = entries.Select(entry => (entry.CommandId, entry.Name)).ToArray();
        // Validation updates must not replace the open submenu or keyboard focus.
        // Rebuild only when discovery/reload changes the command hierarchy.
        var rebuild = !_automationMenuInitialized || !_automationMenuStructure.SequenceEqual(structure);
        if (rebuild)
        {
            while (AutomationMacroMenu.Items.Count > 3) AutomationMacroMenu.Items.RemoveAt(3);
            _automationMenuItems.Clear(); _automationMenuInitialized = true;
            _automationMenuStructure = structure;
            if (entries.Count == 0) AutomationMacroMenu.Items.Add(new MenuItem { Header = "No Automation macros loaded", IsEnabled = false });
        }
        foreach (var entry in entries)
        {
            if (rebuild)
            {
                var parent = AutomationMacroMenu; var parts = entry.Name.Split('/');
                foreach (var name in parts[..^1])
                {
                    var group = parent.Items.OfType<MenuItem>().FirstOrDefault(item => Equals(item.Header, name) && item.Command is null);
                    if (group is null) { group = new MenuItem { Header = name }; parent.Items.Add(group); }
                    parent = group;
                }
                var created = new MenuItem { Header = parts[^1], Command = entry.Command };
                parent.Items.Add(created); _automationMenuItems.Add(entry.CommandId, created);
            }
            var item = _automationMenuItems[entry.CommandId];
            item.InputGesture=(entry.Command as RegistryCommand)?.InputGesture;
            item.ToggleType = entry.Toggle ? MenuItemToggleType.CheckBox : MenuItemToggleType.None;
            item.IsChecked = entry.Active;
            ToolTip.SetTip(item, entry.Help);
        }
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
