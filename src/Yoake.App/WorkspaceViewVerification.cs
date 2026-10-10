using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Yoake.Core.Commands;
using Yoake.Core.Settings;
using Yoake.UI;
using Yoake.UI.ViewModels;

namespace Yoake.App;

// Run in the shipping NativeAOT window, first empty, then with actual FFMS media.
internal sealed class WorkspaceViewVerification
{
    private static readonly (string Id, string Menu, WorkspaceViewMode Mode, bool Audio, bool Video)[] Modes =
    [
        (CommandIds.ViewDefault, "ViewDefault", WorkspaceViewMode.Default, false, false),
        (CommandIds.ViewAudio, "ViewAudio", WorkspaceViewMode.Audio, true, false),
        (CommandIds.ViewVideo, "ViewVideo", WorkspaceViewMode.Video, false, true),
        (CommandIds.ViewAudioVideo, "ViewAudioVideo", WorkspaceViewMode.AudioVideo, true, true),
        (CommandIds.ViewDefault, "ViewDefault", WorkspaceViewMode.Default, false, false),
        (CommandIds.ViewAudioVideo, "ViewAudioVideo", WorkspaceViewMode.AudioVideo, true, true),
    ];
    private readonly MainWindow _window;
    private readonly MainWindowViewModel _model;
    private readonly Action _unchanged;
    private readonly bool _loaded;
    private readonly string _report;
    private readonly GridLength _videoWidth, _textWidth;
    private readonly double _audioHeight;
    private int _mode;
    private bool _waiting;

    public WorkspaceViewVerification(MainWindow window, MainWindowViewModel model, string report, bool loaded)
    {
        _window=window; _model=model; _report=report; _loaded=loaded;
        var workspace=Named<Grid>("UpperWorkspace");
        _videoWidth=workspace.ColumnDefinitions[0].Width; _textWidth=workspace.ColumnDefinitions[2].Width;
        _audioHeight=model.AudioDisplayHeight;
        var editor=model.ActiveEditor!; var document=editor.Document; var draft=model.Draft;
        var line=model.SelectedEvent; var selection=model.SelectedEvents.ToArray();
        var source=document.Serialize(); var undo=editor.Undo.NextUndoName; var redo=editor.Undo.NextRedoName;
        var time=model.CurrentTimeSeconds; var frame=model.CurrentFrame;
        var video=model.VideoFrame; var waveform=model.WaveformSamples;
        var frameTimes=model.FrameTimes; var keyframes=model.Keyframes;
        var tab=model.Tabs.Single(t=>t.IsActive); var scripts=model.AutomationScripts.ToArray();
        if(loaded)Require(video is not null && waveform is not null && frameTimes.Count>0,"View probe requires loaded native audio/video.");
        _unchanged=()=>
        {
            Require(ReferenceEquals(editor,model.ActiveEditor)&&ReferenceEquals(document,model.ActiveEditor!.Document)&&
                ReferenceEquals(draft,model.Draft)&&ReferenceEquals(line,model.SelectedEvent)&&selection.SequenceEqual(model.SelectedEvents)&&
                source==document.Serialize()&&undo==editor.Undo.NextUndoName&&redo==editor.Undo.NextRedoName&&
                ReferenceEquals(tab,model.Tabs.Single(t=>t.IsActive)),"View switching must preserve document, draft, selection and history.");
            Require(time==model.CurrentTimeSeconds&&frame==model.CurrentFrame&&ReferenceEquals(video,model.VideoFrame)&&
                ReferenceEquals(waveform,model.WaveformSamples)&&ReferenceEquals(frameTimes,model.FrameTimes)&&ReferenceEquals(keyframes,model.Keyframes),
                "View switching must retain decoded media and provider-owned frame/keyframe tables without seeking or reopening.");
            Require(scripts.SequenceEqual(model.AutomationScripts),"View switching must retain Automation script state.");
        };
    }

