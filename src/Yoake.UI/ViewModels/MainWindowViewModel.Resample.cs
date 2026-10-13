using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed class ResampleCloseEventArgs(Guid document):EventArgs {public Guid Document {get;}=document;}
public sealed partial class MainWindowViewModel
{
    private bool _resampleOpen,_publishingResample;
    private ResamplePlan? _resamplePlan;
    private Guid? _resampleOwner;
    private long _resampleDraftRevision;
    private AssEvent? _resampleLine;
    public ResamplePlan? ResamplePreview=>_resamplePlan;
    public event EventHandler<ResampleCloseEventArgs>? ResampleCloseRequested;
    public (int Width,int Height) ScriptResolution=>ActiveEditor?.CaptureResample().Resolution??(384,288);
    private void RegisterResampleCommands()
    {
        bool Owner(CommandContext context)=>!_disposed&&_activeId is {} id&&_documents.ContainsKey(id)&&(context.DocumentId is null||context.DocumentId==id)&&!HasGesture;
        void Register(string id,string label,Func<CommandInvocation,CancellationToken,ValueTask> action,Func<bool>? enabled=null)
        {
            _registry.Register(new(new(id,label,label,"Tools"),action,c=>Owner(c)&&!HasEditOperation&&(enabled?.Invoke()??true)));
            Actions[id]=new RegistryCommand(_registry,id,CurrentContext,hotkeys:()=>HotkeyProfile);
        }
        Register("tool/resampleres","Resample Resolution…",async(_,token)=>
        {
            if(_activeId is not {} id||Dialogs is null)return;var state=_documents[id];
            _resampleOpen=true;state.MediaDraftPauses++;_editBurstDelay?.Cancel();_resamplePlan=null;_registry.NotifyStateChanged();
            if(IsPlaying)StopPlayback();
            try{await Dialogs.ShowResampleAsync(this,id,token);}
            finally
            {
                state.MediaDraftPauses--;_resampleOpen=false;_resamplePlan=null;_resampleOwner=null;
                if(!_disposed&&_activeId==id&&Draft?.IsChanged==true&&!state.KaraokeEnabled)
                {_editBurstDelay?.Cancel();_editBurstDelay?.Dispose();_editBurstDelay=new();_=FinalizeBurstAsync(_editBurstDelay.Token);}
                OnPropertyChanged(nameof(ResamplePreview));_registry.NotifyStateChanged();
            }
        },()=>!_resampleOpen);
        Register("resample/preview","Preview resolution changes",async(invocation,token)=>
        {
            if(invocation.Parameter is not ResampleOptions options)throw new ArgumentException("Choose source and target resolutions.");
            var id=_activeId!.Value;var state=_documents[id];
            if(state.Karaoke.Values.Any(e=>e.Plan.Changed))throw new InvalidOperationException("Commit or cancel pending karaoke timing before resampling.");
            _resamplePlan=null;OnPropertyChanged(nameof(ResamplePreview));
            var snapshot=state.Editor.CaptureResample(SelectedEvent,Draft?.Values);var line=SelectedEvent;var draftRevision=state.DraftRevision;
            using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(token);state.Editing=cancellation;
            state.MediaDraftPauses++;_editBurstDelay?.Cancel();OnPropertyChanged(nameof(HasEditOperation));_registry.NotifyStateChanged();
            try
            {
                ResamplePlan? plan=null;var job=_jobs.Run("Resample subtitle resolution",t=>{plan=snapshot.Calculate(options,t);return Task.CompletedTask;},cancellation.Token);
                await job.Completion;cancellation.Token.ThrowIfCancellationRequested();
                if(_disposed||_activeId!=id||!_documents.TryGetValue(id,out var live)||!ReferenceEquals(live,state)||state.DraftRevision!=draftRevision||!ReferenceEquals(SelectedEvent,line))
                    throw new OperationCanceledException("The resampling document or draft changed.");
                _resamplePlan=plan;_resampleOwner=id;_resampleLine=line;_resampleDraftRevision=draftRevision;
                SubtitleStatus=plan!.Summary;OnPropertyChanged(nameof(ResamplePreview));
            }
            finally
            {
                state.MediaDraftPauses--;if(ReferenceEquals(state.Editing,cancellation))state.Editing=null;
                OnPropertyChanged(nameof(HasEditOperation));_registry.NotifyStateChanged();
            }
        });
        Register("resample/apply","Apply reviewed resolution changes",(_,token)=>
        {
            var id=_activeId!.Value;var state=_documents[id];var plan=_resamplePlan??throw new InvalidOperationException("Preview the resolution changes first.");
            if(_resampleOwner!=id||state.DraftRevision!=_resampleDraftRevision||!ReferenceEquals(SelectedEvent,_resampleLine))throw new InvalidOperationException("The active draft changed. Preview again before applying.");
            _publishingResample=true;
            try{state.Editor.ApplyResample(plan,token);}finally{_publishingResample=false;}
            if(plan.HasChanges)ReloadDraft();SubtitleStatus=plan.HasChanges?"Resolution resampled. One Undo restores the complete original source.":"Resolution is unchanged.";
            _resamplePlan=null;OnPropertyChanged(nameof(ResamplePreview));ResampleCloseRequested?.Invoke(this,new(id));return ValueTask.CompletedTask;
        },()=>_resamplePlan is not null&&_resampleOwner==_activeId);
        Register("resample/invalidate","Invalidate resampling preview",(_,_)=>{_resamplePlan=null;OnPropertyChanged(nameof(ResamplePreview));_registry.NotifyStateChanged();return ValueTask.CompletedTask;});
        _registry.Register(new(new("resample/cancel","Cancel resampling","Close without changing subtitles","Tools"),(invocation,_)=>
        {
            var id=invocation.Context.DocumentId??_activeId;if(id is {} document)
            {if(_documents.TryGetValue(document,out var state))state.Editing?.Cancel();if(_resampleOwner==document){_resamplePlan=null;_resampleOwner=null;}ResampleCloseRequested?.Invoke(this,new(document));}
            return ValueTask.CompletedTask;
        }));
    }
}
