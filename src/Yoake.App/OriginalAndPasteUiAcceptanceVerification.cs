using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yoake.Core.Commands;
using Yoake.Core.Logging;
using Yoake.Core.Subtitles;
using Yoake.UI;
using Yoake.UI.ViewModels;

namespace Yoake.App;

// Separate native process, shipping menus/keyboard/dialog/clipboard, no mocks.
internal sealed class OriginalAndPasteUiAcceptanceVerification(IClassicDesktopStyleApplicationLifetime desktop,
    MainWindow window,MainWindowViewModel model,StartupOptions options)
{
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(100)};
    private int _stage;private bool _ticking;private Task<bool>? _opening;
    private PasteOverWindow? _dialog;private AssEvent[] _rows=[];
    private string _original="",_source="";private object? _draft,_times,_samples;private Guid? _document;
    private bool _prepareOriginalInsert;
    private string _insertSelection="";
    private Task<bool>? _cancelCommand;
    private CancellationTokenSource? _pasteCancellation;
    private TextBox Text=>window.FindControl<TextBox>("SubtitleText")!;
    private T Field<T>(string name)where T:Control=>_dialog!.FindControl<T>(name)??throw new InvalidOperationException("Missing paste control: "+name);
    public void Start(){_timer.Tick+=Tick;_timer.Start();}
    private async void Tick(object? sender,EventArgs args)
    {
        if(_ticking||!window.IsVisible||window.Bounds.Width<=0)return;_ticking=true;options.MarkStartupVerified();
        try
        {
            switch(_stage++)
            {
                case 0:
                    Require(window.TryGetPlatformHandle() is {Handle:not 0},"Acceptance needs a real native window.");
                    var path=Path.Combine(options.ProfileDirectory!,"original-paste.ass");
                    File.WriteAllText(path,"[Script Info]\nPlayResX: 320\nPlayResY: 180\n[Events]\r\nFormat: "+string.Join(',',AssDocument.EventFormat)+",Unknown\n"+
                        "Dialogue: 0,0:00:00.00,0:00:01.00,Default,A,01,02,03,fx,{\\future(x)\\distort(1,2)}原文 é,opaque\n"+
                        "; untouched\rDialogue: 1,0:00:01.00,0:00:02.00,Default,B,04,05,06,fx,second,raw\n"+
                        "Comment: 2,0:00:02.00,0:00:03.00,Default,C,07,08,09,fx,third,keep\n");
                    Require(model.OpenSubtitle(path),"Open fixture.");_rows=model.Events.ToArray();_original=model.OriginalText;_document=model.ActiveDocumentId;
                    await Invoke(CommandIds.ViewAudioVideo);_opening=model.OpenMediaAsync(Path.Combine(options.VerificationMedia!,"av.avi"));return;
                case 1:
                    if(!_opening!.IsCompleted){_stage--;return;}Require(await _opening,"Native media fixture.");_times=model.FrameTimes;_samples=model.WaveformSamples;
                    Press(window.FindControl<CheckBox>("ShowOriginalToggle")!,Key.Space);return;
                case 2:
                    var original=window.FindControl<TextBox>("OriginalText")!;
                    Require(model.ShowOriginal&&original.IsReadOnly&&original.IsVisible&&original.Text==_original,"Visible read-only Original snapshot.");
                    Within(original,window);Within(Text,window);Within(window.FindControl<Button>("OriginalInsert")!,window);
                    Require(window.FindControl<MenuItem>("ShowOriginalMenu")!.IsChecked,"Show Original menu reflects toggle.");
                    model.Draft!.Text="translated";await Invoke(CommandIds.EditCommit);await Invoke(CommandIds.EditUndo);await Invoke(CommandIds.EditRedo);
                    Require(model.OriginalText==_original,"Typing, commit, undo and redo keep Original stable.");
                    _prepareOriginalInsert=true;return;
                case 3:
                    if(_prepareOriginalInsert)
                    {
                        Require(Text.Text=="translated","The visible undo/redo binding must settle before selecting text.");
                        Text.Focus();Text.SelectAll();
                        Require(model.TextSelectionStart==0&&model.TextSelectionEnd==model.Draft!.Text.Length,"Native Select All must reach the semantic editor selection.");
                        _insertSelection=$"UI {Text.SelectionStart}:{Text.SelectionEnd}, model {model.TextSelectionStart}:{model.TextSelectionEnd}";
                        _prepareOriginalInsert=false;Press(window.FindControl<Button>("OriginalInsert")!,Key.Enter);_stage--;return;
                    }
                    Require(model.Draft!.Text==_original,$"Actual Insert Original button replaces the current selection. Before: {_insertSelection}; after UI {Text.SelectionStart}:{Text.SelectionEnd}, model {model.TextSelectionStart}:{model.TextSelectionEnd}; expected '{_original}', actual '{model.Draft.Text}'.");
                    model.Draft.Text="{\\future(x)}visible{\\p1}m 0 0 l 5 5{\\p0}";
                    Press(window.FindControl<Button>("OriginalClearText")!,Key.Enter);return;
                case 4:
                    Require(model.Draft!.Text=="{\\future(x)}{\\p1}m 0 0 l 5 5{\\p0}","Clear Text retains tags and drawings.");
                    Press(window.FindControl<Button>("OriginalClear")!,Key.Enter);return;
                case 5:
                    Require(model.Draft!.Text=="","Clear removes all line text.");Press(window.FindControl<Button>("OriginalRevert")!,Key.Enter);return;
                case 6:
                    Require(model.Draft!.Text==_original&&model.OriginalText==_original,"Revert restores stable Original.");
                    model.Draft.Start="invalid";model.SelectedEvent=_rows[1];Require(ReferenceEquals(model.SelectedEvent,_rows[0])&&model.OriginalText==_original,"Validation failure must retain snapshot and active line.");
                    await Invoke(CommandIds.EditCancel);_source=model.ActiveEditor!.Document.Serialize();
                    await window.WriteClipboardAsync("[Events]\nFormat: Text,End,Actor,Start,Future\r\nComment: 日本語, comma,0:00:00.50,new actor,0:00:00.00,incoming\rDialogue: မြန်မာ,0:00:01.50,other,0:00:01.00,extra\n");
                    window.FindControl<MenuItem>("EditMenu")!.IsSubMenuOpen=true;Press(window.FindControl<MenuItem>("PasteOverMenu")!,Key.Enter);return;
                case 7:
                    _dialog=window.OwnedWindows.OfType<PasteOverWindow>().SingleOrDefault();if(_dialog is null){_stage--;return;}
                    Require(_dialog.IsVisible&&_dialog.TryGetPlatformHandle() is {Handle:not 0},"Paste Over opens a real native modal dialog.");
                    foreach(var c in _dialog.GetVisualDescendants().OfType<Control>().Where(c=>c is Button or CheckBox))Within(c,_dialog);
                    Press(Field<Button>("PastePresetAll"),Key.Enter);Require(Checks()==11,"All preset selects every field.");
                    Press(Field<Button>("PastePresetNone"),Key.Enter);Require(Checks()==0,"None preset.");
                    Press(Field<Button>("PastePresetTimes"),Key.Enter);Require(Checks()==2&&Field<CheckBox>("PasteFieldStart").IsChecked==true&&Field<CheckBox>("PasteFieldEnd").IsChecked==true,"Times preset.");
                    Press(Field<Button>("PastePresetText"),Key.Enter);Require(Checks()==1&&Field<CheckBox>("PasteFieldText").IsChecked==true,"Text preset.");
                    Press(Field<Button>("PasteConfirm"),Key.Enter);return;
                case 8:
                    if(window.OwnedWindows.OfType<PasteOverWindow>().Any()){_stage--;return;}
                    Require(_rows[0].Text=="日本語, comma"&&_rows[1].Text=="မြန်မာ"&&_rows[2].Text=="third","Single selection pastes downward, respecting clipboard count.");
                    Require(_rows[0].Actor=="A"&&!_rows[0].IsComment&&_rows[0].Get("Unknown")=="opaque"&&_rows[0].MarginL=="01","Unselected fields and unknown columns remain exact.");
                    Require(model.OriginalText==_original&&ReferenceEquals(model.FrameTimes,_times)&&ReferenceEquals(model.WaveformSamples,_samples),"Original and independent media stay unchanged.");
                    await Invoke(CommandIds.EditUndo);Require(model.ActiveEditor!.Document.Serialize()==_source,"One undo restores exact source.");
                    await window.WriteClipboardAsync("first\r\nlast\nignored");model.SetSelectedEvents([_rows[0],_rows[2]]);
                    Text.Focus();var presenter=Text.GetVisualDescendants().OfType<TextPresenter>().Single();presenter.PreeditText="にほん";Press(Text,Key.V,KeyModifiers.Control|KeyModifiers.Shift);presenter.PreeditText="";return;
                case 9:
                    Require(!window.OwnedWindows.OfType<PasteOverWindow>().Any()&&model.ActiveEditor!.Document.Serialize()==_source,"IME preedit protects Paste Over shortcut.");
                    Press(Text,Key.V,KeyModifiers.Control|KeyModifiers.Shift);return;
                case 10:
                    _dialog=window.OwnedWindows.OfType<PasteOverWindow>().SingleOrDefault();if(_dialog is null){_stage--;return;}
                    Require(Field<CheckBox>("PasteFieldText").IsChecked==true&&Checks()==1,"Successful field choices persist in NativeAOT settings.");
                    Press(Field<Button>("PasteConfirm"),Key.Enter);return;
                case 11:
                    if(window.OwnedWindows.OfType<PasteOverWindow>().Any()){_stage--;return;}
                    Require(_rows[0].Text=="first"&&_rows[1].Text=="second"&&_rows[2].Text=="last","Ctrl-Shift-V uses only the sorted multiple selection.");
                    await Invoke(CommandIds.EditUndo);Require(model.ActiveEditor!.Document.Serialize()==_source,"Multi-row undo is atomic.");
                    model.Draft!.Text="pending draft";_draft=model.Draft;
                    Press(Text,Key.V,KeyModifiers.Control|KeyModifiers.Shift);return;
                case 12:
                    _dialog=window.OwnedWindows.OfType<PasteOverWindow>().SingleOrDefault();if(_dialog is null){_stage--;return;}
                    Press(Field<Button>("PasteCancel"),Key.Enter);return;
                case 13:
                    if(window.OwnedWindows.OfType<PasteOverWindow>().Any()){_stage--;return;}
                    Require(ReferenceEquals(model.Draft,_draft)&&model.Draft!.Text=="pending draft"&&model.ActiveEditor!.Document.Serialize()==_source,"Cancel preserves uncommitted draft and source.");
                    await Invoke(CommandIds.EditCancel);model.SetSelectedEvents([]);Require(!window.FindControl<MenuItem>("PasteOverMenu")!.IsEnabled,"No selection disables Paste Over.");
                    model.SetSelectedEvents([_rows[0]]);await window.WriteClipboardAsync("Dialogue: malformed");Require(!await model.Registry.InvokeAsync("edit/line/paste/over",new(model.ActiveDocumentId)),"Malformed clipboard is UI-safe failure.");
                    Require(model.ActiveEditor!.Document.Serialize()==_source&&!window.OwnedWindows.OfType<PasteOverWindow>().Any(),"Malformed data cannot open a publication dialog or alter source.");
                    await window.WriteClipboardAsync("cancelled replacement");_pasteCancellation=new CancellationTokenSource();
                    _cancelCommand=model.Registry.InvokeAsync("edit/line/paste/over",new(model.ActiveDocumentId),cancellationToken:_pasteCancellation.Token).AsTask();return;
                case 14:
                    _dialog=window.OwnedWindows.OfType<PasteOverWindow>().SingleOrDefault();if(_dialog is null){_stage--;return;}
                    Require(_dialog.IsVisible,"Cancellation must operate on the real modal field dialog.");_pasteCancellation!.Cancel();return;
                case 15:
                    if(!_cancelCommand!.IsCompleted||window.OwnedWindows.OfType<PasteOverWindow>().Any()){_stage--;return;}
                    Require(!await _cancelCommand&&model.ActiveEditor!.Document.Serialize()==_source&&model.OriginalText==_original,"Scoped cancellation closes the native dialog without publishing or changing Original.");
                    _pasteCancellation!.Dispose();
                    await Invoke(CommandIds.SubtitleNew);await Invoke(CommandIds.GridInsertAfter);Require(model.OriginalText=="","New document owns its own Original.");await Invoke(CommandIds.WorkspaceActivateTab,_document);
                    Require(model.OriginalText==_original&&ReferenceEquals(model.FrameTimes,_times),"Tab switching restores original owner and media.");
                    _timer.Stop();options.FinishVerification("PASS: separate native Original/Paste Over acceptance; real toggle, read-only snapshot, insert/revert/clear buttons, stable undo/redo/validation lifecycle; actual native dialog/menu/keyboard/clipboard; all field presets; custom Format Unicode and comments; single/multiple target rules; one-step exact-source undo; persisted fields; button and scoped native-dialog cancellation/draft retention; IME isolation; empty selection and malformed clipboard failure; independent media and document switching.\n");StartupDiagnostics.Complete();desktop.Shutdown(0);return;
            }
        }
        catch(Exception error){_timer.Stop();Program.Fatal("Packaged Original/Paste Over acceptance at stage "+(_stage-1),error);desktop.Shutdown(1);}
        finally{_ticking=false;}
    }
    private int Checks()=>_dialog!.GetVisualDescendants().OfType<CheckBox>().Count(c=>c.IsChecked==true);
    private async Task Invoke(string id,object? parameter=null)=>Require(await model.Registry.InvokeAsync(id,new(model.ActiveDocumentId),parameter),"Preparation command: "+id);
    private static void Press(Control control,Key key,KeyModifiers modifiers=KeyModifiers.None)
    {
        (TopLevel.GetTopLevel(control) as Window)?.Activate();Require(control.Focus()&&control.IsKeyboardFocusWithin,"Shipping control must receive keyboard focus: "+control.Name);
        control.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Source=control,Key=key,KeyModifiers=modifiers});
        if(key==Key.Space)control.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyUpEvent,Source=control,Key=key,KeyModifiers=modifiers});
    }
    private static void Within(Control control,Window owner)
    {
        var point=control.TranslatePoint(new Point(0,0),owner);
        Require(point is {} p&&control.Bounds.Width>0&&control.Bounds.Height>0&&p.X>=-1&&p.Y>=-1&&p.X+control.Bounds.Width<=owner.ClientSize.Width+1&&p.Y+control.Bounds.Height<=owner.ClientSize.Height+1,"Control fits native window: "+control.Name);
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