    public bool Tick()
    {
        if(_mode==Modes.Length)return true;
        var mode=Modes[_mode];
        if(!_waiting)
        {
            var item=Named<MenuItem>(mode.Menu);
            Require(item.Command is not null&&item.Command.CanExecute(null),"View menu must bind an enabled semantic command.");
            item.Command!.Execute(null); _waiting=true; return false; // Allow a real layout pass.
        }
        _unchanged();
        Require(_model.ViewMode==mode.Mode,"View command must select its named mode.");
        foreach(var choice in Modes.Take(4))
        {
            var item=Named<MenuItem>(choice.Menu);
            Require(item.ToggleType==MenuItemToggleType.Radio&&item.IsChecked==(choice.Mode==mode.Mode)&&
                _model.Registry.GetRequired(choice.Id).IsChecked(new())==(choice.Mode==mode.Mode),"Exactly one View menu/command must be checked.");
        }
        var workspace=Named<Grid>("UpperWorkspace"); var pane=Named<Grid>("TemporalTextColumn");
        Require(Named<Grid>("VideoRegion").IsEffectivelyVisible==mode.Video&&Named<Grid>("AudioRegion").IsEffectivelyVisible==mode.Audio,
            "Only the selected media regions must be visible, even without loaded files.");
        Require(Named<GridSplitter>("VideoSplitter").IsVisible==mode.Video&&Named<GridSplitter>("AudioSplitter").IsVisible==mode.Audio,"Hidden media must hide its splitter.");
        Require(mode.Video?workspace.ColumnDefinitions[0].ActualWidth>=340:workspace.ColumnDefinitions[0].ActualWidth==0,"Video column must restore or fully collapse.");
        Require(mode.Audio?pane.RowDefinitions[0].ActualHeight>=160:pane.RowDefinitions[0].ActualHeight==0,"Audio row must restore or fully collapse.");
        var text=Named<TextBox>("SubtitleText"); var rows=Named<ListBox>("SubtitleRows");
        Require(text.IsEffectivelyVisible&&text.Bounds.Width>=300&&text.Bounds.Height>=64&&rows.Bounds.Height>=140,
            $"All modes must retain usable subtitle authoring space ({mode.Mode}, loaded={_loaded}, text={text.Bounds}, rows={rows.Bounds}).");
        foreach(var control in new Control[]{Named<Grid>("EventEditorRegion"),Named<Border>("SubtitleGridRegion")})
        {
            var origin=control.TranslatePoint(default,_window)!.Value;
            Require(origin.X>=0&&origin.Y>=0&&origin.X+control.Bounds.Width<=_window.ClientSize.Width+1&&origin.Y+control.Bounds.Height<=_window.ClientSize.Height+1,
                "View mode must keep editor/grid inside the CI window.");
        }
        if(mode.Audio)
        {
            var audio=Named<Grid>("AudioRegion"); var editor=Named<Grid>("EventEditorRegion");
            Require(audio.TranslatePoint(new Point(0,audio.Bounds.Height),_window)!.Value.Y<=editor.TranslatePoint(default,_window)!.Value.Y,"Audio must remain above the text editor.");
        }
        if(_mode==Modes.Length-1&&_loaded)
            Require(workspace.ColumnDefinitions[0].Width==_videoWidth&&workspace.ColumnDefinitions[2].Width==_textWidth&&_model.AudioDisplayHeight==_audioHeight,
                "Repeated mode changes must preserve splitter sizes.");
        _mode++; _waiting=false;
        if(_mode!=Modes.Length)return false;
        File.AppendAllText(_report+".views.txt",$"PASS: all four workspace modes ({(_loaded?"loaded native media":"empty media")}); radio commands, retained document/draft/selection/history/media/Automation, collapsed regions, usable CI layout, audio above editor and restored splitters.\n");
        return true;
    }

    private T Named<T>(string name) where T:Control => _window.FindControl<T>(name) ?? throw new InvalidOperationException("Missing view control: "+name);
    private static void Require(bool valid,string message){if(!valid)throw new InvalidOperationException(message);}
}
