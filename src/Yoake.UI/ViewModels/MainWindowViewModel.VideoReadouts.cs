using System.Globalization;
using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed record VideoReadoutRequest(bool FromStart,string Operation,string? Color=null);
public sealed partial class MainWindowViewModel
{
    private bool _timingByFrames;
    public bool TimingByFrames=>_timingByFrames&&CanShiftByFrames;
    private void ConfigureTimingDraft()=>Draft?.SetTimingProjection(TimingByFrames?ActiveFrameTiming():null);
    public void SetTimingByFrames(bool frames)
    {
        if(frames&&!CanShiftByFrames)return;
        _timingByFrames=frames;ConfigureTimingDraft();OnPropertyChanged(nameof(TimingByFrames));
    }

    private double _videoPlaybackSpeed=1;
    public string[] VideoPlaybackSpeedChoices {get;}=Enumerable.Range(1,16).Select(i=>(i/4d).ToString("0.00",CultureInfo.InvariantCulture)+"x").ToArray();
    public string VideoPlaybackSpeedText
    {
        get=>_videoPlaybackSpeed.ToString("0.00",CultureInfo.InvariantCulture)+"x";
        set
        {
            if(string.IsNullOrEmpty(value)||!VideoPlaybackSpeedChoices.Contains(value))return;
            var speed=double.Parse(value[..^1],CultureInfo.InvariantCulture);if(speed==_videoPlaybackSpeed)return;
            var resume=IsPlaying&&ActiveVideoPlaybackEndSeconds is not null;var end=ActiveVideoPlaybackEndSeconds;
            if(resume)StopPlayback();_videoPlaybackSpeed=speed;OnPropertyChanged();if(resume)_=StartPlaybackAsync(end);
        }
    }

    public bool CanFadeFromReadout(bool fromStart)=>FrameTimes.Count>0&&!HasGesture&&
        Selection().Any(line=>MangetsuMetadata.Kind(line)==MangetsuMetadataKind.None&&!MangetsuMetadata.IsInherited(line)&&
            line.StartMilliseconds is {} start&&line.EndMilliseconds is {} end&&AssFadeEditing.Duration(fromStart,VideoReadoutMilliseconds,start,end)>0);

    private void RegisterVideoReadoutCommands()
    {
        const string id="video/readout/action";
        _registry.Register(new(new(id,"Video timing readout","Copy, insert or set fade from the current video frame","Video"),
            async(invocation,token)=>
            {
                if(invocation.Parameter is not VideoReadoutRequest request||ReadoutOffset(request.FromStart) is not {} offset)return;
                var number=Math.Abs(offset).ToString(CultureInfo.InvariantCulture);
                if(request.Operation=="copy")
                {if(Dialogs is not null)await Dialogs.WriteClipboardAsync(number);SubtitleStatus="Copied "+number+" ms";return;}
                if(request.Operation=="insert")
                {
                    FormatText("Insert video timing",(text,start,end,_)=>new(text[..Math.Min(start,end)]+number+text[Math.Max(start,end)..],Math.Min(start,end)+number.Length,Math.Min(start,end)+number.Length));
                    return;
                }
                if(request.Operation!="fade"||!CanFadeFromReadout(request.FromStart))return;
                var time=VideoReadoutMilliseconds;var color=request.Color;
                if(color=="custom")
                {
                    if(Dialogs is null||CaptureFormattingTarget() is not {} current)return;
                    var choice=await Dialogs.ChooseColorAsync(new AssColor(255,255,255,0),token);
                    token.ThrowIfCancellationRequested();if(choice is null||!current())return;
                    color=choice.Value.RgbOverride;
                }
                if(!CommitDraft()||ActiveEditor is not {} editor)return;
                var line=SelectedEvent;var cursor=TextCursor;var selectionStart=TextSelectionStart;var selectionEnd=TextSelectionEnd;
                var edits=Selection().Where(target=>MangetsuMetadata.Kind(target)==MangetsuMetadataKind.None&&!MangetsuMetadata.IsInherited(target)&&target.StartMilliseconds is not null&&target.EndMilliseconds is not null)
                    .Select(target=>(Line:target,Edit:AssFadeEditing.Set(target.Text,request.FromStart,time,target.StartMilliseconds!.Value,target.EndMilliseconds!.Value,color))).Where(item=>item.Edit.Text!=item.Line.Text).ToArray();
                if(edits.Length==0)return;
                using(var transaction=editor.Undo.BeginTransaction("Set fade from video"))
                {
                    foreach(var edit in edits)editor.SetField(edit.Line,"Text",edit.Edit.Text,"Set fade from video");
                    transaction.Commit();
                }
                ReloadDraft();
                if(edits.FirstOrDefault(item=>ReferenceEquals(item.Line,line)).Edit is {} change)
                {
                    TextCursor=change.Map(cursor);TextSelectionStart=change.Map(selectionStart);TextSelectionEnd=change.Map(selectionEnd);
                }
                TextFormattingApplied?.Invoke(this,EventArgs.Empty);
            },context=>_activeId is not null&&(context.DocumentId is null||context.DocumentId==_activeId)&&FrameTimes.Count>0&&Draft is not null&&!HasGesture));
        Actions[id]=new RegistryCommand(_registry,id,CurrentContext,hotkeys:()=>HotkeyProfile);
    }
}
