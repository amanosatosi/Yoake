using Yoake.Core.Commands;
using Yoake.Core.Subtitles;

namespace Yoake.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    public int TextSelectionStart {get;set;}
    public int TextSelectionEnd {get;set;}
    public event EventHandler? TextFormattingApplied;
    public string StyleLibraryPath=>Path.Combine(Path.GetDirectoryName(_settingsStore.Path)!,"style-library.json");
    private AssStyle FormattingStyle=>ActiveEditor!.Document.Styles.FirstOrDefault(s=>s.Name==SelectedEvent?.Style)??ActiveEditor.Document.Styles.FirstOrDefault()??AssDocument.CreateEmpty().Styles[0];
    private void FormatText(string name,Func<string,int,int,AssStyle,AssTextEdit> operation)
    {
        if(Draft is null||SelectedEvent is null||ActiveEditor is null)return;
        var raw=Draft.Text;
        int Normalize(int index)=>raw[..Math.Clamp(index,0,raw.Length)].Replace("\r\n","\\N").Replace("\r","\\N").Replace("\n","\\N").Length;
        var start=Normalize(TextSelectionStart);var end=Normalize(TextSelectionEnd);
        if(!CommitDraft())return;
        var edit=operation(SelectedEvent.Text,start,end,FormattingStyle);
        ActiveEditor.SetField(SelectedEvent,"Text",edit.Text,name);ReloadDraft();
        TextSelectionStart=edit.SelectionStart;TextSelectionEnd=edit.SelectionEnd;TextCursor=edit.SelectionEnd;
        TextFormattingApplied?.Invoke(this,EventArgs.Empty);
    }
    private AssStyle? ResolveFormatStyle(string name)=>ActiveEditor?.Document.Styles.FirstOrDefault(s=>s.Name==name);
    private void ToggleFormat(string tag)=>FormatText("Format "+tag,(text,start,end,style)=>AssFormatting.Toggle(text,start,end,tag,style,ResolveFormatStyle));
    private async ValueTask FormatFontAsync()
    {
        if(Dialogs is null||Draft is null||ActiveEditor is null)return;
        var state=AssFormatting.State(Draft.Text,Math.Min(TextSelectionStart,TextSelectionEnd),FormattingStyle,ResolveFormatStyle);
        var choice=await Dialogs.ChooseFontAsync(state.GetValueOrDefault("fn",""),state.GetValueOrDefault("fs","60"));
        if(choice is not null)FormatText("Change subtitle font",(text,start,end,style)=>AssFormatting.Apply(text,start,end,new Dictionary<string,string>{{"fn",choice.Family},{"fs",choice.Size}},style,ResolveFormatStyle));
    }
    private async ValueTask FormatColorAsync(int channel)
    {
        if(Dialogs is null||Draft is null||ActiveEditor is null)return;
        var state=AssFormatting.State(Draft.Text,Math.Min(TextSelectionStart,TextSelectionEnd),FormattingStyle,ResolveFormatStyle);
        AssColor.TryParse(state.GetValueOrDefault(channel+"c"),out var rgb);AssColor.TryParse(state.GetValueOrDefault(channel+"a"),out var alpha);
        var color=await Dialogs.ChooseColorAsync(rgb with{Transparency=alpha.Red});
        if(color is {} chosen)FormatText("Change subtitle color",(text,start,end,style)=>AssFormatting.Apply(text,start,end,new Dictionary<string,string>{{channel+"c",chosen.RgbOverride},{channel+"a",chosen.AlphaOverride}},style,ResolveFormatStyle));
    }
}
