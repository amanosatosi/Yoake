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
using System.Text;

namespace Yoake.App;

internal sealed class AssistantsUiAcceptanceVerification(IClassicDesktopStyleApplicationLifetime desktop,
    MainWindow window,MainWindowViewModel model,StartupOptions options)
{
    private readonly DispatcherTimer _timer=new() { Interval=TimeSpan.FromMilliseconds(200) };
    private int _stage;private bool _ticking;
    private EditorAssistantWindow? _assistant;
    private MangetsuMetadataWindow? _metadata;
    private PostprocessWindow? _postprocess;
    private string _postprocessBefore="";
    private SubtitleEncodingWindow? _encoding;
    private Task<int?>? _encodingResult;
    private byte[] _legacyBytes=[];
    private string _before="",_translated="";
    private T Get<T>(string name) where T:Control=>_assistant!.FindControl<T>(name)??throw new InvalidOperationException("Missing Assistant control: "+name);
    public void Start() { _timer.Tick+=Tick;window.Opened+=(_,_)=>_timer.Start(); }
    private async void Tick(object? sender,EventArgs args)
    {
        if(_ticking)return;_ticking=true;options.MarkStartupVerified();
        try
        {
            switch(_stage++)
            {
                case 0:
                    await Invoke(CommandIds.GridInsertAfter);model.Draft!.Text=@"{\future(opaque)\bord2}日本語{\i1}二番";await Invoke(CommandIds.EditCommit);
                    _before=model.ActiveEditor!.Document.Serialize();
                    window.FindControl<MenuItem>("ToolsMenu")!.IsSubMenuOpen=true;Press(window.FindControl<MenuItem>("TranslationAssistantMenu")!);return;
                case 1:
                    _assistant=window.OwnedWindows.OfType<EditorAssistantWindow>().Single();
                    Require(_assistant.TryGetPlatformHandle() is { Handle:not 0 },"Translation Assistant needs a real native window.");
                    Require(Get<TextBox>("AssistantOriginal").Text=="日本語","Translation starts on the first text block.");
                    Get<TextBox>("AssistantTranslation").Text="မြန်မာ\nစာ";return;
                case 2:
                    var input=Get<TextBox>("AssistantTranslation");input.Focus();var presenter=input.GetVisualDescendants().OfType<TextPresenter>().Single();
                    presenter.PreeditText="မ";Key(input,Avalonia.Input.Key.Enter);
                    Require(model.ActiveEditor!.Document.Serialize()==_before,"IME Enter must not publish translation.");presenter.PreeditText=null;
                    Key(input,Avalonia.Input.Key.Enter);return;
                case 3:
                    Require(model.SelectedEvent!.Text==@"{\future(opaque)\bord2}မြန်မာ\Nစာ{\i1}二番","Translate Japanese to Burmese without rewriting override source.");
                    Require(Get<TextBox>("AssistantOriginal").Text=="二番","Enter must advance to the next block.");
                    Get<TextBox>("AssistantTranslation").Text="ဒုတိယ";return;
                case 4:
                    Press(Get<Button>("AssistantPreview"));return;
                case 5:
                    _translated=model.ActiveEditor!.Document.Serialize();Require(_translated.Contains("ဒုတိယ",StringComparison.Ordinal),"Commit and stay must publish the current block.");
                    _assistant!.Close();await Invoke(CommandIds.EditUndo);await Invoke(CommandIds.EditUndo);
                    Require(model.ActiveEditor.Document.Serialize()==_before,"Both translations undo independently to exact source.");
                    await Invoke(CommandIds.EditRedo);await Invoke(CommandIds.EditRedo);
                    Require(model.ActiveEditor.Document.Serialize()==_translated,"Redo preserves the translated source.");
                    var style=model.ActiveEditor.Document.Styles[0].Clone();style.Name="Burmese";model.ActiveEditor.AddStyle(style);
                    await Invoke("tool/style/assistant");return;
                case 6:
                    _assistant=window.OwnedWindows.OfType<EditorAssistantWindow>().Single();
                    Get<AutoCompleteBox>("AssistantStyle").Text="Burmese";return;
                case 7:
                    Press(Get<Button>("AssistantPreview"));return;
                case 8:
                    Require(model.SelectedEvent!.Style=="Burmese","Styling Assistant must apply the selected existing style.");
                    _assistant!.Close();var path=Path.Combine(options.ProfileDirectory!,"assistant.ass");Require(model.SaveActiveSubtitle(path),"Save assistant output.");
                    var exact=model.ActiveEditor!.Document.Serialize();await Invoke("subtitle/close");Require(model.OpenSubtitle(path),"Reopen assistant output from disk after closing the document.");
                    Require(model.ActiveEditor!.Document.Serialize()==exact,"Translated ASS and styles must survive reopen.");
                    await Invoke("mangetsu/metadata");return;
                case 9:
                    _metadata=window.OwnedWindows.OfType<MangetsuMetadataWindow>().Single();
                    _metadata.FindControl<TextBox>("MangetsuMetadataText")!.Text=@"{\vert1\boxp(10,20,5,15)\future(opaque)}";return;
                case 10:
                    Press(_metadata!.FindControl<Button>("MangetsuMetadataApply")!);return;
                case 11:
                    Require(MangetsuMetadata.Kind(model.Events[0])==MangetsuMetadataKind.Style&&model.Events[0].Text.Contains(@"\future(opaque)",StringComparison.Ordinal),"Metadata editor must preserve future tags in the initial style comment.");
                    _metadata!.FindControl<ComboBox>("MangetsuMetadataKind")!.SelectedItem=MangetsuMetadataKind.Actor;
                    _metadata.FindControl<AutoCompleteBox>("MangetsuMetadataTarget")!.Text="Nene";
                    _metadata.FindControl<TextBox>("MangetsuMetadataText")!.Text=@"{\1c&HFF0000&}";return;
                case 12:
                    Press(_metadata!.FindControl<Button>("MangetsuMetadataApply")!);return;
                case 13:
                    Require(MangetsuMetadata.Kind(model.Events[0])==MangetsuMetadataKind.Style&&MangetsuMetadata.Kind(model.Events[1])==MangetsuMetadataKind.Actor,"Style extensions must precede Actor Colorcoding and dialogue.");
                    _metadata!.Close();var metadataSource=model.ActiveEditor!.Document.Serialize();
                    await Invoke(CommandIds.EditUndo);Require(MangetsuMetadata.Initial(model.ActiveEditor.Document).Length==1,"Actor metadata must undo independently.");
                    await Invoke(CommandIds.EditRedo);Require(model.ActiveEditor.Document.Serialize()==metadataSource,"Metadata redo restores exact source.");
                    var metadataPath=Path.Combine(options.ProfileDirectory!,"metadata.ass");Require(model.SaveActiveSubtitle(metadataPath),"Save metadata.");await Invoke("subtitle/close");Require(model.OpenSubtitle(metadataPath),"Reopen metadata from disk.");
                    Require(model.ActiveEditor!.Document.Serialize()==metadataSource,"Metadata order and payload survive file round-trip.");
                    _encoding=new SubtitleEncodingWindow();_encodingResult=_encoding.ShowDialog<int?>(window);return;
                case 14:
                    var choice=_encoding!.FindControl<AutoCompleteBox>("SubtitleEncoding")!;
                    choice.SelectedItem=choice.ItemsSource!.Cast<SubtitleEncodingChoice>().Single(c=>c.CodePage==932);
                    Press(_encoding.FindControl<Button>("OpenWithEncoding")!);return;
                case 15:
                    Require(_encodingResult!.IsCompleted&&_encodingResult.Result==932,"The native encoding chooser must return Shift-JIS.");
                    var legacySource=AssDocument.CreateEmpty().Serialize()+"Dialogue: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,,日本語\r\n";
                    _legacyBytes=SubtitleEncodings.Get(932).GetBytes(legacySource);
                    var legacyPath=Path.Combine(options.ProfileDirectory!,"legacy.ass");File.WriteAllBytes(legacyPath,_legacyBytes);
                    Require(await model.Registry.InvokeAsync("subtitle/open/charset",new(),new OpenSubtitleEncodingRequest(legacyPath,932)),"Open legacy charset command.");return;
                case 16:
                    Require(model.SelectedEvent!.Text=="日本語"&&model.ActiveEditor!.Document.EncodingCodePage==932,"Open the Japanese source without replacement characters.");
                    window.FindControl<TextBox>("SubtitleText")!.Text="မြန်မာစာ";return;
                case 17:
                    await Invoke(CommandIds.EditCommit);
                    var utf8Path=Path.Combine(options.ProfileDirectory!,"burmese.ass");
                    Require(await model.Registry.InvokeAsync("subtitle/save/utf8",new(),utf8Path),"Save UTF-8 command.");
                    Require(AssDocument.Load(utf8Path).Events.Single().Text=="မြန်မာစာ","Burmese translation must survive UTF-8 save.");
                    Require(File.ReadAllBytes(Path.Combine(options.ProfileDirectory!,"legacy.ass")).AsSpan().SequenceEqual(_legacyBytes),"Encoding conversion must preserve the original Japanese source file.");
                    var timingPath=Path.Combine(options.ProfileDirectory!,"postprocess-input.ass");
                    File.WriteAllText(timingPath,AssDocument.CreateEmpty().Serialize()+"Dialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,{\\future(opaque)}日本語 မြန်မာ\r\n");
                    Require(model.OpenSubtitle(timingPath),"Open timing fixture.");_postprocessBefore=model.ActiveEditor!.Document.Serialize();await Invoke("tool/time/postprocess");return;
                case 18:
                    _postprocess=window.OwnedWindows.OfType<PostprocessWindow>().Single();
                    _postprocess.FindControl<CheckBox>("PostprocessAdjacent")!.IsChecked=false;
                    _postprocess.FindControl<CheckBox>("PostprocessLeadInEnabled")!.IsChecked=true;
                    _postprocess.FindControl<CheckBox>("PostprocessLeadOutEnabled")!.IsChecked=true;
                    _postprocess.FindControl<NumericUpDown>("PostprocessLeadIn")!.Value=90;
                    _postprocess.FindControl<NumericUpDown>("PostprocessLeadOut")!.Value=110;
                    Press(_postprocess.FindControl<Button>("PostprocessPreview")!);return;
                case 19:
                    if(model.HasEditOperation){_stage--;return;}
                    Require(model.ActiveEditor!.Document.Serialize()==_postprocessBefore,"Timing Preview must preserve the original source.");
                    Press(_postprocess!.FindControl<Button>("PostprocessApply")!);return;
                case 20:
                    if(model.HasEditOperation){_stage--;return;}
                    Require(model.SelectedEvent!.StartMilliseconds==910&&model.SelectedEvent.EndMilliseconds==2110,"Native postprocessor must apply configured leads.");
                    Require(model.SelectedEvent.Text==@"{\future(opaque)}日本語 မြန်မာ","Postprocessing must preserve exact text and override tags.");
                    await Invoke(CommandIds.EditUndo);Require(model.ActiveEditor!.Document.Serialize()==_postprocessBefore,"Timing postprocessing must undo in one step.");
                    await Invoke(CommandIds.EditRedo);
                    _timer.Stop();options.FinishVerification("PASS: native Translation/Styling, Japanese/Burmese IME preedit, undo/redo, Mangetsu metadata, Shift-JIS/UTF-8, Save/close/reopen and Timing Postprocessor preview/apply/undo.\n");StartupDiagnostics.Complete();desktop.Shutdown(0);return;
            }
        }
        catch(Exception error) { _timer.Stop();Program.Fatal("Assistants acceptance stage "+_stage,error);desktop.Shutdown(1); }
        finally { _ticking=false; }
    }
    private async Task Invoke(string id) { Require(await model.Registry.InvokeAsync(id,new(model.ActiveDocumentId)),"Assistant command failed: "+id+" · "+model.SubtitleStatus); }
    private static void Press(Control control) { Require(control.Focus(),"Assistant action must be focusable.");Key(control,Avalonia.Input.Key.Enter); }
    private static void Key(Control control,Key key)=>control.RaiseEvent(new KeyEventArgs { RoutedEvent=InputElement.KeyDownEvent,Key=key,Source=control });
    private static void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
}
