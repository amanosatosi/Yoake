using System.Windows.Input;
using Avalonia.Threading;
using Yoake.Core.Automation;
using Yoake.Core.Commands;
using Yoake.Core.Logging;
using Yoake.Core.Subtitles;
using Yoake.Native.Automation;
using Yoake.UI.Commands;
using Yoake.UI.Services;

namespace Yoake.UI.ViewModels;

public sealed class AutomationScriptItem(string path, Guid? documentId)
{
    public string Path { get; } = path;
    public Guid? DocumentId { get; } = documentId;
    public string Scope => DocumentId is null ? "Autoload" : "This document";
    public string Name => Script?.Metadata.Name ?? System.IO.Path.GetFileName(Path);
    public string Description => Error ?? Script?.Metadata.Description ?? "Loading…";
    public string Author => Script?.Metadata.Author ?? "";
    public string Version => Script?.Metadata.Version ?? "";
    public string Status => Error is not null ? "Failed" : Script is null ? "Loading" : $"{Script.Macros.Count} macros, {Script.Filters.Count} filters";
    public string? Error { get; internal set; }
    internal IAutomationScript? Script;
    internal CancellationTokenSource Lifetime = new();
    internal bool Loading;
    internal string? StoredReference;
    public override string ToString() => $"{Name} — {Scope} — {Status}";
}
public sealed record AutomationMenuEntry(string Name, string Help, ICommand Command, bool Active);

public sealed partial class MainWindowViewModel
{
    private AutomationCommandCatalog _automationCatalog = null!;
    private readonly List<AutomationScriptItem> _automationEntries = [];
    private readonly CancellationTokenSource _automationLifetime = new();
    private CancellationTokenSource? _automationValidation;
    private readonly Dictionary<Guid, CancellationTokenSource> _automationRuns = [];
    private bool _automationScanning;
    private IAppLog _automationLog = null!;
    public IAutomationRuntimeProvider AutomationRuntimeProvider { get; set; } = new LuaAutomationProvider(Path.Combine(AppContext.BaseDirectory, "automation", "host.lua"));
    public string AutomationBaseDirectory => Path.Combine(AppContext.BaseDirectory, "automation");
    public string AutomationUserDirectory => Path.Combine(Path.GetDirectoryName(_settingsStore.Path)!, "automation");
    public IReadOnlyList<string> AutomationAutoloadDirectories => _settings.AutomationAutoloadDirectories ?? ["?data/automation/autoload", "?user/automation/autoload"];
    public IReadOnlyList<string> AutomationIncludeDirectories => _settings.AutomationIncludeDirectories ?? ["?user/automation/include", "?data/automation/include"];
    public IReadOnlyList<AutomationScriptItem> AutomationScripts => _automationEntries.Where(e => e.DocumentId is null || e.DocumentId == _activeId).ToArray();
    public IReadOnlyList<AutomationMenuEntry> AutomationMacros => _activeId is { } id
        ? _automationCatalog.Macros(id).Select(b => new AutomationMenuEntry(b.Macro.Name, b.Validation.Help ?? b.Macro.Description,
            new RegistryCommand(_registry, b.CommandId, CurrentContext), b.Validation.Active)).ToArray() : [];

