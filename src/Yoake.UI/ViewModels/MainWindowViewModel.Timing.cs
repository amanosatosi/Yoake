using Yoake.Core.Commands;
using Yoake.Core.Settings;
using Yoake.Core.Subtitles;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed record SubtitleShiftToolRequest(SubtitleShiftPreferences Preferences,IReadOnlyList<AssEvent> Selection);
public sealed class TimingWindowCloseEventArgs(Guid document):EventArgs {public Guid Document {get;}=document;}

public sealed partial class MainWindowViewModel
{
    public SubtitleShiftPreferences ShiftPreferences=>_settings.ShiftPreferences??new();
    public IReadOnlyList<SubtitleShiftHistoryEntry> ShiftHistory=>_settings.ShiftHistory??[];
    public bool CanShiftByFrames=>HasFrameBoundaryTiming;
    public event EventHandler<TimingWindowCloseEventArgs>? TimingWindowCloseRequested;
    private void RegisterTimingCommands()
    {
        bool Applies(CommandContext context)=>_activeId is not null&&(context.DocumentId is null||context.DocumentId==_activeId)&&_gesture is null&&!HasEditOperation;
        void Register(string id,string label,Func<Func<SubtitleTimingSnapshot,CancellationToken,SubtitleTimingPlan>> capture,Func<bool> available)
        {
            _registry.Register(new(new(id,label,label,"Timing"),async(_,token)=>{await CalculateTimingAsync(capture(),Selection(),token);},context=>Applies(context)&&available()));
            Actions[id]=new RegistryCommand(_registry,id,CurrentContext,hotkeys:()=>HotkeyProfile);
        }
        foreach(var (id,label,start) in new[]{("time/continuous/start","Make starts continuous",true),("time/continuous/end","Make ends continuous",false)})
            Register(id,label,()=>(snapshot,token)=>snapshot.Adjoin(start,token),CanAdjoinTiming);
        foreach(var (id,label,start) in new[]{("time/snap/start_video","Snap starts to video",true),("time/snap/end_video","Snap ends to video",false)})
            Register(id,label,()=>{var timing=ActiveFrameTiming();var frame=CurrentFrame;return(snapshot,token)=>snapshot.Snap(timing,frame,start,token);},()=>SelectedEvent is not null&&HasFrameBoundaryTiming);
        Register("time/frame/current","Shift selection to current frame",()=>{var active=SelectedEvent!;var timing=ActiveFrameTiming();var frame=CurrentFrame;return(snapshot,token)=>snapshot.ShiftToFrame(active,timing,frame,token);},()=>SelectedEvent is not null&&HasFrameBoundaryTiming);
        Register("time/snap/scene","Snap selection to scene",()=>{var timing=ActiveFrameTiming();var frame=CurrentFrame;var keys=Keyframes.ToArray();return(snapshot,token)=>snapshot.SnapScene(timing,frame,keys,token);},()=>SelectedEvent is not null&&HasFrameBoundaryTiming&&Keyframes.Count>0);
        _registry.Register(new(new("time/shift","Shift Times…","Shift subtitle start/end times by time or frames","Timing"),async(_,_)=>
        {if(Dialogs is not null)await Dialogs.ShowShiftTimesAsync(this);},context=>Applies(context)&&Events.Count>0));
        Actions["time/shift"]=new RegistryCommand(_registry,"time/shift",CurrentContext,hotkeys:()=>HotkeyProfile);
        _registry.Register(new(new("time/shift/apply","Apply shift","Apply the chosen shift as one undo operation","Timing"),async(invocation,token)=>
        {
            if(invocation.Parameter is not SubtitleShiftToolRequest request)throw new ArgumentException("Choose a shift amount and scope.");
            var preferences=request.Preferences;
            if(preferences!=preferences.Normalize())throw new ArgumentException("Invalid shift preferences.");
            var frameTiming=preferences.ByFrames?ActiveFrameTiming():null;
            // Validate options before committing any draft.
            var parameters=preferences.Request();
            if(parameters.Scope!=SubtitleTimingScope.All&&request.Selection.Count==0)throw new ArgumentException("Select rows to shift.");
            var document=_activeId!.Value;
            var count=await CalculateTimingAsync((snapshot,ct)=>snapshot.Shift(parameters,frameTiming,ct),request.Selection,token);
            if(count<0)return;
            _settings=_settings with{ShiftPreferences=preferences};
            if(count>0)
            {
                var rows=parameters.Scope==SubtitleTimingScope.All?"All rows":parameters.Scope==SubtitleTimingScope.SelectionOnward?"Selection onward":$"{request.Selection.Count} selected rows";
                var entry=new SubtitleShiftHistoryEntry(DateTimeOffset.UtcNow,SuggestedSubtitleFileName,rows,preferences);
                _settings=_settings with{ShiftHistory=new[]{entry}.Concat(ShiftHistory).Take(50).ToArray()};
            }
            _settingsStore.Save(_settings);OnPropertyChanged(nameof(ShiftPreferences));OnPropertyChanged(nameof(ShiftHistory));
            TimingWindowCloseRequested?.Invoke(this,new(document));
        },context=>Applies(context)&&Events.Count>0));
        Actions["time/shift/apply"]=new RegistryCommand(_registry,"time/shift/apply",CurrentContext,hotkeys:()=>HotkeyProfile);
        _registry.Register(new(new("time/shift/history/clear","Clear shift history","Clear saved Shift Times history","Timing"),(_,_)=>
        {_settings=_settings with{ShiftHistory=[]};_settingsStore.Save(_settings);OnPropertyChanged(nameof(ShiftHistory));return ValueTask.CompletedTask;},_=>ShiftHistory.Count>0));
        Actions["time/shift/history/clear"]=new RegistryCommand(_registry,"time/shift/history/clear",CurrentContext,hotkeys:()=>HotkeyProfile);
        _registry.Register(new(new("time/shift/close","Cancel shift","Cancel timing calculation and close the shift window","Timing"),(invocation,_)=>
        {
            if((invocation.Context.DocumentId??_activeId) is {} document)
            {if(_documents.TryGetValue(document,out var state))state.Editing?.Cancel();TimingWindowCloseRequested?.Invoke(this,new(document));}
            return ValueTask.CompletedTask;
        }));
        Actions["time/shift/close"]=new RegistryCommand(_registry,"time/shift/close",CurrentContext,hotkeys:()=>HotkeyProfile);
    }
    private Yoake.Core.Media.FrameTiming ActiveFrameTiming()=>_activeId is {} document&&_documents.TryGetValue(document,out var state)&&state.FrameTiming is {} timing
        ?timing:throw new InvalidOperationException("Load video with a valid frame map to shift or snap by frames.");
    private bool CanAdjoinTiming()
    {
        var rows=Selection();if(rows.Length==0)return false;if(rows.Length==1||rows.Length==Events.Count)return true;
        var positions=rows.Select(line=>line.Number).Order().ToArray();return positions.Zip(positions.Skip(1)).All(pair=>pair.Second==pair.First+1);
    }
    private async Task<int> CalculateTimingAsync(Func<SubtitleTimingSnapshot,CancellationToken,SubtitleTimingPlan> calculate,IReadOnlyList<AssEvent> selection,CancellationToken parent)
    {
        if(_activeId is not {} document||!_documents.TryGetValue(document,out var state))return -1;
        var resumeBurst=Draft?.IsChanged==true&&_editBurstDelay is not null;
        if(resumeBurst){_editBurstDelay!.Cancel();_editBurstDelay.Dispose();_editBurstDelay=null;}
        state.Editing?.Cancel();var cancellation=CancellationTokenSource.CreateLinkedTokenSource(parent);state.Editing=cancellation;
        OnPropertyChanged(nameof(HasEditOperation));_registry.NotifyStateChanged();
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            // Validate/capture the current draft without committing it. A
            // canceled or invalid calculation must retain the exact draft.
            var snapshot=state.Editor.CaptureTiming(selection,SelectedEvent,Draft?.IsChanged==true?Draft.Values:null);SubtitleTimingPlan? plan=null;SubtitleStatus="Calculating subtitle timing…";
            var job=_jobs.Run("Subtitle timing",token=>{plan=calculate(snapshot,token);return Task.CompletedTask;},cancellation.Token);
            await job.Completion;cancellation.Token.ThrowIfCancellationRequested();
            if(_disposed||_activeId!=document||!_documents.TryGetValue(document,out var live)||!ReferenceEquals(live,state))return -1;
            int count;IsSynchronizingSelection=true;
            try{count=state.Editor.ApplyTiming(plan!);}finally{IsSynchronizingSelection=false;}
            ReloadDraft();OnPropertyChanged(nameof(SelectedEvent));OnPropertyChanged(nameof(ActorNames));
            SubtitleStatus=$"Changed timing of {count} subtitle rows.";return count;
        }
        catch(OperationCanceledException)when(cancellation.IsCancellationRequested)
        {if(parent.IsCancellationRequested)throw;if(_activeId==document)SubtitleStatus="Timing canceled.";return -1;}
        finally
        {
            if(ReferenceEquals(state.Editing,cancellation))state.Editing=null;cancellation.Dispose();
            if(resumeBurst&&!_disposed&&_activeId==document&&Draft?.IsChanged==true&&_editBurstDelay is null)
            {var delay=new CancellationTokenSource();_editBurstDelay=delay;_=FinalizeBurstAsync(delay.Token);}
            OnPropertyChanged(nameof(HasEditOperation));_registry.NotifyStateChanged();
        }
    }
}
