using System.ComponentModel;
using Yoake.Core.Subtitles;

namespace Yoake.UI.ViewModels;

public enum EditorAssistantMode { Translation, Styling }

// A modeless tool retains its own owner and input. No document can receive an
// edit from a window opened for a different tab or an obsolete source span.
public sealed class EditorAssistantSession : INotifyPropertyChanged, IDisposable
{
    internal MainWindowViewModel Model { get; }
    internal Guid DocumentId { get; }
    private readonly SubtitleEditor _editor;
    private AssEvent? _line;
    private string _source="",_input="";
    private SubtitleTextSpan[] _blocks=[];
    private int _block;
    private bool _publishing,_disposed;
    public EditorAssistantMode Mode { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? Invalidated;
    public int InputSelectionStart { get; set; }
    public int InputSelectionEnd { get; set; }
    public string Input { get=>_input;set { if(_input==value)return;_input=value;Changed(nameof(Input)); } }
    public string Original => Mode==EditorAssistantMode.Styling?_source:_blocks.Length==0?"":_source.Substring(_blocks[_block].Start,_blocks[_block].Length);
    public string ContextText => _source;
    public IReadOnlyList<string> Styles => Model.StyleNames;
    public string Position => $"Line {(_line is null?0:_editor.Document.Events.IndexOf(_line)+1)} / {_editor.Document.Events.Count}"+(Mode==EditorAssistantMode.Translation?$" · block {(_blocks.Length==0?0:_block+1)} / {_blocks.Length}":"");
    public bool SeekVideo { get; set; }=true;
    public string Status { get; private set; }="";
    public bool IsCurrent => !_disposed&&Model.ActiveDocumentId==DocumentId&&ReferenceEquals(Model.ActiveEditor,_editor)&&ReferenceEquals(Model.SelectedEvent,_line)
        &&_line is not null&&_editor.Document.Events.Contains(_line)&&_line.Text==_source&&!Model.HasGesture&&Model.Draft?.IsChanged!=true;
    internal EditorAssistantSession(MainWindowViewModel model,EditorAssistantMode mode)
    {
        Model=model;Mode=mode;DocumentId=model.ActiveDocumentId??throw new InvalidOperationException("Open subtitles first.");
        _editor=model.ActiveEditor??throw new InvalidOperationException("Open subtitles first.");
        model.PropertyChanged+=ModelChanged;Reload();
    }
    private void ModelChanged(object? sender,PropertyChangedEventArgs args)
    {
        if(_disposed||_publishing)return;
        if(Model.ActiveDocumentId!=DocumentId||!ReferenceEquals(Model.ActiveEditor,_editor)) { Dispose();Invalidated?.Invoke(this,EventArgs.Empty);return; }
        if(args.PropertyName==nameof(MainWindowViewModel.SelectedEvent)&&!ReferenceEquals(_line,Model.SelectedEvent))Reload();
        else if(args.PropertyName==nameof(MainWindowViewModel.PreviewRevision)&&!IsCurrent)Message("The main editor changed. Reload this block before committing.");
        else if(args.PropertyName==nameof(MainWindowViewModel.StyleNames))Changed(nameof(Styles));
    }
    public void Reload()
    {
        _line=Model.SelectedEvent;_source=_line?.Text??"";_blocks=SubtitleTranslationText.Blocks(_source);_block=0;
        Input=Mode==EditorAssistantMode.Styling?_line?.Style??"":"";
        Message(Mode==EditorAssistantMode.Translation&&_blocks.Length==0?"This line has no translatable text. Use Next.":"");Refresh();
    }
    internal void Commit(bool advance)
    {
        EnsureCurrent();
        var nextBlock=-1;
        _publishing=true;
        try
        {
            if(Mode==EditorAssistantMode.Styling)
            {
                if(!_editor.Document.Styles.Any(style=>style.Name==Input))throw new ArgumentException("Choose an existing style.");
                Model.PublishAssistantField(_line!,"Style",Input,"Styling Assistant");
            }
            else
            {
                if(_blocks.Length==0)throw new ArgumentException("This line has no translatable text.");
                var text=SubtitleTranslationText.Replace(_source,_blocks[_block],Input);
                var start=_blocks[_block].Start;
                var replacementEnd=start+text.Length-_source.Length+_blocks[_block].Length;
                Model.PublishAssistantField(_line!,"Text",text,"Translation Assistant");
                _source=text;_blocks=SubtitleTranslationText.Blocks(text);
                // Keep a logical cursor even when a block is deleted or new
                // override tags are inserted by the translator.
                _block=Array.FindIndex(_blocks,b=>b.Start>=start);
                if(_block<0)_block=Math.Max(0,_blocks.Length-1);
                nextBlock=Array.FindIndex(_blocks,b=>b.Start>=replacementEnd);
                Input="";
            }
            Message("Committed.");Refresh();
        }
        finally { _publishing=false; }
        if(advance&&Mode==EditorAssistantMode.Translation&&nextBlock>=0)
        { _block=nextBlock;Input="";Message("");Refresh(); }
        else if(advance) { if(Mode==EditorAssistantMode.Translation)_block=Math.Max(0,_blocks.Length-1);Move(1); }
    }
    internal void Move(int direction)
    {
        EnsureCurrent();
        if(Mode==EditorAssistantMode.Translation&&_block+direction>=0&&_block+direction<_blocks.Length)
        { _block+=direction;Input="";Message("");Refresh();return; }
        var row=_editor.Document.Events.IndexOf(_line!);
        for(var target=row+direction;target>=0&&target<_editor.Document.Events.Count;target+=direction)
        {
            var line=_editor.Document.Events[target];
            if(Mode==EditorAssistantMode.Translation&&SubtitleTranslationText.Blocks(line.Text).Length==0)continue;
            _publishing=true;
            try { Model.SelectAssistantLine(line);Reload();if(direction<0&&Mode==EditorAssistantMode.Translation)_block=Math.Max(0,_blocks.Length-1); }
            finally { _publishing=false; }
            if(SeekVideo&&line.StartMilliseconds is {} time)Model.CurrentTimeSeconds=time/1000d;
            Refresh();return;
        }
        Message(direction>0?"No more lines.":"At the first line.");
    }
    internal void InsertOriginal()
    {
        EnsureCurrent();
        var boundaries=System.Globalization.StringInfo.ParseCombiningCharacters(Input).Append(Input.Length).ToArray();
        var first=Math.Clamp(Math.Min(InputSelectionStart,InputSelectionEnd),0,Input.Length);
        var last=Math.Clamp(Math.Max(InputSelectionStart,InputSelectionEnd),first,Input.Length);
        var start=boundaries.Last(b=>b<=first);var end=boundaries.First(b=>b>=last);
        var original=Original;Input=Input[..start]+original+Input[end..];
        InputSelectionStart=InputSelectionEnd=start+original.Length;
    }
    private void EnsureCurrent() { if(!IsCurrent)throw new InvalidOperationException("The document or main editor changed. Reload before continuing."); }
    internal void Message(string text) { Status=text;Changed(nameof(Status)); }
    private void Refresh() { foreach(var name in new[] { nameof(Original),nameof(ContextText),nameof(Position),nameof(Styles),nameof(IsCurrent) })Changed(name); }
    private void Changed(string name)=>PropertyChanged?.Invoke(this,new(name));
    public void Dispose() { if(_disposed)return;_disposed=true;Model.PropertyChanged-=ModelChanged; }
}
