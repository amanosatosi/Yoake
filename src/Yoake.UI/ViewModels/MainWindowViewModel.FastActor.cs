using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    private sealed class FastActorState
    {
        public bool Enabled;
        public string ActiveName="";
        public string Playback="Video";
        public string Mode="Nanashi";
        public readonly List<string> Recent=[];
        public int Selected=-1;
    }
    private bool _settingFastActor;
    private FastActorState? FastActor => _activeId is { } id && _documents.TryGetValue(id,out var state) ? state.FastActor : null;
    public bool FastActorEnabled => FastActor?.Enabled==true;
    public IReadOnlyList<string> FastActorRecent => FastActor?.Recent.ToArray() ?? [];
    public int FastActorSelectedIndex => FastActor?.Selected ?? -1;
    public string FastActorPlayback => FastActor?.Playback ?? "Video";
    public string FastActorMode => FastActor?.Mode ?? "Nanashi";
    private void NotifyFastActor()
    {
        OnPropertyChanged(nameof(FastActorEnabled));OnPropertyChanged(nameof(FastActorRecent));OnPropertyChanged(nameof(FastActorSelectedIndex));
        OnPropertyChanged(nameof(FastActorPlayback));OnPropertyChanged(nameof(FastActorMode));
        _registry.NotifyStateChanged();
    }
    private void FastActorDraftChanged()
    {
        if(_settingFastActor || FastActor is not { Enabled:true } state)return;
        state.Selected=-1;OnPropertyChanged(nameof(FastActorSelectedIndex));
    }
    private static void PromoteActor(FastActorState state,string name)
    {
        name=name.Trim();if(name.Length==0)return;
        state.Recent.RemoveAll(item=>string.Equals(item.Trim(),name,StringComparison.Ordinal));
        const int recentLimit=30;
        state.Recent.Insert(0,name);if(state.Recent.Count>recentLimit)state.Recent.RemoveRange(recentLimit,state.Recent.Count-recentLimit);
    }
    private void FastActorLineChanged(string? previousActor)
    {
        if(FastActor is not { Enabled:true } state)return;
        state.ActiveName=(previousActor??"").Trim();PromoteActor(state,state.ActiveName);state.Selected=-1;
        if(state.ActiveName.Length>0 && SelectedEvent is { } line && string.IsNullOrWhiteSpace(line.Actor) && ActiveEditor is { } editor)
        {
            var field=line.FieldNames.FirstOrDefault(n=>n.Equals("Name",StringComparison.OrdinalIgnoreCase)||n.Equals("Actor",StringComparison.OrdinalIgnoreCase));
            if(field is null){SubtitleStatus="This line has no actor field.";NotifyFastActor();return;}
            editor.SetField(line,field,state.ActiveName,"Set actor name");
            ReloadDraft();OnPropertyChanged(nameof(ActorNames));
        }
        NotifyFastActor();
    }
    private void ChooseFastActor(FastActorState state,int index)
    {
        if(index<0||index>=state.Recent.Count||Draft is null)return;
        state.Selected=index;_settingFastActor=true;
        try{Draft.Actor=state.Recent[index];}finally{_settingFastActor=false;}
        OnPropertyChanged(nameof(FastActorSelectedIndex));
    }
    private void RegisterFastActorCommands()
    {
        bool Applies(CommandContext context)=>_activeId is { } id && (context.DocumentId is null||context.DocumentId==id) && FastActor is not null && SelectedEvent is not null && !HasGesture;
        void Register(string id,string label,Func<CommandInvocation,ValueTask> action,Func<bool>? condition=null,Func<bool>? isChecked=null)
        {
            _registry.Register(new(new(id,label,label,"Actor"),(invocation,token)=>{token.ThrowIfCancellationRequested();return action(invocation);},
                context=>Applies(context)&&(condition?.Invoke()??true),isChecked:isChecked is null?null:_=>isChecked()));
            Actions[id]=new RegistryCommand(_registry,id,CurrentContext,hotkeys:()=>HotkeyProfile);
        }
        Register("actor/fast/toggle","Fast actor naming",_=>
        {
            var state=FastActor!;state.Enabled=!state.Enabled;state.ActiveName="";state.Selected=-1;NotifyFastActor();return ValueTask.CompletedTask;
        },isChecked:()=>FastActorEnabled);
        Register("actor/fast/select","Select recent actor",invocation=>
        {
            if(invocation.Parameter is int index)ChooseFastActor(FastActor!,index);return ValueTask.CompletedTask;
        },()=>FastActorEnabled);
        Register("actor/fast/playback","Fast actor playback",invocation=>
        {
            if(invocation.Parameter is string value && value is "Video" or "Audio" or "Off") { FastActor!.Playback=value;OnPropertyChanged(nameof(FastActorPlayback)); }
            return ValueTask.CompletedTask;
        },()=>FastActorEnabled);
        Register("actor/fast/mode","Fast actor naming mode",invocation=>
        {
            if(invocation.Parameter is string value && value is "Nanashi" or "Normal") { FastActor!.Mode=value;OnPropertyChanged(nameof(FastActorMode)); }
            return ValueTask.CompletedTask;
        },()=>FastActorEnabled);
        Register("actor/fast/previous","Previous actor line",_=>
        {
            if(SelectedEvent is not { } previous || ActiveEditor is not { } editor || Events.ToList().IndexOf(previous)<=0 || Draft is null)return ValueTask.CompletedTask;
            try { SubtitleEditor.ValidateEventDraftValues(previous,Draft.Values); }
            catch(Exception e) { SubtitleStatus=e.Message;return ValueTask.CompletedTask; }
            if(IsKaraokeMode&&KaraokePlan?.Changed==true){SubtitleStatus="Commit or cancel the karaoke plan before publishing this edit draft.";return ValueTask.CompletedTask;}
            using var transaction=editor.Undo.BeginTransaction("Fast actor naming");
            Navigate(-1);
            transaction.Commit();
            if(!ReferenceEquals(previous,SelectedEvent))PlayFastActorPreview(FastActor!);
            return ValueTask.CompletedTask;
        },()=>FastActorEnabled);
        Register("actor/fast/step","Choose recent actor",invocation=>
        {
            var state=FastActor!;if(state.Recent.Count==0)return ValueTask.CompletedTask;
            var direction=invocation.Parameter is int n&&n<0?-1:1;
            var index=state.Selected<0?(direction<0?state.Recent.Count-1:Math.Min(1,state.Recent.Count-1)):(state.Selected+direction+state.Recent.Count)%state.Recent.Count;
            ChooseFastActor(state,index);return ValueTask.CompletedTask;
        },()=>FastActorEnabled);
        Register("actor/fast/accept","Apply actor and advance",_=>
        {
            var state=FastActor!;if(Draft is null||SelectedEvent is not { } current||ActiveEditor is not { } editor)return ValueTask.CompletedTask;
            // Do not start a transaction for an invalid draft: cancelling even an
            // empty transaction notifies history listeners, which reload drafts.
            try { SubtitleEditor.ValidateEventDraftValues(current,Draft.Values); }
            catch(Exception e) { SubtitleStatus=e.Message;return ValueTask.CompletedTask; }
            if(IsKaraokeMode&&KaraokePlan?.Changed==true){SubtitleStatus="Commit or cancel the karaoke plan before publishing this edit draft.";return ValueTask.CompletedTask;}
            var actor=Draft.Actor.Trim();var oldActor=current.Actor;
            using var transaction=editor.Undo.BeginTransaction("Fast actor naming");
            Draft.Actor=actor;
            if(!CommitDraft())return ValueTask.CompletedTask;
            if(actor.Length>0)PromoteActor(state,actor);
            else if(!Events.Any(line=>string.Equals(line.Actor.Trim(),oldActor.Trim(),StringComparison.Ordinal)))state.Recent.RemoveAll(name=>string.Equals(name.Trim(),oldActor.Trim(),StringComparison.Ordinal));
            state.ActiveName=actor;state.Selected=-1;
            var index=Events.ToList().IndexOf(current);
            Select(index+1<Events.Count?Events[index+1]:editor.InsertTimingSuccessor(current,_settings.AudioDefaultDuration??3000));
            transaction.Commit();NotifyFastActor();
            PlayFastActorPreview(state);
            return ValueTask.CompletedTask;
        },()=>FastActorEnabled);
    }
    private void PlayFastActorPreview(FastActorState state)
    {
        var playback=state.Playback=="Video"?"video/play/line":state.Playback=="Audio"?"audio/play/line":null;
        if(playback is not null && Actions[playback].CanExecute(null))Actions[playback].Execute(null);
    }
}
