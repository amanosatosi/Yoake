using System.ComponentModel;
using System.Runtime.CompilerServices;
using Yoake.Core.Subtitles;

namespace Yoake.UI.ViewModels;

public sealed class KanjiTimingSession : INotifyPropertyChanged,IDisposable
{
    internal MainWindowViewModel Model { get; }
    internal SubtitleEditor Editor { get; }
    public Guid DocumentId { get; }
    private long _revision;
    private AssEvent[] _sources=[],_targets=[];
    private readonly Dictionary<AssEvent,string> _accepted=[];
    private readonly Stack<(int Source,int Target,AssEvent? Accepted)> _history=[];
    private int _source,_target;
    private bool _disposed;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? Invalidated;
    public string[] Styles=>Editor.Document.Styles.Select(s=>s.Name).ToArray();
    public KaraokeAlignment? Alignment { get; private set; }
    public KaraokeMatchSuggestion? Suggestion { get; private set; }
    public string SourceLine=>_source<_sources.Length?_sources[_source].Text:"No more source lines";
    public string DestinationLine=>_target<_targets.Length?_targets[_target].Text:"No more destination lines";
    public string[] SourceSyllables=>Alignment?.RemainingSource.Select(s=>$"{s.Duration/10} cs · {(s.Text.Length==0?"(empty)":s.Text)}").ToArray()??[];
    public string[] DestinationCharacters=>Alignment?.RemainingDestination??[];
    public string[] LinkedGroups=>Alignment?.Groups.Select(g=>$"{g.SourceText} → {g.DestinationText}  ({g.Duration/10} cs)").ToArray()??[];
    public string Status { get; private set; }="Choose different source and destination styles, then Start.";
    public int AcceptedCount=>_accepted.Count;
    public bool IsCurrent=>!_disposed&&Model.ActiveDocumentId==DocumentId&&ReferenceEquals(Model.ActiveEditor,Editor)&&Editor.Document.Revision==_revision&&Model.Draft?.IsChanged!=true&&!Model.HasGesture;
    public KanjiTimingSession(MainWindowViewModel model)
    {
        if(model.ActiveEditor is null||model.ActiveDocumentId is not {} id)throw new InvalidOperationException("Open a subtitle first.");
        Model=model;Editor=model.ActiveEditor;DocumentId=id;_revision=Editor.Document.Revision;Model.PropertyChanged+=ModelChanged;
    }
    private void ModelChanged(object? sender,PropertyChangedEventArgs args)
    {
        if(Model.ActiveDocumentId!=DocumentId||!ReferenceEquals(Model.ActiveEditor,Editor)){Dispose();Invalidated?.Invoke(this,EventArgs.Empty);}
        else if(args.PropertyName==nameof(MainWindowViewModel.PreviewRevision)&&!IsCurrent){Status="The editor changed. Restart to discard staged matches and reload the lines.";Changed(nameof(Status));}
    }
    internal void Restart(string sourceStyle,string destinationStyle)
    {
        if(_disposed||Model.ActiveDocumentId!=DocumentId||Model.Draft?.IsChanged==true||Model.HasGesture)throw new InvalidOperationException("Finish the main editor draft before starting.");
        if(sourceStyle==destinationStyle||!Styles.Contains(sourceStyle)||!Styles.Contains(destinationStyle))throw new ArgumentException("Choose two different existing styles.");
        _revision=Editor.Document.Revision;
        bool Eligible(AssEvent line)=>line.Text.Length>0&&!line.IsComment&&!MangetsuMetadata.IsInherited(line);
        _sources=Editor.Document.Events.Where(e=>Eligible(e)&&e.Style==sourceStyle).ToArray();_targets=Editor.Document.Events.Where(e=>Eligible(e)&&e.Style==destinationStyle).ToArray();
        _accepted.Clear();_history.Clear();_source=_target=0;LoadPair();
    }
    internal void Suggest()
    {
        EnsureCurrent();Suggestion=(Alignment??throw new InvalidOperationException("Start a line pair first.")).Suggest();Changed(nameof(Suggestion));
    }
    internal void Link(int sourceCount,int destinationCount)
    { EnsureCurrent();(Alignment??throw new InvalidOperationException("Start a line pair first.")).Link(sourceCount,destinationCount);Refresh("Group linked. Enter links the next group; accept the line when complete."); }
    internal void Unlink(){EnsureCurrent();Alignment?.Unlink();Refresh("Last group unlinked.");}
    internal void Accept()
    {
        EnsureCurrent();if(Alignment is null||_target>=_targets.Length)throw new InvalidOperationException("No active line pair.");
        var output=Alignment.Render();var target=_targets[_target];
        _history.Push((_source,_target,target));_accepted[target]=output;_source++;_target++;LoadPair();
    }
    internal void Skip(bool source)
    {
        EnsureCurrent();if(source?_source>=_sources.Length:_target>=_targets.Length)return;
        _history.Push((_source,_target,null));if(source)_source++;else _target++;LoadPair();
    }
    internal void Previous()
    {
        EnsureCurrent();if(!_history.TryPop(out var previous))return;
        if(previous.Accepted is {} target)_accepted.Remove(target);_source=previous.Source;_target=previous.Target;LoadPair();
    }
    internal void Apply()
    {
        EnsureCurrent();if(_accepted.Count==0)throw new InvalidOperationException("Accept at least one complete line before Apply.");
        using(var transaction=Editor.Undo.BeginTransaction("Kanji Timing Copier"))
        {
            foreach(var (line,text) in _accepted)Editor.SetField(line,"Text",text,"Kanji Timing Copier");
            transaction.Commit();
        }
        _revision=Editor.Document.Revision;_accepted.Clear();_history.Clear();Model.RefreshKanjiDraft();Refresh("Accepted lines applied in one undo step. Current unfinished groups were kept.");
    }
    private void LoadPair()
    {
        Alignment=null;var status=$"Source {_source+1}/{_sources.Length} · Destination {_target+1}/{_targets.Length} · {_accepted.Count} accepted, unapplied";
        if(_source<_sources.Length&&_target<_targets.Length)
        {
            try
            {
                var source=_sources[_source];
                if(source.StartMilliseconds is not {} start||source.EndMilliseconds is not {} end)throw new ArgumentException("Correct the source line start/end times before matching.");
                Alignment=new(SourceLine,DestinationLine,checked(end-start));
            }
            catch(Exception error) when(error is ArgumentException or NotSupportedException){status=error.Message+" Use Skip or choose another pair; no source was changed.";}
        }
        else status=$"End of matching lines · {_accepted.Count} accepted, unapplied. Apply saves accepted lines; Cancel discards them.";
        Refresh(status);
    }
    private void EnsureCurrent(){if(!IsCurrent)throw new InvalidOperationException("This matching session is stale. Restart before applying changes.");}
    private void Refresh(string status)
    {
        Status=status;foreach(var name in new[] { nameof(Alignment),nameof(SourceLine),nameof(DestinationLine),nameof(SourceSyllables),nameof(DestinationCharacters),nameof(LinkedGroups),nameof(AcceptedCount),nameof(Status) })Changed(name);
    }
    private void Changed([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
    public void Dispose(){if(_disposed)return;_disposed=true;Model.PropertyChanged-=ModelChanged;}
}
