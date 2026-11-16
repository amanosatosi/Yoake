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
    public int TextSelectionStart {get=>_textSelectionStart;set{if(_textSelectionStart==value)return;var selected=_textSelectionStart!=_textSelectionEnd;_textSelectionStart=value;RefreshFormatting();RefreshInlineSelection(selected);}}
    public int TextSelectionEnd {get=>_textSelectionEnd;set{if(_textSelectionEnd==value)return;var selected=_textSelectionStart!=_textSelectionEnd;_textSelectionEnd=value;RefreshFormatting();RefreshInlineSelection(selected);}}
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
        var beforePosition=new TextPosition(CommittedOffset(TextCursor),start,end);
        if(!CommitDraft())return;
        var edit=operation(SelectedEvent.Text,start,end,FormattingStyle);
        var editor=ActiveEditor;var line=SelectedEvent;
        var afterPosition=new TextPosition(edit.SelectionEnd,edit.SelectionStart,edit.SelectionEnd);
        void Position(TextPosition position)
        {
            _textPositions.Remove(line);_textPositions.Add(line,position);
            if(!ReferenceEquals(ActiveEditor,editor)||!ReferenceEquals(SelectedEvent,line))return;
            TextCursor=position.Caret;TextSelectionStart=position.Start;TextSelectionEnd=position.End;
        }
        if(edit.Text!=line.Text)
        {
            using var transaction=editor.Undo.BeginTransaction(name);
            editor.SetField(line,"Text",edit.Text,name);
            editor.Undo.Execute(new Yoake.Core.Undo.DelegateUndoOperation(name,()=>Position(beforePosition),()=>Position(afterPosition)));
            transaction.Commit();
        }
        else Position(afterPosition);
        ReloadDraft();
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
    private void RegisterFormattingDialogCommands()
    {
        foreach (var (id, channel) in new[] { (CommandIds.FormatFont, 0), (CommandIds.FormatPrimary, 1),
            (CommandIds.FormatSecondary, 2), (CommandIds.FormatOutline, 3), (CommandIds.FormatShadow, 4) })
        {
            var colorChannel = channel;
            var label = channel == 0 ? "Font family and size…" : "Choose subtitle color…";
            _registry.Register(new(new(id,label,label,"Edit"),
                (_,token) => colorChannel == 0 ? FormatFontAsync(token) : FormatColorAsync(colorChannel,token),
                context => !_disposed && _activeId is {} owner && (context.DocumentId is null || context.DocumentId == owner)
                    && HasSelectedEvent && !HasGesture && Dialogs is not null));
            Actions[id] = new Yoake.UI.Commands.RegistryCommand(_registry,id,CurrentContext,hotkeys:()=>HotkeyProfile);
        }
    }
    // A modal result belongs to the exact editor, draft and text range that opened
    // it. Neither tab activation nor edits/undo while it is pending may redirect it.
    private Func<bool>? CaptureFormattingTarget()
    {
        if (_activeId is not {} owner || !_documents.TryGetValue(owner,out var state) ||
            Draft is not {} draft || SelectedEvent is not {} line || ActiveEditor is not {} editor) return null;
        var revision = editor.Document.Revision; var draftRevision = state.DraftRevision;
        var start = TextSelectionStart; var end = TextSelectionEnd; var caret = TextCursor;
        return () => !_disposed && _activeId == owner && _documents.TryGetValue(owner,out var live) && ReferenceEquals(live,state)
            && ReferenceEquals(ActiveEditor,editor) && ReferenceEquals(SelectedEvent,line) && ReferenceEquals(Draft,draft)
            && editor.Document.Revision == revision && state.DraftRevision == draftRevision && !HasGesture
            && TextSelectionStart == start && TextSelectionEnd == end && TextCursor == caret;
    }
    private async ValueTask FormatFontAsync(CancellationToken token)
    {
        if(Dialogs is null || CaptureFormattingTarget() is not {} current)return;
        var state=CaretFormatState();
        var choice=await Dialogs.ChooseFontAsync(state.GetValueOrDefault("fn",""),state.GetValueOrDefault("fs","60"),token);
        token.ThrowIfCancellationRequested();
        if(choice is not null && current())FormatText("Change subtitle font",(text,start,end,style)=>AssFormatting.Apply(text,start,end,new Dictionary<string,string>{{"fn",choice.Family},{"fs",choice.Size}},style,ResolveFormatStyle));
    }
    private async ValueTask FormatColorAsync(int channel,CancellationToken token)
    {
        if(Dialogs is null || CaptureFormattingTarget() is not {} current)return;
        var state=CaretFormatState();
        AssColor.TryParse(state.GetValueOrDefault(channel+"c"),out var rgb);AssColor.TryParse(state.GetValueOrDefault(channel+"a"),out var alpha);
        var color=await Dialogs.ChooseColorAsync(rgb with{Transparency=alpha.Red},token);
        token.ThrowIfCancellationRequested();
        if(color is {} chosen && current())FormatText("Change subtitle color",(text,start,end,style)=>AssFormatting.Apply(text,start,end,new Dictionary<string,string>{{channel+"c",chosen.RgbOverride},{channel+"a",chosen.AlphaOverride}},style,ResolveFormatStyle));
    }
}
