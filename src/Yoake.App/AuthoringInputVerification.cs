using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI;
using Yoake.UI.Controls;
using Yoake.UI.ViewModels;

namespace Yoake.App;

// Exercise the shipping dialog and focused editor across dispatcher/layout ticks.
internal sealed class AuthoringInputVerification(MainWindow window,MainWindowViewModel model,Action<Control,string,double> capture)
{
    private int _stage;
    private Window? _dialog;
    private FontPicker? _font;
    private AssEvent? _line;
    private string _original="",_stable="";
    private DateTime _afterBurst;
    private AssTextBox Text=>window.FindControl<AssTextBox>("SubtitleText")!;
    public bool Tick()
    {
        switch(_stage++)
        {
            case 0:
                _=window.ChooseFontAsync("Arial","60");return false;
            case 1:
                _dialog=window.OwnedWindows.OfType<Window>().Single(w=>w.Name=="SubtitleFontDialog");
                _font=_dialog.GetVisualDescendants().OfType<FontPicker>().Single();
                Require(_font.InstalledFamilies.Count>0,"Shipping font dialog must enumerate installed families.");
                _font.FontName=_font.InstalledFamilies.First();
                var arrow=_font.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="BrowseFonts");
                Require(arrow.Bounds.Width>0&&arrow.IsVisible,"Font browse arrow must be visible.");
                arrow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));return false;
            case 2:
                var list=_font!.BrowserList;
                Require(_font.IsBrowserOpen&&list.GetVisualRoot() is not null&&list.Bounds.Width>=260&&list.Bounds.Height>20,"Font browser must have an actual popup visual root and visible dimensions.");
                var rows=list.GetVisualDescendants().OfType<ListBoxItem>().Where(r=>r.Bounds.Height>0&&r.IsVisible).ToArray();
                Require(rows.Length>0&&list.SelectedItem as string==_font.FontName,"Installed current family must be selected and realized.");
                capture(list,"subtitle-font-browser-visible",1);
                var choice=rows.First(r=>r.Content is string family&&family!=_font.FontName);
                list.SelectedItem=choice.Content;
                var pointer=new Pointer(413,PointerType.Mouse,true);
                choice.RaiseEvent(new PointerReleasedEventArgs(choice,pointer,(Avalonia.Visual)list.GetVisualRoot()!,choice.TranslatePoint(new(4,4),(Avalonia.Visual)list.GetVisualRoot()!)!.Value,0,new(RawInputModifiers.None,PointerUpdateKind.LeftButtonReleased),KeyModifiers.None,MouseButton.Left));
                Require(!_font.IsBrowserOpen&&_font.FontName==(string)choice.Content!,"Clicking an actual font row must choose its exact family.");
                _font.OpenBrowser();return false;
            case 3:
                _font!.BrowserList.SelectedItem=_font.InstalledFamilies.Last();
                _font.BrowserList.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Source=_font.BrowserList,Key=Key.Enter});
                Require(!_font.IsBrowserOpen&&_font.FontName==_font.InstalledFamilies.Last(),"Enter in font browser must choose.");
                _font.FontName="D F 円楷書 Std W5";_font.OpenBrowser();return false;
            case 4:
                Require(_font!.FontName=="D F 円楷書 Std W5"&&_font.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.IsVisible&&t.Text=="Unavailable family; exact name retained"),"Missing exact family must remain visible and explicitly unavailable.");
                _font.BrowserList.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Source=_font.BrowserList,Key=Key.Escape});
                Require(!_font.IsBrowserOpen,"Escape must close the visible font browser.");_dialog!.Close();
                _line=model.SelectedEvent!;_original=_line.Text;return false;
            case 5:
                Text.Focus();Text.SetCurrentValue(TextBox.TextProperty,"enter draft");Caret(3,3);Key(Key.Enter);
                Require(_line!.Text=="enter draft"&&!ReferenceEquals(model.SelectedEvent,_line)&&!_line.Text.Contains('\n')&&!_line.Text.Contains('\r')&&!_line.Text.Contains(@"\N"),"Focused plain Enter must commit and advance without any newline.");
                model.SelectedEvent=_line;return false;
            case 6:
                Text.Focus();Text.SetCurrentValue(TextBox.TextProperty,"ctrl draft");Caret(3,3);Key(Key.Enter,KeyModifiers.Control);
                Require(ReferenceEquals(model.SelectedEvent,_line)&&_line!.Text=="ctrl draft","Ctrl+Enter must commit and stay.");
                Text.SetCurrentValue(TextBox.TextProperty,"abcdef");Caret(2,4);Key(Key.Enter,KeyModifiers.Shift);
                _stable=@"ab\Nef";
                Require(Text.Text==_stable&&Text.CaretIndex==4&&Text.SelectionStart==4&&Text.SelectionEnd==4,"Shift+Enter must replace selection immediately with literal ASS syntax and place the caret after it.");
                _afterBurst=DateTime.UtcNow.AddMilliseconds(1150);return false;
            case 7:
                if(DateTime.UtcNow<_afterBurst){_stage--;return false;}
                Require(Text.Text==_stable&&_line!.Text==_stable,"Hard newline text must remain identical after the edit-burst timeout.");
                Caret(2,2);Key(Key.Enter,KeyModifiers.Shift);Require(Text.Text==@"ab\N\Nef"&&Text.CaretIndex==4,"Shift+Enter at a caret must insert immediate literal syntax.");
                var presenter=Text.GetVisualDescendants().OfType<TextPresenter>().Single();presenter.PreeditText="仮";
                var selected=model.SelectedEvent;var source=model.SelectedEvent!.Text;Key(Key.Enter);
                Require(ReferenceEquals(model.SelectedEvent,selected)&&model.SelectedEvent.Text==source&&presenter.PreeditText=="仮","Composition Enter must remain owned by IME without commit/navigation.");
                presenter.PreeditText=null;model.CommitDraft();model.ActiveEditor!.SetField(_line!,"Text",_original,"Restore input verification fixture");return true;
            default:return true;
        }
    }
    private void Caret(int start,int end){Text.CaretIndex=end;Text.SelectionStart=start;Text.SelectionEnd=end;}
    private void Key(Key key,KeyModifiers modifiers=KeyModifiers.None)=>Text.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Source=Text,Key=key,KeyModifiers=modifiers});
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
