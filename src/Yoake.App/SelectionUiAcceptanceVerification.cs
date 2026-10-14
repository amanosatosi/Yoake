using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yoake.Core.Commands;
using Yoake.Core.Hotkeys;
using Yoake.Core.Logging;
using Yoake.Core.Subtitles;
using Yoake.UI;
using Yoake.UI.ViewModels;

namespace Yoake.App;

internal sealed class SelectionUiAcceptanceVerification(IClassicDesktopStyleApplicationLifetime desktop,
    MainWindow window,MainWindowViewModel model,StartupOptions options)
{
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(100)};
    private bool _ready,_ticking;private int _stage;private SelectLinesWindow? _select;private Task<bool>? _media;
    private SubtitleEditor? _editor;private AssEvent[] _rows=[];private string _source="";private EventEditDraft? _draft;
    private object? _samples,_times,_keys;private double _position;
    private string Marker=>Path.Combine(options.ProfileDirectory!,"selection-verified.txt");
    private ListBox Grid=>window.FindControl<ListBox>("SubtitleRows")!;
    private T InSelect<T>(string name)where T:Control=>_select!.FindControl<T>(name)??throw new InvalidOperationException("Missing Select Lines control: "+name);
    public void Start(){window.LayoutUpdated+=(_,_)=>_ready=window.IsVisible&&window.Bounds.Width>0;_timer.Tick+=Tick;_timer.Start();}
    private async void Tick(object? sender,EventArgs args)
    {
        if(!_ready||_ticking)return;_ticking=true;options.MarkStartupVerified();
        try
        {
            switch(_stage++)
            {
                case 0:
                    Require(window.TryGetPlatformHandle() is not null,"Selection acceptance needs the real native window.");
                    if(File.Exists(Marker))Require(model.SelectionPreferences is{Query:"Actor",Field:SubtitleSearchField.Actor,Mode:SubtitleSelectionMode.Exact,Action:SubtitleSelectionAction.Replace},"Select Lines preferences must restore in a second process.");
                    var path=Path.Combine(options.ProfileDirectory!,"selection.ass");
                    File.WriteAllText(path,"[Events]\nFormat: "+string.Join(',',AssDocument.EventFormat)+"\n"+
                        "Dialogue: 0,0:00:00.00,0:00:00.45,Default,Actor,0,0,0,fx,{\\future(x)\\distort(0,0,1,1)}日本語 é\n"+
                        "Comment: 0,0:00:00.35,0:00:00.65,Default,Other,0,0,0,,日本語\n"+
                        "Dialogue: 0,0:00:00.35,0:00:01.00,Default,Actor,0,0,0,fx,မြန်မာ 👩‍👩‍👧‍👦\n");
                    Require(model.OpenSubtitle(path),"Open selection fixture.");_editor=model.ActiveEditor;_rows=model.Events.ToArray();_source=_editor!.Document.Serialize();
                    Require(!model.Actions["subtitle/select/visible"].CanExecute(null),"Select visible must be disabled without video.");
                    window.FindControl<MenuItem>("EditMenu")!.IsSubMenuOpen=true;return;
                case 1:Press(window.FindControl<MenuItem>("SelectLinesMenu")!,Key.Enter);return;
                case 2:
                    _select=window.OwnedWindows.OfType<SelectLinesWindow>().Single();Require(_select.IsVisible&&InSelect<TextBox>("SelectQuery").IsKeyboardFocusWithin,"Real Select Lines menu Enter must open and focus the actual tool.");
                    foreach(var name in new[]{"SelectQuery","SelectField","SelectMode","SelectAction","SelectRaw","SelectConfirm","SelectApply","SelectCancel"})Within(_select.FindControl<Control>(name)!,_select);
                    Press(InSelect<ComboBox>("SelectField"),Key.Enter);return;
                case 3:
                    var field=InSelect<ComboBox>("SelectField");Require(field.IsDropDownOpen,"Real Enter must open the native field dropdown without submitting.");
                    Press(field.ContainerFromIndex(1) as Control??throw new InvalidOperationException("Native field dropdown must realize its Style item."),Key.Enter);return;
                case 4:
                    Require(_select!.IsVisible&&!InSelect<ComboBox>("SelectField").IsDropDownOpen&&Equals(InSelect<ComboBox>("SelectField").SelectedItem,SubtitleSearchField.Style),"Native dropdown item Enter must choose the field and close only the popup.");SourceUnchanged();
                    _draft=model.Draft;_draft!.Start="unfinished timing";InSelect<TextBox>("SelectQuery").Text="[";InSelect<ComboBox>("SelectMode").SelectedItem=SubtitleSelectionMode.RegularExpression;
                    Press(InSelect<Button>("SelectConfirm"),Key.Enter);return;
                case 5:
                    Require(_select!.IsVisible&&InSelect<TextBlock>("SelectStatus").Text?.Contains("Invalid regular expression")==true&&ReferenceEquals(_draft,model.Draft)&&model.Draft!.Start=="unfinished timing"&&model.SelectedEvents.SequenceEqual(new[]{_rows[0]}),"Invalid regex through the actual confirmation button must preserve the tool, draft, selection and undo.");SourceUnchanged();
                    InSelect<TextBox>("SelectQuery").Text="日本語";InSelect<ComboBox>("SelectMode").SelectedItem=SubtitleSelectionMode.Contains;InSelect<ComboBox>("SelectField").SelectedItem=SubtitleSearchField.Text;InSelect<ComboBox>("SelectAction").SelectedItem=SubtitleSelectionAction.Replace;InSelect<CheckBox>("SelectComments").IsChecked=true;
                    Press(InSelect<Button>("SelectApply"),Key.Enter);return;
                case 6:
                    if(model.HasEditOperation){_stage--;return;}
                    Require(_select!.IsVisible&&model.SelectedEvents.SequenceEqual(new[]{_rows[0],_rows[1]})&&ReferenceEquals(_draft,model.Draft),"Actual Apply must select matching dialogue/comments without committing an unchanged active line's invalid draft.");SourceUnchanged();
                    InSelect<ComboBox>("SelectMode").SelectedItem=SubtitleSelectionMode.Exact;Press(InSelect<Button>("SelectApply"),Key.Enter);return;
                case 7:
                    if(model.HasEditOperation){_stage--;return;}
                    Require(model.SelectedEvents.SequenceEqual(new[]{_rows[0],_rows[1]})&&ReferenceEquals(_draft,model.Draft)&&model.SelectionStatus.Contains("draft"),"An invalid draft must prevent changing the active row and leave selection atomic.");
                    model.Draft!.Start=_rows[0].Start;Press(InSelect<Button>("SelectApply"),Key.Enter);return;
                case 8:
                    if(model.HasEditOperation){_stage--;return;}
                    Require(model.SelectedEvents.SequenceEqual(new[]{_rows[1]})&&ReferenceEquals(model.SelectedEvent,_rows[1]),"Exact visible-text match must move to the first selected row after the draft is valid.");SourceUnchanged();
                    InSelect<ComboBox>("SelectAction").SelectedItem=SubtitleSelectionAction.Subtract;Press(InSelect<Button>("SelectApply"),Key.Enter);return;
                case 9:
                    if(model.HasEditOperation){_stage--;return;}
                    Require(model.SelectedEvents.Count==0&&ReferenceEquals(model.SelectedEvent,_rows[1]),"Subtract must allow an empty selection while retaining the active line.");
                    InSelect<TextBox>("SelectQuery").Text="Actor";InSelect<ComboBox>("SelectField").SelectedItem=SubtitleSearchField.Actor;InSelect<ComboBox>("SelectAction").SelectedItem=SubtitleSelectionAction.Add;Press(InSelect<Button>("SelectApply"),Key.Enter);return;
                case 10:
                    if(model.HasEditOperation){_stage--;return;}
                    Require(model.SelectedEvents.SequenceEqual(new[]{_rows[0],_rows[2]}),"Add must use the actual empty selection rather than silently reintroducing the old active row.");
                    InSelect<TextBox>("SelectQuery").Text="မြန်မာ";InSelect<ComboBox>("SelectField").SelectedItem=SubtitleSearchField.Text;InSelect<ComboBox>("SelectMode").SelectedItem=SubtitleSelectionMode.Contains;InSelect<ComboBox>("SelectAction").SelectedItem=SubtitleSelectionAction.Intersect;Press(InSelect<Button>("SelectApply"),Key.Enter);return;
                case 11:
                    if(model.HasEditOperation){_stage--;return;}
                    Require(model.SelectedEvents.SequenceEqual(new[]{_rows[2]}),"Intersect must retain the matching Unicode row only.");SourceUnchanged();
                    var query=InSelect<TextBox>("SelectQuery");query.Text="日本語";query.Focus();var presenter=query.GetVisualDescendants().OfType<TextPresenter>().Single();presenter.PreeditText="にほん";Press(query,Key.Enter);presenter.PreeditText="";return;
                case 12:
                    Require(_select!.IsVisible&&model.SelectedEvents.SequenceEqual(new[]{_rows[2]}),"IME Enter must not invoke default selection or close the tool.");Press(InSelect<TextBox>("SelectQuery"),Key.Escape);return;
                case 13:
                    Require(!window.OwnedWindows.OfType<SelectLinesWindow>().Any(),"Real Escape must route through the semantic Cancel command.");
                    await Invoke("settings/hotkeys/set",new HotkeyChange(HotkeyContext.Default,"tool/line/select",["F9"]));Require(window.FindControl<MenuItem>("SelectLinesMenu")!.InputGesture?.Key==Key.F9,"Select Lines menu must reflect a live binding.");Press(Grid,Key.F9);return;
                case 14:
                    _select=window.OwnedWindows.OfType<SelectLinesWindow>().Single();Require(_select.IsVisible,"Real remapped grid F9 must open the same document-local Select Lines tool.");
                    InSelect<TextBox>("SelectQuery").Text="Actor";InSelect<ComboBox>("SelectField").SelectedItem=SubtitleSearchField.Actor;InSelect<ComboBox>("SelectMode").SelectedItem=SubtitleSelectionMode.Exact;InSelect<ComboBox>("SelectAction").SelectedItem=SubtitleSelectionAction.Replace;InSelect<CheckBox>("SelectComments").IsChecked=false;
                    Press(InSelect<Button>("SelectConfirm"),Key.Enter);return;
                case 15:
                    if(model.HasEditOperation){_stage--;return;}
                    Require(!window.OwnedWindows.OfType<SelectLinesWindow>().Any()&&model.SelectedEvents.SequenceEqual(new[]{_rows[0],_rows[2]}),"Actual confirmation must apply and close, persisting successful criteria.");SourceUnchanged();
                    Require(model.IsDefaultView && !model.Actions[CommandIds.ViewAudioVideo].CanExecute(null),
                        "Selection test starts without video or audio.");_media=model.OpenMediaAsync(Path.Combine(options.VerificationMedia!,"av.avi"));return;
                case 16:
                    if(!_media!.IsCompleted){_stage--;return;}
                    Require(_media.Result,"Load native media for visible selection.");
                    Require(model.IsAudioVideoView && model.Actions[CommandIds.ViewAudioVideo].CanExecute(null),
                        "Loaded selection media selects Audio + Video.");_samples=model.WaveformSamples;_times=model.FrameTimes;_keys=model.Keyframes;
                    if(!model.VideoAutoSeek)await Invoke("video/autoseek");model.CurrentTimeSeconds=model.FrameTimes[3];_position=model.CurrentTimeSeconds;
                    await Invoke("settings/hotkeys/set",new HotkeyChange(HotkeyContext.Default,"subtitle/select/visible",["F11"]));Press(Grid,Key.F11);return;
                case 17:
                    if(model.HasEditOperation){_stage--;return;}
                    Require(model.SelectedEvents.SequenceEqual(new[]{_rows[1],_rows[2]})&&ReferenceEquals(model.SelectedEvent,_rows[1])&&model.CurrentFrame==3&&model.CurrentTimeSeconds==_position,"Real remapped visible-selection key must use inclusive VFR START/END bounds, retain frame even with autoseek, and include timed comment rows as the release does.");
                    Require(ReferenceEquals(_samples,model.WaveformSamples)&&ReferenceEquals(_times,model.FrameTimes)&&ReferenceEquals(_keys,model.Keyframes),"Selection cannot reload native providers.");SourceUnchanged();
                    await Invoke("settings/hotkeys/reset-command",new HotkeyCommandReset(HotkeyContext.Default,"tool/line/select"));await Invoke("settings/hotkeys/reset-command",new HotkeyCommandReset(HotkeyContext.Default,"subtitle/select/visible"));
                    Require(StartupDiagnostics.BindingWarningCount==0&&StartupDiagnostics.FrameworkErrorCount==0,"Select Lines workflow must have no binding/framework errors.");
                    File.WriteAllText(Marker,"verified");_timer.Stop();options.FinishVerification("PASS: separate real-window Select Lines acceptance; actual native menu/dropdown Enter, remapped grid F9 and visible-selection F11; usable dialog controls at CI size; real Apply/Confirm/Escape; invalid regex and active-draft atomic validation; text/actor/exact/contains and dialogue/comment filters; replace/add/subtract/intersect including empty selection; IME Enter isolation; persisted criteria across processes; source/undo/unknown/Mangetsu/Unicode/provider retention; source-derived inclusive VFR visibility and comments; current frame retained despite autoseek.\n");StartupDiagnostics.Complete();desktop.Shutdown(0);return;
            }
        }
        catch(Exception exception){_timer.Stop();Program.Fatal("Packaged selection acceptance at stage "+(_stage-1),exception);desktop.Shutdown(1);}
        finally{_ticking=false;}
    }
    private void SourceUnchanged()=>Require(ReferenceEquals(_editor,model.ActiveEditor)&&_editor!.Document.Serialize()==_source&&!_editor.Undo.CanUndo,"Selection must not mutate subtitle source or undo.");
    private async Task Invoke(string id,object? parameter=null)=>Require(await model.Registry.InvokeAsync(id,new(model.ActiveDocumentId),parameter),"Selection preparation command failed: "+id);
    private static void Press(Control control,Key key)
    {
        if(control is ListBox list)control=list.ContainerFromIndex(Math.Max(0,list.SelectedIndex)) as Control??throw new InvalidOperationException("A realized grid row must receive selection keys.");
        (TopLevel.GetTopLevel(control) as Window)?.Activate();Require(control.Focus()&&control.IsKeyboardFocusWithin,"The shipped tool/menu/grid control must own keyboard focus.");control.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Source=control,Key=key});
    }
    private static void Within(Control control,Window owner)
    {var origin=control.TranslatePoint(new Point(0,0),owner);Require(origin is {} point&&control.Bounds.Width>0&&control.Bounds.Height>0&&point.X>=-1&&point.Y>=-1&&point.X+control.Bounds.Width<=owner.ClientSize.Width+1&&point.Y+control.Bounds.Height<=owner.ClientSize.Height+1,"Select Lines control must remain usable inside its native dialog: "+control.Name);}
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
