using Yoake.Core.Commands;
using Yoake.Core.Media;
using Yoake.Core.Subtitles;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed record VideoJumpRequest(int? Frame=null,string? Time=null);
public sealed class VideoSeekFocusEventArgs(bool Toggle):EventArgs {public bool Toggle {get;}=Toggle;}

public sealed partial class MainWindowViewModel
{
    public IReadOnlyList<double> FrameTimes=>_media?.FrameTimes??Array.Empty<double>();
    public IReadOnlyList<int> Keyframes=>_media?.HasVideo==true?_media.Keyframes:
        _activeId is {} id&&_documents.TryGetValue(id,out var state)?state.ExternalKeyframes??Array.Empty<int>():Array.Empty<int>();
    public int CurrentFrame=>FrameNavigation.AtTime(FrameTimes,CurrentTimeSeconds);
    public string FramePositionDisplay=>FrameTimes.Count==0?TimeDisplay:$"#{CurrentFrame} · {TimeSpan.FromSeconds(FrameTimes[CurrentFrame]):hh\\:mm\\:ss\\.fff} / {DurationDisplay} "+(Keyframes.Contains(CurrentFrame)?"· keyframe":"");
    public string RelativeTimingDisplay=>SelectedEvent is {} line?FrameNavigation.Relative(FrameTimes.Count>0?FrameTimes[CurrentFrame]:CurrentTimeSeconds,line.StartMilliseconds??0,line.EndMilliseconds??0):"";
    public event EventHandler<VideoSeekFocusEventArgs>? VideoSeekFocusRequested;
    public decimal? VideoFastJumpStep
    {get=>_settings.VideoFastJumpStep??10;set{if(value is {} step)Actions["video/preferences/fast-step"].Execute((int)step);}}
    public int VideoFrameAtMilliseconds(long milliseconds)=>ActiveFrameTiming().FrameAtMilliseconds(milliseconds);
    private Task SeekFrameAsync(int frame)=>FrameTimes.Count==0?Task.CompletedTask:SeekPlaybackAsync(FrameTimes[Math.Clamp(frame,0,FrameTimes.Count-1)]);
    private (double Start,double End)? VideoLineRange=>_media?.HasVideo==true&&CanShiftByFrames&&PrimaryAudioRange is {} range
        ?FrameNavigation.LineRange(ActiveFrameTiming(),FrameTimes,range.StartMilliseconds,range.EndMilliseconds,MediaDurationSeconds):null;
    private async Task SeekBoundaryAsync(bool next)
    {
        if(PrimaryAudioRange is not {} range||SelectedEvent is not {} current)return;
        var timing=ActiveFrameTiming();var target=FrameNavigation.Boundary(timing,CurrentFrame,range.StartMilliseconds,range.EndMilliseconds,next);
        if(target is {} frame){await SeekFrameAsync(frame);return;}
        var index=Events.ToList().IndexOf(current)+(next?1:-1);
        if(index<0||index>=Events.Count)return;
        var line=Events[index];var endpoint=next?line.StartMilliseconds:line.EndMilliseconds;
        if(endpoint is null)throw new ArgumentException("Correct the destination line's timing before seeking.");
        var destination=timing.FrameAtMilliseconds(endpoint.Value,next?FrameTimeKind.Start:FrameTimeKind.End);
        if(!CommitDraft())return;
        var suppress=_suppressSelectionSeek;_suppressSelectionSeek=true;
        try{Select(line);}finally{_suppressSelectionSeek=suppress;}
        await SeekFrameAsync(destination);
    }
    private async Task JumpAsync(VideoJumpRequest request)
    {
        if(request.Frame is {} frame)
        {
            if(request.Time is not null||frame<0||frame>=FrameTimes.Count)throw new ArgumentException("Choose a frame within the loaded video.");
            await SeekFrameAsync(frame);return;
        }
        if(!AssTime.TryParse(request.Time,out var milliseconds))throw new ArgumentException("Enter h:mm:ss.cc.");
        await SeekFrameAsync(ActiveFrameTiming().FrameAtMilliseconds(milliseconds));
    }
    private void RegisterVideoNavigationCommands()
    {
        bool Applies(CommandContext context)=>_activeId is not null&&(context.DocumentId is null||context.DocumentId==_activeId)&&_media?.HasVideo==true;
        void Register(string id,string label,Func<CommandInvocation,CancellationToken,ValueTask> action,Func<bool>? available=null,Func<bool>? check=null)
        {
            _registry.Register(new(new(id,label,label,"Video navigation"),action,c=>Applies(c)&&(available?.Invoke()??true),isChecked:check is null?null:c=>Applies(c)&&check()));
            Actions[id]=new RegistryCommand(_registry,id,CurrentContext,hotkeys:()=>HotkeyProfile);
        }
        Register(CommandIds.VideoPlay,"Play / Pause",(invocation,token)=>{token.ThrowIfCancellationRequested();if(IsPlaying)StopPlayback();else{CurrentTimeSeconds=FrameTimes[CurrentFrame];_=StartPlaybackAsync();}return ValueTask.CompletedTask;},check:()=>IsPlaying);
        Register(CommandIds.VideoStop,"Stop video",(invocation,token)=>{token.ThrowIfCancellationRequested();StopPlayback();return ValueTask.CompletedTask;});
        Register("video/play/line","Play current line",(invocation,token)=>
        {
            token.ThrowIfCancellationRequested();var range=VideoLineRange!.Value;StopPlayback();CurrentTimeSeconds=range.Start;_=StartPlaybackAsync(range.End);return ValueTask.CompletedTask;
        },()=>!HasGesture&&VideoLineRange is not null);
        foreach(var delta in new[]{-1,1})
        {
            var step=delta;
            Register(step>0?CommandIds.VideoFrameNext:CommandIds.VideoFramePrevious,step>0?"Next frame":"Previous frame",async(invocation,token)=>{token.ThrowIfCancellationRequested();await StepFrameAsync(step);});
            Register(step>0?"video/frame/next-keyframe":"video/frame/previous-keyframe",step>0?"Next keyframe":"Previous keyframe",async(invocation,token)=>
            {token.ThrowIfCancellationRequested();await SeekFrameAsync(FrameNavigation.StepKeyframe(Keyframes,CurrentFrame,step,FrameTimes.Count));});
            Register(step>0?"video/frame/next/large":"video/frame/prev/large",step>0?"Fast jump forward":"Fast jump backward",async(invocation,token)=>
            {token.ThrowIfCancellationRequested();await SeekFrameAsync(FrameNavigation.LargeStep(CurrentFrame,step,_settings.VideoFastJumpStep??10,FrameTimes.Count));});
            Register(step>0?"video/frame/next/boundary":"video/frame/prev/boundary",step>0?"Next subtitle boundary":"Previous subtitle boundary",async(invocation,token)=>
            {token.ThrowIfCancellationRequested();await SeekBoundaryAsync(step>0);},()=>!HasGesture&&!HasEditOperation&&CanShiftByFrames&&PrimaryAudioRange is not null);
        }
        Register("video/seek/frame","Seek to frame",async(i,token)=>{token.ThrowIfCancellationRequested();if(i.Parameter is int frame)await SeekFrameAsync(frame);else throw new ArgumentException("Choose a frame.");});
        Register(CommandIds.VideoSeek,"Seek to exact time",async(i,token)=>{token.ThrowIfCancellationRequested();await JumpAsync(new(Time:i.Parameter as string));},()=>CanShiftByFrames);
        foreach(var (id,start) in new[]{(CommandIds.TimingJumpStart,true),(CommandIds.TimingJumpEnd,false)})
        {
            var beginning=start;Register(id,start?"Jump video to subtitle start":"Jump video to subtitle end",async(invocation,token)=>
            {token.ThrowIfCancellationRequested();var range=PrimaryAudioRange!.Value;await SeekFrameAsync(ActiveFrameTiming().FrameAtMilliseconds(beginning?range.StartMilliseconds:range.EndMilliseconds,beginning?FrameTimeKind.Start:FrameTimeKind.End));},()=>CanShiftByFrames&&PrimaryAudioRange is not null);
        }
        Register("video/jump","Jump to frame or time…",async(i,token)=>
        {
            token.ThrowIfCancellationRequested();var document=_activeId;var provider=_media;StopPlayback();
            try
            {
                var request=i.Parameter as VideoJumpRequest??(Dialogs is null?null:await Dialogs.ShowVideoJumpAsync(this));
                token.ThrowIfCancellationRequested();
                if(request is not null&&!_disposed&&document==_activeId&&ReferenceEquals(provider,_media))await JumpAsync(request);
            }
            finally{if(document==_activeId&&ReferenceEquals(provider,_media))VideoSeekFocusRequested?.Invoke(this,new(false));}
        },()=>CanShiftByFrames);
        Register("video/focus_seek","Toggle video slider focus",(invocation,token)=>{token.ThrowIfCancellationRequested();VideoSeekFocusRequested?.Invoke(this,new(true));return ValueTask.CompletedTask;});
        _registry.Register(new(new("video/preferences/fast-step","Fast frame jump size","Frames jumped with Alt-Left/Right","Video"),(i,token)=>
        {
            token.ThrowIfCancellationRequested();if(i.Parameter is not int step||step is <1 or >1000000)throw new ArgumentException("Choose a jump size between 1 and 1000000 frames.");
            _settings=_settings with{VideoFastJumpStep=step};_settingsStore.Save(_settings);OnPropertyChanged(nameof(VideoFastJumpStep));return ValueTask.CompletedTask;
        }));
        Actions["video/preferences/fast-step"]=new RegistryCommand(_registry,"video/preferences/fast-step",CurrentContext,hotkeys:()=>HotkeyProfile);
    }
}
