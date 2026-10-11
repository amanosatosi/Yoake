using Yoake.Core.Audio;
using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    private AudioPlaybackPlan? _audioPlayback;
    public AudioPlaybackPlan? ActiveAudioPlayback
    {
        get=>_audioPlayback;
        private set{if(_audioPlayback==value)return;_audioPlayback=value;OnPropertyChanged();}
    }
    // Invalid input remains editable. Preview falls back to stored timing and
    // never commits or repairs the draft as a side effect of playing audio.
    public AudioClipInterval? PrimaryAudioRange
    {
        get
        {
            if(SelectedEvent is not {} line)return null;
            if(_gesture is null&&AudioTiming?.Get(line) is {} pending)return pending;
            if(line.StartMilliseconds is not {} left||line.EndMilliseconds is not {} right||left<0||right<left)return null;
            if(_gesture is not null)return new(left,right);
            var start=Draft is {} draft&&AssTime.TryParse(draft.Start,out var draftStart)?draftStart:left;
            var end=Draft is {} endDraft&&AssTime.TryParse(endDraft.End,out var draftEnd)?draftEnd:right;
            if(start>=0&&end>=start)return new(start,end);
            return new(left,right);
        }
    }
    private AudioPlaybackPlan? PlanAudioPlayback(AudioPlaybackKind kind)
    {
        if(_media?.HasAudio!=true||HasGesture)return null;
        var range=PrimaryAudioRange;
        if(kind!=AudioPlaybackKind.Cursor&&range is null)return null;
        var info=_media.Info;
        var plan=AudioPlaybackPlan.Create(kind,info.SampleRate,info.AudioSamples,range??new(0,0),CurrentTimeSeconds);
        return plan.IsEmpty?null:plan;
    }
    private void UpdateAudioPlaybackEnd()
    {
        if(_publishingAudioTiming)return;
        OnPropertyChanged(nameof(PrimaryAudioRange));
        if(ActiveAudioPlayback is {FollowsSelectionEnd:true} playing&&PrimaryAudioRange is {} primary)
        {var updated=playing.WithSelectionEnd(primary.EndMilliseconds);ActiveAudioPlayback=updated;_audioPlayer?.SetEndFrame(updated.EndFrame);}
    }
    private void RegisterAudioPlaybackCommands()
    {
        bool Applies(CommandContext context)=>_activeId is not null&&(context.DocumentId is null||context.DocumentId==_activeId);
        var modes=new (string Id,string Label,AudioPlaybackKind Kind)[]
        {
            ("audio/play/current","Play captured selection",AudioPlaybackKind.Current),
            ("audio/play/line","Play current line",AudioPlaybackKind.Line),
            ("audio/play/selection","Play selection",AudioPlaybackKind.Selection),
            ("audio/play/selection/before","Play 500 ms before selection",AudioPlaybackKind.Before),
            ("audio/play/selection/after","Play 500 ms after selection",AudioPlaybackKind.After),
            ("audio/play/selection/begin","Play first 500 ms",AudioPlaybackKind.Begin),
            ("audio/play/selection/end","Play last 500 ms",AudioPlaybackKind.End),
            ("audio/play/to_end","Play selection to end of audio",AudioPlaybackKind.ToEnd),
            ("audio/play/cursor","Play cursor to end of audio",AudioPlaybackKind.Cursor),
        };
        foreach(var (id,label,kind) in modes)
        {
            var command=id;var mode=kind;
            _registry.Register(new(new(command,label,mode is AudioPlaybackKind.Selection or AudioPlaybackKind.End?
                "Preview audio, following selection end changes without committing edits":"Preview a captured audio interval without committing edits","Audio"),(invocation,token)=>
            {
                token.ThrowIfCancellationRequested();var plan=PlanAudioPlayback(mode);
                if(plan is not null)_=RunPlaybackAsync(null,plan,command);
                return ValueTask.CompletedTask;
            },context=>Applies(context)&&PlanAudioPlayback(mode) is not null));
            Actions[command]=new RegistryCommand(_registry,command,CurrentContext,hotkeys:()=>HotkeyProfile);
        }
        _registry.Register(new(new("audio/play/toggle","Play selection or stop","Play selection, or stop existing media playback","Audio"),(invocation,token)=>
        {
            token.ThrowIfCancellationRequested();if(IsPlaying)StopPlayback();
            else if(PlanAudioPlayback(AudioPlaybackKind.Selection) is {} plan)_=RunPlaybackAsync(null,plan,"audio/play/toggle");
            return ValueTask.CompletedTask;
        },context=>Applies(context)&&_media?.HasAudio==true&&(IsPlaying||PlanAudioPlayback(AudioPlaybackKind.Selection) is not null),context=>Applies(context)&&_media?.HasAudio==true&&IsPlaying));
        _registry.Register(new(new("audio/stop","Stop playback","Stop audio and video playback","Audio"),(_,_)=>
        {StopPlayback();return ValueTask.CompletedTask;},context=>Applies(context)&&_media?.HasAudio==true&&IsPlaying));
        foreach(var id in new[]{"audio/play/toggle","audio/stop"})Actions[id]=new RegistryCommand(_registry,id,CurrentContext,hotkeys:()=>HotkeyProfile);
    }
}
