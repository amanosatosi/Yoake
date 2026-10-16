using Yoake.Core.Commands;
using Yoake.Core.Subtitles;

namespace Yoake.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    private sealed record TextPosition(int Caret, int Start, int End);
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<AssEvent, TextPosition> _textPositions = new();
    private AssEvent? _textPositionLine;
    private void RestoreTextPosition()
    {
        if (_textPositionLine is {} previous)
        {
            _textPositions.Remove(previous);
            _textPositions.Add(previous, new(TextCursor, TextSelectionStart, TextSelectionEnd));
        }
        _textPositionLine = SelectedEvent;
        var position = SelectedEvent is {} line && _textPositions.TryGetValue(line, out var saved) ? saved : new TextPosition(0, 0, 0);
        var length = Draft?.Text.Length ?? 0;
        TextCursor = Math.Clamp(position.Caret, 0, length);
        TextSelectionStart = Math.Clamp(position.Start, 0, length);
        TextSelectionEnd = Math.Clamp(position.End, 0, length);
        SearchSelectionApplied?.Invoke(this, EventArgs.Empty);
    }
    private int _textSelectionStart, _textSelectionEnd;
    public int TextSelectionStart {get=>_textSelectionStart;set{if(_textSelectionStart==value)return;_textSelectionStart=value;RefreshFormatting();}}
    public int TextSelectionEnd {get=>_textSelectionEnd;set{if(_textSelectionEnd==value)return;_textSelectionEnd=value;RefreshFormatting();}}
    public AssFormattingState Formatting {get;private set;} = AssFormattingState.Empty;
    private void RefreshFormatting()
    {
        Formatting=Draft is null||ActiveEditor is null?AssFormattingState.Empty:AssFormattingState.Read(Draft.Text,TextSelectionStart,TextSelectionEnd,FormattingStyle,ResolveFormatStyle);
        OnPropertyChanged(nameof(Formatting));
    }
    private Dictionary<string,string> CaretFormatState()=>AssFormatting.State(Draft!.Text,
        AssFormatting.Boundary(Draft.Text,Math.Min(TextSelectionStart,TextSelectionEnd),TextSelectionStart==TextSelectionEnd),FormattingStyle,ResolveFormatStyle);
    public event EventHandler? TextFormattingApplied;
    public double[] StyleSplitWeights=>_settings.StyleSplitWeights??[1,1,3.4];
    public string RecentColorsPath=>Path.Combine(Path.GetDirectoryName(_settingsStore.Path)!,"recent-colors.txt");
    public string StyleLibraryPath=>Path.Combine(Path.GetDirectoryName(_settingsStore.Path)!,"style-library.json");
    private AssStyle FormattingStyle=>ActiveEditor!.Document.Styles.FirstOrDefault(s=>s.Name==(Draft?.Style??SelectedEvent?.Style))??ActiveEditor.Document.Styles.FirstOrDefault()??AssDocument.CreateEmpty().Styles[0];
    private void FormatText(string name,Func<string,int,int,AssStyle,AssTextEdit> operation)
    {
        if(Draft is null||SelectedEvent is null||ActiveEditor is null)return;
        // Older recovery bundles and direct draft callers may still contain
        // physical newlines. Commit normalizes those; retain the same range.
        var raw=Draft.Text;
        int CommittedOffset(int index)=>AssEditorProjection.EncodeNewlines(raw[..Math.Clamp(index,0,raw.Length)]).Length;
        var start=CommittedOffset(TextSelectionStart);var end=CommittedOffset(TextSelectionEnd);
        if(!CommitDraft())return;
        var edit=operation(SelectedEvent.Text,start,end,FormattingStyle);
        ActiveEditor.SetField(SelectedEvent,"Text",edit.Text,name);ReloadDraft();
        TextSelectionStart=edit.SelectionStart;TextSelectionEnd=edit.SelectionEnd;TextCursor=edit.SelectionEnd;
        TextFormattingApplied?.Invoke(this,EventArgs.Empty);
    }
    private AssStyle? ResolveFormatStyle(string name)=>ActiveEditor?.Document.Styles.FirstOrDefault(s=>s.Name==name);
    private void InsertHardNewline()
    {
        if(Draft is null)return;
        var start=Math.Clamp(Math.Min(TextSelectionStart,TextSelectionEnd),0,Draft.Text.Length);
        var end=Math.Clamp(Math.Max(TextSelectionStart,TextSelectionEnd),start,Draft.Text.Length);
        Draft.Text=Draft.Text[..start]+@"\N"+Draft.Text[end..];
        TextSelectionStart=TextSelectionEnd=TextCursor=start+2;
        TextFormattingApplied?.Invoke(this,EventArgs.Empty);
    }
    private void ToggleFormat(string tag)=>FormatText("Format "+tag,(text,start,end,style)=>AssFormatting.Toggle(text,start,end,tag,style,ResolveFormatStyle));
    private async ValueTask FormatFontAsync()
    {
        if(Dialogs is null||Draft is null||ActiveEditor is null)return;
        var state=CaretFormatState();
        var choice=await Dialogs.ChooseFontAsync(state.GetValueOrDefault("fn",""),state.GetValueOrDefault("fs","60"));
        if(choice is not null)FormatText("Change subtitle font",(text,start,end,style)=>AssFormatting.Apply(text,start,end,new Dictionary<string,string>{{"fn",choice.Family},{"fs",choice.Size}},style,ResolveFormatStyle));
    }
    private async ValueTask FormatColorAsync(int channel)
    {
        if(Dialogs is null||Draft is null||ActiveEditor is null)return;
        var state=CaretFormatState();
        AssColor.TryParse(state.GetValueOrDefault(channel+"c"),out var rgb);AssColor.TryParse(state.GetValueOrDefault(channel+"a"),out var alpha);
        var color=await Dialogs.ChooseColorAsync(rgb with{Transparency=alpha.Red});
        if(color is {} chosen)FormatText("Change subtitle color",(text,start,end,style)=>AssFormatting.Apply(text,start,end,new Dictionary<string,string>{{channel+"c",chosen.RgbOverride},{channel+"a",chosen.AlphaOverride}},style,ResolveFormatStyle));
    }
}
