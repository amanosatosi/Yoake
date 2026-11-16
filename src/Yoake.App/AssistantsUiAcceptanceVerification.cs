using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yoake.Core.Commands;
using Yoake.Core.Logging;
using Yoake.UI;
using Yoake.UI.ViewModels;

namespace Yoake.App;

internal sealed class AssistantsUiAcceptanceVerification(IClassicDesktopStyleApplicationLifetime desktop,
    MainWindow window,MainWindowViewModel model,StartupOptions options)
{
    private readonly DispatcherTimer _timer=new() { Interval=TimeSpan.FromMilliseconds(200) };
    private int _stage;private bool _ticking;
    private EditorAssistantWindow? _assistant;
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
                    var exact=model.ActiveEditor!.Document.Serialize();Require(model.OpenSubtitle(path),"Reopen assistant output.");
                    Require(model.ActiveEditor!.Document.Serialize()==exact,"Translated ASS and styles must survive reopen.");
                    _timer.Stop();options.FinishVerification("PASS: native Translation and Styling Assistants, Japanese/Burmese source preservation, IME preedit, commit/navigation, independent undo/redo, styles, Save/reopen.\n");StartupDiagnostics.Complete();desktop.Shutdown(0);return;
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
