using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed record SubtitleSelectionRequest(SubtitleSelectionOptions Options,bool CloseOnSuccess=false);
public sealed class SelectionWindowCloseEventArgs(Guid document):EventArgs {public Guid Document {get;}=document;}

public sealed partial class MainWindowViewModel
{
    private bool _suppressSelectionSeek;
    public SubtitleSelectionOptions SelectionPreferences=>_settings.SelectionPreferences??new();
    public string SelectionStatus=>_activeId is {} id&&_documents.TryGetValue(id,out var state)?state.SelectionStatus:"";
    public event EventHandler<SelectionWindowCloseEventArgs>? SelectionWindowCloseRequested;
    private void RegisterSelectionCommands()
    {
        bool Applies(CommandContext context)=>_activeId is not null&&(context.DocumentId is null||context.DocumentId==_activeId)&&_gesture is null&&!HasEditOperation;
        void Register(string id,string label,Func<CommandInvocation,CancellationToken,ValueTask> action,Func<bool>? available=null)
        {_registry.Register(new(new(id,label,label,"Selection"),action,context=>Applies(context)&&(available?.Invoke()??true)));Actions[id]=new RegistryCommand(_registry,id,CurrentContext,hotkeys:()=>HotkeyProfile);}
        Register("tool/line/select","Select Lines…",async(_,_)=>{StopPlayback();if(Dialogs is not null)await Dialogs.ShowSelectLinesAsync(this);});
        Register("selection/apply","Apply selection",async(invocation,token)=>
        {
            if(invocation.Parameter is not SubtitleSelectionRequest request)throw new ArgumentException("Choose selection criteria.");
            await CalculateSelectionAsync(request,false,token);
        });
        Register("subtitle/select/visible","Select visible lines",async(_,token)=>{StopPlayback();await CalculateSelectionAsync(null,true,token);},()=>HasFrameBoundaryTiming);
        _registry.Register(new(new("selection/close","Close Select Lines","Cancel matching and close this document's selection window","Selection"),(invocation,_)=>
        {
            if((invocation.Context.DocumentId??_activeId) is {} document)
            {if(_documents.TryGetValue(document,out var state))state.SelectionCancellation?.Cancel();SelectionWindowCloseRequested?.Invoke(this,new(document));}
            return ValueTask.CompletedTask;
        }));
        Actions["selection/close"]=new RegistryCommand(_registry,"selection/close",CurrentContext,hotkeys:()=>HotkeyProfile);
    }
    private async Task CalculateSelectionAsync(SubtitleSelectionRequest? request,bool visible,CancellationToken parent)
    {
        if(_activeId is not {} document||!_documents.TryGetValue(document,out var state))return;
        var oldSelection=SelectedEvents.Distinct().ToArray();var oldActive=SelectedEvent;var options=request?.Options??new SubtitleSelectionOptions();
        var snapshot=SubtitleSelectionSnapshot.Capture(state.Editor,oldSelection,options,visible?null:oldActive,visible?null:Draft?.Values);
        var timing=visible?ActiveFrameTiming():null;var frame=CurrentFrame;
        var resumeBurst=Draft?.IsChanged==true&&_editBurstDelay is not null;
        if(resumeBurst){_editBurstDelay!.Cancel();_editBurstDelay.Dispose();_editBurstDelay=null;}
        state.Editing?.Cancel();var cancellation=CancellationTokenSource.CreateLinkedTokenSource(parent);state.Editing=cancellation;state.SelectionCancellation=cancellation;
        SetSelectionStatus(state,"Matching subtitle rows…");OnPropertyChanged(nameof(HasEditOperation));_registry.NotifyStateChanged();
        try
        {
            SubtitleSelectionResult? result=null;var job=_jobs.Run("Select subtitle lines",token=>
            {result=visible?snapshot.Visible(timing!,frame,token):snapshot.Match(token);return Task.CompletedTask;},cancellation.Token);
            await job.Completion;cancellation.Token.ThrowIfCancellationRequested();
            if(_disposed||_activeId!=document||!_documents.TryGetValue(document,out var live)||!ReferenceEquals(live,state)||_gesture is not null)return;
            result!.Validate(state.Editor);
            if(!SelectedEvents.SequenceEqual(oldSelection)||!ReferenceEquals(SelectedEvent,oldActive))throw new InvalidOperationException("Current selection changed; apply again.");
            // Select Visible activates the first visible row in both releases.
            // Criteria matching instead retains an already-selected active row.
            var active=result.ActiveLine(oldActive,firstVisible:visible);
            if(!ReferenceEquals(active,oldActive)&&!CommitDraft()){SetSelectionStatus(state,"Complete the current line's draft before changing the active line.");return;}
            var suppress=_suppressSelectionSeek;var syncing=IsSynchronizingSelection;IsSynchronizingSelection=true;_suppressSelectionSeek=visible;
            try{SetSelectedEvents(result.Lines);SelectedEvent=active;}finally{IsSynchronizingSelection=syncing;_suppressSelectionSeek=suppress;}
            SetSelectionStatus(state,$"Matched {result.MatchCount} rows; selected {result.Lines.Count} rows.");
            if(request is not null)
            {
                _settings=_settings with{SelectionPreferences=options};_settingsStore.Save(_settings);OnPropertyChanged(nameof(SelectionPreferences));
                if(request.CloseOnSuccess)SelectionWindowCloseRequested?.Invoke(this,new(document));
            }
        }
        catch(OperationCanceledException)when(cancellation.IsCancellationRequested)
        {if(parent.IsCancellationRequested)throw;if(_activeId==document)SetSelectionStatus(state,"Selection canceled.");}
        finally
        {
            if(ReferenceEquals(state.Editing,cancellation))state.Editing=null;if(ReferenceEquals(state.SelectionCancellation,cancellation))state.SelectionCancellation=null;cancellation.Dispose();
            if(resumeBurst&&!_disposed&&_activeId==document&&Draft?.IsChanged==true&&_editBurstDelay is null)
            {var delay=new CancellationTokenSource();_editBurstDelay=delay;_=FinalizeBurstAsync(delay.Token);}
            OnPropertyChanged(nameof(HasEditOperation));_registry.NotifyStateChanged();
        }
    }
    private void SetSelectionStatus(DocumentState state,string text){state.SelectionStatus=text;OnPropertyChanged(nameof(SelectionStatus));}
}
