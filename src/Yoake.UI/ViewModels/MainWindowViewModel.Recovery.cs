using Avalonia.Threading;
using Yoake.Core.Subtitles;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed record AutosaveOptions(bool Enabled, int IntervalSeconds, string? Directory);

public sealed partial class MainWindowViewModel
{
    private readonly DispatcherTimer _recoveryTimer = new();
    private SubtitleRecoveryStore _recoveryStore = null!;
    public bool AutosaveEnabled => _settings.AutosaveEnabled ?? true;
    public int AutosaveIntervalSeconds => _settings.AutosaveIntervalSeconds ?? 60;
    public string RecoveryDirectory => _settings.RecoveryDirectory ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_settingsStore.Path))!, "recovery");
    public string? RecoveryProvenance => _activeId is {} id && _documents.TryGetValue(id,out var state) ? state.RecoveryProvenance : null;

    private void InitializeRecovery()
    {
        _recoveryStore = new(RecoveryDirectory);
        _recoveryTimer.Tick += RecoveryTick;
        ConfigureRecoveryTimer();
        _registry.Register(new(new("settings/autosave", "Autosave settings", "Configure automatic recovery snapshots", "Application"), (invocation, token) =>
        {
            token.ThrowIfCancellationRequested();
            if(invocation.Parameter is not AutosaveOptions options || options.IntervalSeconds is <1 or >86400) throw new ArgumentException("Autosave interval must be between 1 and 86400 seconds.");
            var directory=string.IsNullOrWhiteSpace(options.Directory)?null:Path.GetFullPath(options.Directory);
            var settings=_settings with{AutosaveEnabled=options.Enabled,AutosaveIntervalSeconds=options.IntervalSeconds,RecoveryDirectory=directory};
            _settingsStore.Save(settings);
            foreach(var id in _documents.Keys)InvalidateRecovery(id);
            _settings=settings;_recoveryStore=new(RecoveryDirectory);ConfigureRecoveryTimer();
            OnPropertyChanged(nameof(AutosaveEnabled));OnPropertyChanged(nameof(AutosaveIntervalSeconds));OnPropertyChanged(nameof(RecoveryDirectory));return ValueTask.CompletedTask;
        }));
        _registry.Register(new(new("subtitle/open/autosave", "Recover Subtitles…", "Open an autosaved version in a separate unsaved tab", "Subtitle"), async (_,token) =>
        { if(Dialogs is {} dialogs)await dialogs.ShowRecoveryAsync(this,token); }));
        _registry.Register(new(new("recovery/open-version", "Recover selected version", "Recover into a new unsaved document", "Subtitle"), async (invocation,token) =>
        {
            if(invocation.Parameter is not string path)throw new ArgumentException("Choose a recovery version.");
            var recovered=await _recoveryStore.ReadAsync(path,token);token.ThrowIfCancellationRequested();
            if(_disposed)throw new OperationCanceledException();
            // Recovery opens another tab. It must not commit/discard the draft,
            // history or media of an already-open original (even an invalid draft).
            var session=_workspace.CreateUntitled();Attach(session,recovered.Document);
            var state=_documents[session.Id];state.RecoveredUnsaved=true;state.RecoveryProvenance=recovered.Metadata.OriginalPath;
            session.Title="Recovered - "+recovered.Metadata.Title;
            if(recovered.Metadata.SelectedRow>=0)state.Selected=recovered.Document.Events[recovered.Metadata.SelectedRow];
            if(recovered.Metadata.Draft is {} draft)
            {
                state.Selected=recovered.Document.Events[draft.Row];state.RetainedDraft=new(state.Selected,draft.Values);
                state.OriginalLine=state.Selected;state.OriginalText=draft.OriginalText;
            }
            SynchronizeActiveDocument();session.IsDirty=true;
            if(recovered.Metadata.Draft is {} selection)
            {TextCursor=selection.Caret;TextSelectionStart=selection.SelectionStart;TextSelectionEnd=selection.SelectionEnd;}
            OnPropertyChanged(nameof(RecoveryProvenance));SubtitleStatus="Recovered into an unsaved tab. Save As to choose its destination.";
        },_=>!HasGesture));
        _registry.Register(new(new("recovery/discard-version", "Discard selected recovery", "Permanently remove this recovery version", "Subtitle"), async (invocation,token) =>
        {
            if(invocation.Parameter is not string path)throw new ArgumentException("Choose a recovery version.");
            if(Dialogs is {} dialogs&&await dialogs.ConfirmRecoveryDiscardAsync(Path.GetFileName(path),token))await _recoveryStore.DiscardAsync(path,token);
        }));
        Actions["subtitle/open/autosave"]=new RegistryCommand(_registry,"subtitle/open/autosave",CurrentContext,hotkeys:()=>HotkeyProfile);
    }
    private void ConfigureRecoveryTimer()
    {
        _recoveryTimer.Stop();_recoveryTimer.Interval=TimeSpan.FromSeconds(AutosaveIntervalSeconds);
        if(AutosaveEnabled&&!_disposed)_recoveryTimer.Start();
    }
    private async void RecoveryTick(object? sender,EventArgs args)
    {
        try { await RunAutosaveAsync(); }
        catch(Exception error) { _registry.ReportFailure("subtitle/autosave",error); }
    }
    public Task<RecoveryEntry[]> ListRecoveryAsync(CancellationToken token=default)=>_recoveryStore.ListAsync(token);

    // Called on the model owner thread by the production timer. Only copying
    // editable arrays/draft strings happens here; encoding, hashing, ZIP I/O,
    // validation and pruning are all background work owned by the store.
    public async Task RunAutosaveAsync()
    {
        if(_disposed||!AutosaveEnabled)return;
        var jobs=new List<Task>();
        foreach(var session in _workspace.Documents.ToArray())
        {
            if(!_documents.TryGetValue(session.Id,out var state)||state.RecoveryWriting is not null)continue;
            var draft=session.Id==_activeId?Draft:state.RetainedDraft;
            if(!state.Editor.IsDirty&&!state.RecoveredUnsaved&&draft?.IsChanged!=true)continue;
            if(state.Editor.Undo.IsTransactionActive||(session.Id==_activeId&&HasGesture))continue; // no partially applied edit transaction
            var stamp=$"{state.Editor.Document.Revision}/{state.DraftRevision}/{draft?.IsChanged}/{session.Path}/{session.Title}";
            if(stamp==state.LastRecoveryStamp)continue;
            var snapshot=state.Editor.Document.CaptureSnapshot();var selected=session.Id==_activeId?SelectedEvent:state.Selected;
            RecoveryDraft? recoveryDraft=null;
            if(draft?.IsChanged==true)
            {
                var row=state.Editor.Document.Events.IndexOf(draft.Line);
                if(row>=0)
                {
                    var length=draft.Text.Length;
                    recoveryDraft=new(){Row=row,Values=new(draft.Values,StringComparer.OrdinalIgnoreCase),OriginalText=state.OriginalText,
                        Caret=Math.Clamp(session.Id==_activeId?TextCursor:0,0,length),SelectionStart=Math.Clamp(session.Id==_activeId?TextSelectionStart:0,0,length),SelectionEnd=Math.Clamp(session.Id==_activeId?TextSelectionEnd:0,0,length)};
                }
            }
            var metadata=new RecoveryMetadata{DocumentId=session.Id,Sequence=++state.RecoverySequence,Timestamp=DateTimeOffset.UtcNow,Title=session.Title,
                OriginalPath=session.Path??state.RecoveryProvenance,Draft=recoveryDraft,SelectedRow=selected is null?-1:state.Editor.Document.Events.IndexOf(selected)};
            var cancellation=new CancellationTokenSource();state.RecoveryWriting=cancellation;
            var generation=state.RecoveryLease.Renew();jobs.Add(WriteRecoveryAsync(state,snapshot,metadata,stamp,generation,cancellation));
        }
        await Task.WhenAll(jobs);
    }
    private async Task WriteRecoveryAsync(DocumentState state,AssDocumentSnapshot snapshot,RecoveryMetadata metadata,string stamp,long generation,CancellationTokenSource cancellation)
    {
        try
        {
            await _recoveryStore.WriteAsync(snapshot,metadata,state.RecoveryLease,generation,cancellation.Token);
            if(!_disposed&&state.RecoveryLease.IsCurrent(generation))state.LastRecoveryStamp=stamp;
        }
        catch(OperationCanceledException) { }
        catch(Exception error) { if(!_disposed)_registry.ReportFailure("subtitle/autosave",error); }
        finally {if(ReferenceEquals(state.RecoveryWriting,cancellation))state.RecoveryWriting=null;cancellation.Dispose();}
    }
    private void InvalidateRecovery(Guid id)
    {
        if(!_documents.TryGetValue(id,out var state))return;
        state.RecoveryLease.Invalidate();state.RecoveryWriting?.Cancel();state.LastRecoveryStamp=null;
    }
    private void PresentRecoveredDraft()
    {
        try{SelectedEvent?.ShowDraft(Draft?.Values);}catch(ArgumentException){ }
        InvalidateSubtitlePreview(true);OnPropertyChanged(nameof(PreviewRevision));
        if(_workspace.ActiveDocument is {} session)session.IsDirty=true;
    }
}