    private void InitializeAutomationCommands()
    {
        _automationLog = new FileAppLog(Path.Combine(Path.GetDirectoryName(_settingsStore.Path)!, "automation.log"));
        _automationCatalog = new(_registry, ExecuteAutomationMacroAsync);
        _automationCatalog.Changed += (_, _) => { OnPropertyChanged(nameof(AutomationScripts)); OnPropertyChanged(nameof(AutomationMacros)); };
        void Register(string id, string label, Func<CommandInvocation, CancellationToken, ValueTask> execute, Func<CommandContext, bool>? available = null)
        {
            _registry.Register(new(new(id, label, label, "Automation"), execute, available));
            Actions[id] = new RegistryCommand(_registry, id, CurrentContext);
        }
        Register(CommandIds.AutomationManager, "Automation Manager…", async (_, _) => { if (Dialogs is not null) await Dialogs.ShowAutomationManagerAsync(this); });
        Register(CommandIds.AutomationLoad, "Add script…", async (invocation, token) =>
        {
            if (invocation.Context.DocumentId is not { } id || !_documents.ContainsKey(id)) return;
            var path = invocation.Parameter as string ?? (Dialogs is null ? null : await Dialogs.OpenAutomationScriptAsync());
            if (path is not null)
            {
                if (!CommitDraft()) return;
                var session = _workspace.Documents.Single(s => s.Id == id);
                var value = LocalAutomationReferences(_documents[id].Editor.Document);
                var reference = AutomationScriptReference.Encode(path, session.Path, AutomationBaseDirectory);
                var added = value.Length == 0 ? reference : value + "|" + reference;
                _documents[id].Editor.SetProjectProperties(new Dictionary<string, string> { ["Automation Scripts"] = added });
                await SynchronizeDocumentAutomationAsync(id, token);
            }
        }, context => context.DocumentId.HasValue);
        Register(CommandIds.AutomationRemove, "Remove script", async (invocation, token) =>
        {
            if (invocation.Parameter is not AutomationScriptItem { DocumentId: { } id } item || !_documents.TryGetValue(id, out var state) || !CommitDraft()) return;
            var remaining = LocalAutomationReferences(state.Editor.Document).Split('|').Where(p => p.Trim() != item.StoredReference).ToArray();
            state.Editor.SetProjectProperties(new Dictionary<string, string> { ["Automation Scripts"] = string.Join('|', remaining) });
            await SynchronizeDocumentAutomationAsync(id, token);
        });
        Register(CommandIds.AutomationReload, "Reload script", async (invocation, token) => { if (invocation.Parameter is AutomationScriptItem item) await ReloadAutomationScriptAsync(item, token); });
        Register(CommandIds.AutomationRescan, "Rescan autoload", async (_, token) => await RescanAutomationAsync(token), _ => !_automationScanning);
        Register(CommandIds.AutomationExport, "Export subtitles…", ExportAutomationAsync, context => context.DocumentId is { } id && !_automationRuns.ContainsKey(id));
        Register(CommandIds.AutomationPaths, "Search paths…", async (invocation, token) =>
        {
            var paths = invocation.Parameter as AutomationSearchPaths ?? (Dialogs is null ? null : await Dialogs.EditAutomationPathsAsync(AutomationAutoloadDirectories, AutomationIncludeDirectories));
            if (paths is null) return;
            // Preferences own search paths; changing them reloads interpreters so
            // cached modules cannot retain the previous resolution environment.
            _settings = (_settings with { AutomationAutoloadDirectories = paths.Autoload, AutomationIncludeDirectories = paths.Includes }).Normalize(); _settingsStore.Save(_settings);
            await RescanAutomationAsync(token);
            foreach (var item in _automationEntries.Where(e => e.DocumentId is not null).ToArray()) await ReloadAutomationScriptAsync(item, token);
        }, _ => !_automationScanning);
    }

    public async Task<AutomationScriptItem> LoadAutomationScriptAsync(string path, Guid? documentId, CancellationToken token = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        path = Path.GetFullPath(path);
        var item = _automationEntries.FirstOrDefault(e => e.DocumentId == documentId && e.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (item is not null) return item;
        item = new(path, documentId); _automationEntries.Add(item);
        await ReloadAutomationScriptAsync(item, token);
        return item;
    }

    private async Task ReloadAutomationScriptAsync(AutomationScriptItem item, CancellationToken token)
    {
        if (item.Loading || !_automationEntries.Contains(item) || _disposed) return;
        item.Loading = true;
        item.Lifetime.Cancel(); item.Lifetime.Dispose(); item.Lifetime = new();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, item.Lifetime.Token, _automationLifetime.Token);
        _automationCatalog.SetScript(item.DocumentId, item.Path, null);
        var old = item.Script; item.Script = null; item.Error = null;
        OnPropertyChanged(nameof(AutomationScripts));
        try
        {
            if (old is not null) await Task.Run(old.Dispose);
            var resolver = new AutomationPathResolver(item.Path, AutomationIncludeDirectories, AutomationTokens(item.DocumentId));
            var loaded = await AutomationRuntimeProvider.LoadAsync(item.Path, resolver, cancellation.Token);
            if (cancellation.IsCancellationRequested || !_automationEntries.Contains(item)) { await Task.Run(loaded.Dispose); return; }
            item.Script = loaded;
            _automationCatalog.SetScript(item.DocumentId, item.Path, loaded);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception e) { item.Error = e.ToString(); _registry.ReportFailure(CommandIds.AutomationLoad, e); }
        finally { item.Loading = false; OnPropertyChanged(nameof(AutomationScripts)); ScheduleAutomationValidation(); }
    }

