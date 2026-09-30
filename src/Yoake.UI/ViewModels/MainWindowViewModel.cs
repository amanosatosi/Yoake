using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Yoake.Core.Commands;
using Yoake.Core.Settings;
using Yoake.Core.Undo;
using Yoake.Core.Workspace;
using Yoake.UI.Commands;
using Yoake.UI.Services;

namespace Yoake.UI.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly CommandRegistry _registry;
    private readonly WorkspaceManager _workspace;
    private readonly UndoManager _undo;
    private readonly IThemeService _theme;
    private readonly SettingsStore _settingsStore;
    private AppSettings _settings;
    private string _activeVisualTool = "Position";

    public MainWindowViewModel(
        CommandRegistry registry,
        WorkspaceManager workspace,
        UndoManager undo,
        IThemeService theme,
        SettingsStore settingsStore,
        AppSettings settings)
    {
        _registry = registry;
        _workspace = workspace;
        _undo = undo;
        _theme = theme;
        _settingsStore = settingsStore;
        _settings = settings;

        RegisterCommands();
        NewDocumentCommand = new RegistryCommand(_registry, CommandIds.SubtitleNew, CurrentContext);
        UndoCommand = new RegistryCommand(_registry, CommandIds.EditUndo, CurrentContext);
        RedoCommand = new RegistryCommand(_registry, CommandIds.EditRedo, CurrentContext);
        ThemeCycleCommand = new RegistryCommand(_registry, CommandIds.ViewThemeCycle, CurrentContext);
        PositionToolCommand = new RegistryCommand(_registry, CommandIds.VideoToolPosition, CurrentContext);
        ClipToolCommand = new RegistryCommand(_registry, CommandIds.VideoToolClip, CurrentContext);

        RegisterWorkspaceCommands();
        _workspace.Changed += (_, _) => SynchronizeTabs();
        _undo.Changed += (_, _) => _registry.NotifyStateChanged();
        _workspace.CreateUntitled();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<DocumentTabViewModel> Tabs { get; } = [];
    public ICommand NewDocumentCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand ThemeCycleCommand { get; }
    public ICommand PositionToolCommand { get; }
    public ICommand ClipToolCommand { get; }
    public string ActiveVisualTool
    {
        get => _activeVisualTool;
        private set
        {
            if (_activeVisualTool == value) return;
            _activeVisualTool = value;
            OnPropertyChanged();
        }
    }

    private CommandContext CurrentContext() => new(_workspace.ActiveDocumentId, "Default");

    private void RegisterCommands()
    {
        _registry.Register(new AppCommand(
            new(CommandIds.SubtitleNew, "New subtitle", "Create a new subtitle document", "Subtitle", "new"),
            (_, _) => { _workspace.CreateUntitled(); return ValueTask.CompletedTask; }));

        _registry.Register(new AppCommand(
            new(CommandIds.SubtitleClose, "Close subtitle", "Close a subtitle document", "Subtitle", "close"),
            (invocation, _) =>
            {
                if (invocation.Parameter is Guid id) _workspace.Close(id);
                return ValueTask.CompletedTask;
            }));

        _registry.Register(new AppCommand(
            new(CommandIds.EditUndo, "Undo", "Undo the last editor operation", "Edit", "undo"),
            (_, _) => { _undo.Undo(); return ValueTask.CompletedTask; },
            _ => _undo.CanUndo));

        _registry.Register(new AppCommand(
            new(CommandIds.EditRedo, "Redo", "Redo the last editor operation", "Edit", "redo"),
            (_, _) => { _undo.Redo(); return ValueTask.CompletedTask; },
            _ => _undo.CanRedo));

        _registry.Register(new AppCommand(
            new(CommandIds.ViewThemeCycle, "Cycle theme", "Cycle System, Dark, and Light themes", "View", "theme"),
            (_, _) =>
            {
                _settings = _settings with { Theme = _theme.Next() };
                _settingsStore.Save(_settings);
                return ValueTask.CompletedTask;
            }));

        _registry.Register(new AppCommand(
            new(CommandIds.VideoToolPosition, "Position tool", "Select the position visual tool", "Visual Tools", "position"),
            (_, _) => { ActiveVisualTool = "Position"; return ValueTask.CompletedTask; }));

        _registry.Register(new AppCommand(
            new(CommandIds.VideoToolClip, "Clip tool", "Select the clip visual tool", "Visual Tools", "clip"),
            (_, _) => { ActiveVisualTool = "Clip"; return ValueTask.CompletedTask; }));
    }

    private void SynchronizeTabs()
    {
        var documents = _workspace.Documents;
        var liveIds = documents.Select(document => document.Id).ToHashSet();

        for (var i = Tabs.Count - 1; i >= 0; i--)
        {
            if (liveIds.Contains(Tabs[i].Id))
                continue;
            var removed = Tabs[i];
            Tabs.RemoveAt(i);
            removed.Dispose();
        }

        for (var index = 0; index < documents.Count; index++)
        {
            var document = documents[index];
            if (index < Tabs.Count && Tabs[index].Id == document.Id)
                continue;

            var existingIndex = -1;
            for (var candidate = index + 1; candidate < Tabs.Count; candidate++)
            {
                if (Tabs[candidate].Id == document.Id)
                {
                    existingIndex = candidate;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                Tabs.Move(existingIndex, index);
            }
            else
            {
                var id = document.Id;
                Tabs.Insert(index, new DocumentTabViewModel(
                    document,
                    _workspace.ActiveDocumentId == id,
                    new RegistryCommand(_registry, CommandIds.WorkspaceActivateTab, CurrentContext, id),
                    new RegistryCommand(_registry, CommandIds.SubtitleClose, CurrentContext, id)));
            }
        }

        foreach (var tab in Tabs)
            tab.SetActive(_workspace.ActiveDocumentId == tab.Id);
    }

    public void RegisterWorkspaceCommands()
    {
        if (_registry.TryGet(CommandIds.WorkspaceActivateTab, out _)) return;
        _registry.Register(new AppCommand(
            new(CommandIds.WorkspaceActivateTab, "Activate tab", "Activate an open document", "Workspace"),
            (invocation, _) =>
            {
                if (invocation.Parameter is Guid id) _workspace.Activate(id);
                return ValueTask.CompletedTask;
            }));
        SynchronizeTabs();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
