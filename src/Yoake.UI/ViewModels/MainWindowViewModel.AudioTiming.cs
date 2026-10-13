using Yoake.Core.Audio;
using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed record AudioTimingPreferencesRequest(int DefaultDuration,int LeadIn,int LeadOut);
public enum AudioTimingCommitMode { Preference, Stay, Next, DefaultNext }

public sealed partial class MainWindowViewModel
{
    private PendingAudioTiming? AudioTiming=>_activeId is {} id&&_documents.TryGetValue(id,out var state)?state.AudioTiming:null;
    private PendingAudioTiming.Checkpoint? _audioTimingGesture;
    private AssEvent? _audioTimingGestureLine;
    private AudioClipInterval _audioTimingGestureRange;
    private bool _publishingAudioTiming;
    public bool HasPendingAudioTiming=>AudioTiming?.Count>0;
    public bool AudioAutoCommit=>_settings.AudioAutoCommit??false;
    public bool AudioAutoNext=>_settings.AudioAutoNext??true;
    public decimal? AudioTimingDefaultDuration
    {get=>_settings.AudioDefaultDuration??3000;set{if(value is {} n)SetAudioTimingPreferences((int)n,(int)AudioTimingLeadIn!.Value,(int)AudioTimingLeadOut!.Value);}}
    public decimal? AudioTimingLeadIn
    {get=>_settings.AudioLeadIn??100;set{if(value is {} n)SetAudioTimingPreferences((int)AudioTimingDefaultDuration!.Value,(int)n,(int)AudioTimingLeadOut!.Value);}}
    public decimal? AudioTimingLeadOut
    {get=>_settings.AudioLeadOut??350;set{if(value is {} n)SetAudioTimingPreferences((int)AudioTimingDefaultDuration!.Value,(int)AudioTimingLeadIn!.Value,(int)n);}}
    private void SetAudioTimingPreferences(int duration,int leadIn,int leadOut)=>Actions["audio/timing/preferences"].Execute(new AudioTimingPreferencesRequest(duration,leadIn,leadOut));
    private bool ValidAudioTimingDraft()
    {
        if(ActiveEditor is not {} editor||SelectedEvent is null)return false;
        try
        {
            if(Draft is {} draft)
            {
                var values=draft.Values;
                // A zero-ended destination may retain a nonzero stored start.
                // Validate the controller's explicitly staged default range,
                // while genuinely edited/invalid endpoint input still blocks.
                if(draft.Start==SelectedEvent.Start&&draft.End==SelectedEvent.End&&AudioTiming?.Get(SelectedEvent) is not null)
                    values=AudioPreviewValues()!;
                SubtitleEditor.ValidateEventDraftValues(SelectedEvent,values);
            }
            return true;
        }
        catch(ArgumentException){return false;}
    }
    private IReadOnlyDictionary<string,string>? AudioPreviewValues()
    {
        if(SelectedEvent is not {} line)return null;
        var values=_gesture is null&&Draft?.IsChanged==true?Draft.Values:null;
        if(AudioTiming?.Get(line) is not {} pending)return KaraokePreviewValues(values);
        var merged=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        if(values is not null){foreach(var pair in values)merged[pair.Key]=pair.Value;}
        else{foreach(var name in line.FieldNames)merged[name]=line.Get(name);}
        merged["Start"]=AssTime.Format(pending.StartMilliseconds);merged["End"]=AssTime.Format(pending.EndMilliseconds);return KaraokePreviewValues(merged);
    }
    private void AudioTimingChanged()
    {
        try{SelectedEvent?.ShowDraft(AudioPreviewValues());}catch(ArgumentException){}
        InvalidateSubtitlePreview(true);OnPropertyChanged(nameof(PreviewRevision));OnPropertyChanged(nameof(HasPendingAudioTiming));
        UpdateAudioPlaybackEnd();_registry.NotifyStateChanged();
    }
    private void StageAudioTiming(AudioClipInterval range,bool allowAutoCommit=true)
    {
        if(AudioTiming is not {} timing||SelectedEvent is not {} line)return;
        timing.Stage(line,range);AudioTimingChanged();
        if(allowAutoCommit&&AudioAutoCommit)CommitAudioMarkers(CancellationToken.None);
    }
    private void CommitAudioMarkers(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(AudioTiming is not {} timing||ActiveEditor is null||SelectedEvent is null)return;
        if(!ValidAudioTimingDraft())throw new ArgumentException("Correct the edit draft before committing audio timing. Nothing was changed.");
        var plan=timing.CaptureCommit(token);
        _publishingAudioTiming=true;
        try{timing.Commit(plan,token);Draft?.SynchronizeTiming();}
        finally{_publishingAudioTiming=false;}
        AudioTimingChanged();
    }
    private void CommitAudioTiming(AudioTimingCommitMode mode,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(!ValidAudioTimingDraft())throw new ArgumentException("Correct the edit draft before committing audio timing. Nothing was changed.");
        if(IsKaraokeMode&&KaraokePlan is not null){CommitKaraoke(token);if(mode!=AudioTimingCommitMode.Stay&&(mode!=AudioTimingCommitMode.Preference||AudioAutoNext)){NavigateAudioTiming(1);EnsureKaraoke();NotifyKaraoke();}return;}
        var end=PrimaryAudioRange?.EndMilliseconds??throw new ArgumentException("Select a valid subtitle timing range.");
        // Preflight the complete successor before committing markers or a draft.
        // Stage rejects malformed stored endpoints, and the new pending range
        // must also leave a valid event (including its untouched metadata).
        var advances=mode!=AudioTimingCommitMode.Stay&&(mode!=AudioTimingCommitMode.Preference||AudioAutoNext);
        if(advances)
        {
            var successorIndex=Events.ToList().IndexOf(SelectedEvent!)+1;
            var successor=successorIndex<Events.Count?Events[successorIndex]:null;
            if(successor is null||mode==AudioTimingCommitMode.DefaultNext||successor.EndMilliseconds==0)
            {
                var range=new AudioClipInterval(end,checked(end+(_settings.AudioDefaultDuration??3000)));
                PendingAudioTiming.Validate(range);
                if(successor is not null)
                {
                    if(successor.StartMilliseconds is not {} start||successor.EndMilliseconds is not {} finish||finish!=0&&finish<start)
                        throw new ArgumentException("Correct the successor's stored timing before committing. Nothing was changed.");
                    var values=successor.FieldNames.ToDictionary(name=>name,successor.Get,StringComparer.OrdinalIgnoreCase);
                    values["Start"]=AssTime.Format(range.StartMilliseconds);values["End"]=AssTime.Format(range.EndMilliseconds);
                    SubtitleEditor.ValidateEventDraftValues(successor,values);
                }
            }
        }
        // Seed from typed timing without publishing unrelated text/metadata.
        if(AudioTiming?.Get(SelectedEvent!) is null)StageAudioTiming(PrimaryAudioRange!.Value,false);
        CommitAudioMarkers(token);
        if(mode==AudioTimingCommitMode.Stay||mode==AudioTimingCommitMode.Preference&&!AudioAutoNext)return;
        if(!CommitDraft())return;
        var current=SelectedEvent!;var index=Events.ToList().IndexOf(current);
        var next=index+1<Events.Count?Events[index+1]:ActiveEditor!.InsertTimingSuccessor(current,_settings.AudioDefaultDuration??3000);
        Select(next);
        if(mode==AudioTimingCommitMode.DefaultNext||next.EndMilliseconds==0)
            StageAudioTiming(new(end,checked(end+(_settings.AudioDefaultDuration??3000))),false);
    }
    private void NavigateAudioTiming(int delta)
    {
        // Normal metadata draft navigation retains its existing policy.
        // Each line's audio markers remain transient and never commit here.
        if(!CommitDraft())return;
        var index=SelectedEvent is null?-1:Events.ToList().IndexOf(SelectedEvent);
        if(Events.Count>0)
        {
            var destination=Events[Math.Clamp(index+delta,0,Events.Count-1)];
            if(!ReferenceEquals(destination,SelectedEvent))Select(destination);
        }
        AudioTimingChanged();
    }
    private void CommitEditingNext()
    {
        if(!ValidAudioTimingDraft())return;
        if(HasPendingAudioTiming)CommitAudioMarkers(CancellationToken.None);
        if(!CommitDraft()||SelectedEvent is not {} current)return;
        var index=Events.ToList().IndexOf(current);
        Select(index+1<Events.Count?Events[index+1]:ActiveEditor!.InsertTimingSuccessor(current,_settings.AudioDefaultDuration??3000));
    }
    public bool BeginAudioTimingGesture()
    {if(HasGesture)return false;Actions["audio/timing/gesture/begin"].Execute(null);return _audioTimingGesture is not null;}
    public void UpdateAudioTimingGesture(int part,double seconds)=>Actions["audio/timing/gesture/update"].Execute(new TimingGestureUpdate(part,seconds));
    public void EndAudioTimingGesture()=>Actions["audio/timing/gesture/commit"].Execute(null);
    private void BeginAudioTimingGestureCore()
    {
        CancelGestureCore();
        if(!ValidAudioTimingDraft()||SelectedEvent is not {} line||PrimaryAudioRange is not {} range||AudioTiming is not {} timing)return;
        _audioTimingGesture=timing.Capture(line);_audioTimingGestureLine=line;_audioTimingGestureRange=range;
        _editBurstDelay?.Cancel();_editBurstDelay?.Dispose();_editBurstDelay=null;
        OnPropertyChanged(nameof(HasGesture));_registry.NotifyStateChanged();
    }
    private void UpdateAudioTimingGestureCore(TimingGestureUpdate update)
    {
        if(_audioTimingGesture is null||!ReferenceEquals(SelectedEvent,_audioTimingGestureLine))return;
        if(update.Part is <0 or >2||!double.IsFinite(update.Delta))throw new ArgumentException("Invalid timing gesture.");
        var delta=checked((long)Math.Round(update.Delta*100)*10);var old=_audioTimingGestureRange;
        AudioClipInterval range;
        if(update.Part==2){var start=Math.Max(0,checked(old.StartMilliseconds+delta));range=new(start,checked(start+old.EndMilliseconds-old.StartMilliseconds));}
        else range=PendingAudioTiming.Adjust(old,update.Part==1?0:delta,update.Part==0?0:delta);
        StageAudioTiming(range,false);
    }
    private void FinishAudioTimingGesture(bool commit)
    {
        var checkpoint=_audioTimingGesture;if(checkpoint is null)return;
        _audioTimingGesture=null;_audioTimingGestureLine=null;
        try
        {
            if(!commit)AudioTiming?.Restore(checkpoint);
            else if(AudioAutoCommit)CommitAudioMarkers(CancellationToken.None);
        }
        catch{AudioTiming?.Restore(checkpoint);throw;}
        finally
        {
            AudioTimingChanged();OnPropertyChanged(nameof(HasGesture));GestureEnded?.Invoke(this,EventArgs.Empty);
            if(!IsKaraokeMode&&Draft?.IsChanged==true&&_editBurstDelay is null){var delay=new CancellationTokenSource();_editBurstDelay=delay;_=FinalizeBurstAsync(delay.Token);}
        }
    }
    private void RegisterAudioTimingCommands()
    {
        bool Applies(CommandContext c)=>_activeId is not null&&(c.DocumentId is null||c.DocumentId==_activeId)&&!HasEditOperation;
        bool Available()=>SelectedEvent is not null&&!HasGesture&&PrimaryAudioRange is not null&&ValidAudioTimingDraft();
        void Register(string id,string label,Action<CommandInvocation,CancellationToken> action,Func<bool>? available=null,Func<bool>? check=null,bool gestureInput=false)
        {
            _registry.Register(new(new(id,label,label,"Audio timing"),(i,t)=>{t.ThrowIfCancellationRequested();action(i,t);return ValueTask.CompletedTask;},
                c=>(gestureInput?_activeId is not null&&(c.DocumentId is null||c.DocumentId==_activeId):Applies(c))&&(available?.Invoke()??true),check is null?null:c=>_activeId is not null&&(c.DocumentId is null||c.DocumentId==_activeId)&&check()));
            Actions[id]=new RegistryCommand(_registry,id,CurrentContext,hotkeys:()=>HotkeyProfile);
        }
        foreach(var (id,label,mode) in new[]{("audio/commit","Commit timing",AudioTimingCommitMode.Preference),
            ("audio/commit/stay","Commit and stay",AudioTimingCommitMode.Stay),("audio/commit/next","Commit and next",AudioTimingCommitMode.Next),
            ("audio/commit/default","Commit and default next",AudioTimingCommitMode.DefaultNext)})
        {var behavior=mode;Register(id,label,(_,t)=>CommitAudioTiming(behavior,t),Available);}
        Register("audio/opt/autocommit","Auto-commit timing",(_,_)=>{_settings=_settings with{AudioAutoCommit=!AudioAutoCommit};_settingsStore.Save(_settings);OnPropertyChanged(nameof(AudioAutoCommit));_registry.NotifyStateChanged();},check:()=>AudioAutoCommit);
        Register("audio/opt/autonext","Next line on commit",(_,_)=>{_settings=_settings with{AudioAutoNext=!AudioAutoNext};_settingsStore.Save(_settings);OnPropertyChanged(nameof(AudioAutoNext));_registry.NotifyStateChanged();},check:()=>AudioAutoNext);
        _registry.Register(new(new("audio/timing/options","Timing preferences…","Configure default timing and lead times","Audio timing"),async(_,_)=>
        {if(Dialogs is not null)await Dialogs.ShowAudioTimingOptionsAsync(this);},Applies));
        Actions["audio/timing/options"]=new RegistryCommand(_registry,"audio/timing/options",CurrentContext,hotkeys:()=>HotkeyProfile);
        Register("audio/timing/preferences","Timing preferences",(i,_)=>
        {
            if(i.Parameter is not AudioTimingPreferencesRequest p||p.DefaultDuration is <10 or >600000||p.LeadIn is <0 or >60000||p.LeadOut is <0 or >60000)
                throw new ArgumentException("Default duration must be 10–600000 ms; lead times must be 0–60000 ms.");
            _settings=_settings with{AudioDefaultDuration=p.DefaultDuration,AudioLeadIn=p.LeadIn,AudioLeadOut=p.LeadOut};_settingsStore.Save(_settings);
            OnPropertyChanged(nameof(AudioTimingDefaultDuration));OnPropertyChanged(nameof(AudioTimingLeadIn));OnPropertyChanged(nameof(AudioTimingLeadOut));
        });
        foreach(var (id,label,start,end) in new[]{("time/start/decrease","Start −10 ms",-10L,0L),("time/start/increase","Start +10 ms",10L,0L),
            ("time/length/decrease","Length −10 ms",0L,-10L),("time/length/increase","Length +10 ms",0L,10L),
            ("time/length/decrease/shift","Length −10 ms and shift syllables",0L,-10L),("time/length/increase/shift","Length +10 ms and shift syllables",0L,10L)})
        {var s=start;var e=end;Register(id,label,(_,_)=>{if(IsKaraokeMode&&KaraokePlan is {} plan){plan.Adjust(s,e);KaraokeChanged();}else StageAudioTiming(PendingAudioTiming.Adjust(PrimaryAudioRange!.Value,s,e));},Available);}
        foreach(var (id,label,input,output) in new[]{("time/lead/in","Add lead-in",true,false),("time/lead/out","Add lead-out",false,true),("time/lead/both","Add lead-in and out",true,true)})
        {var leadIn=input;var leadOut=output;Register(id,label,(_,_)=>{if(IsKaraokeMode&&KaraokePlan is {} plan){plan.Lead(leadIn?-(_settings.AudioLeadIn??100):0,leadOut?(_settings.AudioLeadOut??350):0);KaraokeChanged();}else StageAudioTiming(PendingAudioTiming.Adjust(PrimaryAudioRange!.Value,leadIn?-(_settings.AudioLeadIn??100):0,leadOut?(_settings.AudioLeadOut??350):0));},Available);}
        Register("time/next","Next timing line",(_,_)=>{if(IsKaraokeMode)NavigateKaraoke(1);else NavigateAudioTiming(1);},()=>!HasGesture&&Events.Count>0);
        Register("time/prev","Previous timing line",(_,_)=>{if(IsKaraokeMode)NavigateKaraoke(-1);else NavigateAudioTiming(-1);},()=>!HasGesture&&Events.Count>0);
        Register("audio/timing/revert","Revert pending timing",(_,_)=>{if(SelectedEvent is {} line)AudioTiming?.Clear(line);AudioTimingChanged();},()=>!HasGesture&&SelectedEvent is {} line&&AudioTiming?.Get(line) is not null);
        Register("audio/timing/gesture/begin","Begin audio timing drag",(_,_)=>BeginAudioTimingGestureCore(),Available);
        Register("audio/timing/gesture/update","Move audio timing markers",(i,_)=>
        {try{if(i.Parameter is TimingGestureUpdate p)UpdateAudioTimingGestureCore(p);}catch{FinishAudioTimingGesture(false);throw;}},()=>_audioTimingGesture is not null,gestureInput:true);
        Register("audio/timing/gesture/commit","Finish audio timing drag",(_,_)=>FinishAudioTimingGesture(true),()=>_audioTimingGesture is not null,gestureInput:true);
    }
}