    private async Task RemoveAutomationScriptAsync(AutomationScriptItem item)
    {
        if (!_automationEntries.Remove(item)) return;
        item.Lifetime.Cancel(); _automationCatalog.SetScript(item.DocumentId, item.Path, null);
        var script = item.Script; item.Script = null;
        OnPropertyChanged(nameof(AutomationScripts));
        if (script is not null) await Task.Run(script.Dispose);
        item.Lifetime.Dispose();
    }

    private async Task RescanAutomationAsync(CancellationToken token)
    {
        if (_automationScanning) return;
        _automationScanning = true; _registry.NotifyStateChanged();
        try
        {
            foreach (var item in _automationEntries.Where(e => e.DocumentId is null).ToArray()) await RemoveAutomationScriptAsync(item);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, _automationLifetime.Token);
            var paths = new AutomationPathResolver(Path.Combine(AppContext.BaseDirectory, "context.lua"), [], AutomationTokens(null));
            var directories = AutomationAutoloadDirectories.Select(paths.DecodePath).ToArray();
            var files = await Task.Run(() => AutomationPathResolver.Discover(directories, cancellation.Token), cancellation.Token);
            foreach (var path in files) { cancellation.Token.ThrowIfCancellationRequested(); await LoadAutomationScriptAsync(path, null, cancellation.Token); }
        }
        finally { _automationScanning = false; _registry.NotifyStateChanged(); }
    }

    private IReadOnlyDictionary<string, string> AutomationTokens(Guid? documentId)
    {
        var session = _workspace.Documents.FirstOrDefault(s => s.Id == documentId);
        var media = documentId is { } id && _documents.TryGetValue(id, out var state) ? state.Media?.SourcePath : null;
        return new Dictionary<string, string>
        {
            ["?data"] = AppContext.BaseDirectory, ["?local"] = Path.GetDirectoryName(_settingsStore.Path)!,
            ["?user"] = Path.GetDirectoryName(_settingsStore.Path)!, ["?temp"] = Path.GetTempPath(),
            ["?dictionary"] = Path.Combine(AppContext.BaseDirectory, "dictionaries"),
            ["?script"] = session?.Path is { } script ? Path.GetDirectoryName(script)! : "",
            ["?audio"] = media is null ? "" : Path.GetDirectoryName(media)!,
            ["?video"] = media is null ? "" : Path.GetDirectoryName(media)!
        };
    }

    private async ValueTask ExecuteAutomationMacroAsync(AutomationMacroBinding binding, CommandInvocation command, CancellationToken token)
    {
        if (command.Context.DocumentId is not { } id || !_documents.TryGetValue(id, out var state) || id != _activeId || !CommitDraft()) return;
        CancelGesture(); StopPlayback();
        _automationValidation?.Cancel();
        var item = _automationEntries.First(e => ReferenceEquals(e.Script, binding.Script));
        var progress = Dialogs?.BeginAutomationProgress(binding.Macro.Name);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, _automationLifetime.Token, item.Lifetime.Token, progress?.CancellationToken ?? CancellationToken.None);
        _automationRuns[id] = cancellation;
        Exception? failure = null;
        var initialSelection = Selection(); var initialActive = SelectedEvent;
        using var subs = new AutomationSubtitleDocument(state.Editor.Document);
        var context = new AutomationInvocation(subs, subs.SelectionIndexes(initialSelection), initialActive is null ? 0 : subs.IndexOf(initialActive),
            CreateAutomationServices(id, progress, interactive: true));
        try
        {
            // Recheck against the finalized draft, even if the menu was validated.
            using (var view = new AutomationSubtitleDocument(state.Editor.Document, writable: false))
            {
                var validation = await binding.Script.ValidateAsync(binding.Macro.Index, context with { Subtitles = view }, cancellation.Token);
                if (!validation.Enabled) return;
            }
            var result = await binding.Script.RunAsync(binding.Macro.Index, context, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!_documents.TryGetValue(id, out var current) || !ReferenceEquals(state, current)) throw new OperationCanceledException("The Automation document was closed or replaced.");
            IsSynchronizingSelection = true;
            try
            {
                var mapping = subs.Commit(state.Editor, binding.Macro.Name, new(initialSelection, initialActive), result,
                    restored => RestoreAutomationSelection(id, state, restored));
                var selected = result.Selection is null ? initialSelection.Where(state.Editor.Document.Events.Contains).ToArray()
                    : result.Selection.Distinct().Where(mapping.ContainsKey).Select(index => mapping[index]).ToArray();
                var active = result.ActiveLine is { } index && mapping.TryGetValue(index, out var returned) ? returned
                    : initialActive is not null && state.Editor.Document.Events.Contains(initialActive) ? initialActive : selected.FirstOrDefault() ?? state.Editor.Document.Events.FirstOrDefault();
                state.Selected = active;
                if (_activeId == id)
                {
                    _selectedEvent = active; state.Editor.Document.UpdateCurrentEvent(active);
                    SetSelectedEvents(selected.Length == 0 && active is not null ? [active] : selected);
                    ReloadDraft(); OnPropertyChanged(nameof(Events)); OnPropertyChanged(nameof(SelectedEvent)); OnPropertyChanged(nameof(HasSelectedEvent)); OnPropertyChanged(nameof(ActorNames));
                }
            }
            finally { IsSynchronizingSelection = false; }
            SubtitleStatus = $"Automation: {binding.Macro.Name}";
        }
        catch (OperationCanceledException e) { failure = e; SubtitleStatus = "Automation cancelled; changes discarded"; }
        catch (Exception e) { failure = e; throw; }
        finally { _automationRuns.Remove(id); progress?.Complete(failure); ScheduleAutomationValidation(); }
    }

    public void ScheduleAutomationValidation()
    {
        _automationValidation?.Cancel(); _automationValidation?.Dispose();
        if (_disposed || _activeId is not { } id || !_documents.TryGetValue(id, out var state) || _automationCatalog.IsBusy(id) || _automationCatalog.Macros(id).Count == 0) return;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_automationLifetime.Token); _automationValidation = cancellation;
        _ = ValidateAutomationAsync(id, state, cancellation.Token);
    }

    private void RestoreAutomationSelection(Guid id, DocumentState state, AutomationSelectionState restored)
    {
        state.Selected = restored.ActiveLine;
        if (_activeId != id || !_documents.TryGetValue(id, out var current) || !ReferenceEquals(current, state)) return;
        _selectedEvent?.ShowDraft(null); _selectedEvent = restored.ActiveLine;
        state.Editor.Document.UpdateCurrentEvent(restored.ActiveLine); SetSelectedEvents(restored.Selection);
        ReloadDraft(); OnPropertyChanged(nameof(Events)); OnPropertyChanged(nameof(SelectedEvent)); OnPropertyChanged(nameof(HasSelectedEvent));
    }
    private async Task ValidateAutomationAsync(Guid id, DocumentState state, CancellationToken token)
    {
        try
        {
            await Task.Delay(100, token);
            foreach (var binding in _automationCatalog.Macros(id))
            {
                token.ThrowIfCancellationRequested();
                using var subs = new AutomationSubtitleDocument(state.Editor.Document, writable: false);
                var revision = state.Editor.Document.Revision;
                var context = new AutomationInvocation(subs, subs.SelectionIndexes(Selection()), SelectedEvent is { } active ? subs.IndexOf(active) : 0, CreateAutomationServices(id, null, interactive: false));
                try
                {
                    var validation = await binding.Script.ValidateAsync(binding.Macro.Index, context, token);
                    if (!token.IsCancellationRequested && _activeId == id && state.Editor.Document.Revision == revision) _automationCatalog.UpdateValidation(id, binding, validation);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception e) { _automationCatalog.UpdateValidation(id, binding, new(false, e.Message, false)); _automationLog.Error($"Validation failed in '{binding.Script.Path}'", e); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e) { _registry.ReportFailure("automation/validation", e); }
    }

    private void CloseAutomationDocument(Guid id)
    {
        _automationExportChoices.Remove(id);
        _automationReferenceState.Remove(id);
        if (_automationRuns.TryGetValue(id, out var run)) run.Cancel();
        _automationCatalog.RemoveDocument(id);
        foreach (var item in _automationEntries.Where(e => e.DocumentId == id).ToArray()) _ = RemoveAutomationScriptAsync(item);
    }
    private void DisposeAutomation()
    {
        _automationLifetime.Cancel(); _automationValidation?.Cancel(); _automationCatalog.Dispose();
        foreach (var item in _automationEntries.ToArray())
        {
            item.Lifetime.Cancel(); var script = item.Script; item.Script = null;
            if (script is not null) _ = Task.Run(script.Dispose);
        }
        _automationEntries.Clear();
    }
}
