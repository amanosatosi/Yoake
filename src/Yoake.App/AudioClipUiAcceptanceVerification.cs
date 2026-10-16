using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using Yoake.Core.Commands;
using Yoake.Core.Hotkeys;
using Yoake.Core.Logging;
using Yoake.Core.Subtitles;
using Yoake.UI;
using Yoake.UI.Controls;
using Yoake.UI.ViewModels;

namespace Yoake.App;

internal sealed class AudioClipUiAcceptanceVerification(IClassicDesktopStyleApplicationLifetime desktop,
    MainWindow window,MainWindowViewModel model,StartupOptions options)
{
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(100)};
    private bool _ready,_ticking,_cancelSent;private int _stage;private Task<bool>? _media;private PropertyChangedEventHandler? _cancel;
    private SubtitleEditor? _editor;private string _source="";private EventEditDraft? _draft;private AssEvent[] _selection=[];
    private object? _samples,_times,_keys;private double _position;private byte[] _saved=[];
    private string Clip=>Path.Combine(options.ProfileDirectory!,"音声 မြန်မာ.wav");
    private AudioWaveformControl Audio=>window.FindControl<AudioWaveformControl>("AudioDisplay")!;
    public void Start(){window.LayoutUpdated+=(_,_)=>_ready=window.IsVisible&&window.Bounds.Width>0;_timer.Tick+=Tick;_timer.Start();}
    private async void Tick(object? sender,EventArgs args)
    {
        if(!_ready||_ticking)return;_ticking=true;options.MarkStartupVerified();
        try
        {
            switch(_stage++)
            {
                case 0:
                    Require(window.TryGetPlatformHandle() is not null&&!model.Actions["audio/save/clip"].CanExecute(null),"Audio export requires the real window and loaded audio.");
                    var path=Path.Combine(options.ProfileDirectory!,"audio-clip.ass");File.WriteAllText(path,"[Events]\nFormat: "+string.Join(',',AssDocument.EventFormat)+"\n"+
                        "Dialogue: 0,0:00:00.30,0:00:01.00,Default,,0,0,0,,{\\future(x)\\distort(1,2,3,4)}日本語 é\n"+
                        "Comment: 0,0:00:01.20,0:00:01.40,Default,,0,0,0,,မြန်မာ 👩‍👩‍👧‍👦\n");
                    Require(model.OpenSubtitle(path),"Open audio clip fixture.");
                    Require(model.IsDefaultView && !model.Actions[CommandIds.ViewAudioVideo].CanExecute(null),
                        "Empty media views must stay disabled before clip media loads.");_media=model.OpenMediaAsync(Path.Combine(options.VerificationMedia!,"av.avi"));return;
                case 1:
                    if(!_media!.IsCompleted){_stage--;return;}Require(_media.Result,"Load native clip media.");
                    Require(model.IsAudioVideoView && model.Actions[CommandIds.ViewAudioVideo].CanExecute(null),
                        "Loaded clip media must select Audio + Video.");await Invoke(CommandIds.GridSelectAll);
                    _editor=model.ActiveEditor;_source=_editor!.Document.Serialize();_selection=model.SelectedEvents.ToArray();_draft=model.Draft;_draft!.Start="unfinished timing";
                    _samples=model.WaveformSamples;_times=model.FrameTimes;_keys=model.Keyframes;model.CurrentTimeSeconds=.8;_position=model.CurrentTimeSeconds;
                    model.PlaybackVolume=.2;model.PlaybackMuted=true;Require(model.Actions["audio/save/clip"].CanExecute(null)&&!model.Actions["audio/save/cancel"].CanExecute(null),"Clip commands need correct native media/selection state.");
                    window.FindControl<MenuItem>("AudioMenu")!.IsSubMenuOpen=true;return;
                case 2:
                    var save=window.FindControl<MenuItem>("SaveAudioClipMenu")!;save.CommandParameter=Clip;Press(save,Key.Enter);return;
                case 3:
                    if(model.HasAudioExportOperation){_stage--;return;}_saved=File.ReadAllBytes(Clip);
                    Require(_saved.Length==44+52800*2&&BitConverter.ToUInt16(_saved,22)==1&&BitConverter.ToUInt32(_saved,24)==48000&&_saved.Skip(44).Any(value=>value!=0),"Real menu Enter must export one non-silent 1.1-second native PCM span including the selected comment and gap despite mute/volume.");Preserved();
                    await Invoke("settings/hotkeys/set",new HotkeyChange(HotkeyContext.Audio,"audio/save/cancel",["F12"]));
                    _cancel=(_,change)=>{if(change.PropertyName==nameof(MainWindowViewModel.HasAudioExportOperation)&&model.HasAudioExportOperation){_cancelSent=true;Press(Audio,Key.F12);}};model.PropertyChanged+=_cancel;
                    window.FindControl<MenuItem>("AudioMenu")!.IsSubMenuOpen=true;return;
                case 4:Press(window.FindControl<MenuItem>("SaveAudioClipMenu")!,Key.Enter);return;
                case 5:
                    if(model.HasAudioExportOperation){_stage--;return;}model.PropertyChanged-=_cancel;
                    Require(_cancelSent&&model.MediaStatus.Contains("canceled",StringComparison.Ordinal)&&File.ReadAllBytes(Clip).AsSpan().SequenceEqual(_saved),"Real Audio F12 cancellation must preserve the previous destination.");Preserved();
                    Require(!await model.Registry.InvokeAsync("audio/save/clip",new(Guid.NewGuid()),Clip),"Foreign document invocation cannot export active audio.");
                    window.FindControl<MenuItem>("AudioMenu")!.IsSubMenuOpen=true;return;
                case 6:window.FindControl<MenuItem>("SaveAudioClipMenu")!.CommandParameter=Path.Combine(options.VerificationMedia!,"av.avi");Press(window.FindControl<MenuItem>("SaveAudioClipMenu")!,Key.Enter);return;
                case 7:
                    if(model.HasAudioExportOperation){_stage--;return;}
                    Require(model.SubtitleStatus.Contains("destination other than",StringComparison.Ordinal),"Attempting to overwrite opened media must report a non-destructive validation error.");Preserved();
                    model.SetSelectedEvents([]);window.FindControl<MenuItem>("AudioMenu")!.IsSubMenuOpen=true;return;
                case 8:
                    Require(model.SelectedEvents.Count==0&&!model.Actions["audio/save/clip"].CanExecute(null)&&!window.FindControl<MenuItem>("SaveAudioClipMenu")!.IsEffectivelyEnabled,"An empty actual selection must disable the opened native export item rather than substitute its active row.");
                    var disabled=window.FindControl<MenuItem>("SaveAudioClipMenu")!;
                    Require(!disabled.Focus(),"A disabled native export item must reject keyboard focus.");
                    Press(window.FindControl<MenuItem>("AudioMenu")!,Key.Down);
                    Require(window.FindControl<MenuItem>("AudioMenu")!.SelectedItem!=disabled&&!model.HasAudioExportOperation&&File.ReadAllBytes(Clip).AsSpan().SequenceEqual(_saved),"Native keyboard navigation must skip disabled export and preserve the destination without starting a job.");
                    window.FindControl<MenuItem>("AudioMenu")!.IsSubMenuOpen=false;model.SetSelectedEvents(_selection);Preserved();
                    await Invoke("settings/hotkeys/reset-command",new HotkeyCommandReset(HotkeyContext.Audio,"audio/save/cancel"));
                    Require(StartupDiagnostics.BindingWarningCount==0&&StartupDiagnostics.FrameworkErrorCount==0,"Audio export routing must have no binding/framework errors.");
                    _timer.Stop();options.FinishVerification("PASS: separate real-window audio clip export acceptance; actual native menu Enter; native FFMS2 PCM span including selected comments/gaps; Unicode destination; raw audio independent of mute/gain; real focused Audio F12 cancellation preserving the previous destination; foreign-document and empty-selection availability; safe opened-media overwrite rejection; retained document/selection/draft/source/undo/native providers/current frame.\n");StartupDiagnostics.Complete();desktop.Shutdown(0);return;
            }
        }
        catch(Exception exception){_timer.Stop();Program.Fatal("Packaged audio clip acceptance at stage "+(_stage-1),exception);desktop.Shutdown(1);}
        finally{_ticking=false;}
    }
    private void Preserved()=>Require(ReferenceEquals(_editor,model.ActiveEditor)&&ReferenceEquals(_draft,model.Draft)&&model.Draft!.Start=="unfinished timing"&&model.SelectedEvents.SequenceEqual(_selection)&&_editor!.Document.Serialize()==_source&&!_editor.Undo.CanUndo&&model.CurrentTimeSeconds==_position&&ReferenceEquals(_samples,model.WaveformSamples)&&ReferenceEquals(_times,model.FrameTimes)&&ReferenceEquals(_keys,model.Keyframes),"Audio export must retain subtitle/media state without committing a draft.");
    private async Task Invoke(string id,object? parameter=null)=>Require(await model.Registry.InvokeAsync(id,new(model.ActiveDocumentId),parameter),"Audio clip preparation failed: "+id);
    private static void Press(Control control,Key key)
    {(TopLevel.GetTopLevel(control) as Window)?.Activate();Require(control.Focus()&&control.IsKeyboardFocusWithin,"Actual audio/menu control must own focus.");control.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Source=control,Key=key});}
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
