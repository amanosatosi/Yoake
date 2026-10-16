using System.Globalization;
using System.Text;
using Yoake.Core.Undo;

namespace Yoake.Core.Subtitles;

public enum SubtitleSortKey { Start,End,Style,Actor,Effect,Layer }

// A revision-checked snapshot of one editor, not another document or timeline.
// Capture on the caller thread, calculate in a cancellable job, apply on UI.
public sealed class SubtitleSortSnapshot
{
    internal sealed record Entry(AssEvent Line,int Position,bool Selected,long Start,long End,int Layer,string Style,string Actor,string Effect);
    internal SubtitleEditor Editor {get;}
    internal long Revision {get;}
    private readonly Entry[] _entries;private readonly SubtitleSortKey _key;
    internal SubtitleSortSnapshot(SubtitleEditor editor,Entry[] entries,SubtitleSortKey key){Editor=editor;Revision=editor.Document.Revision;_entries=entries;_key=key;}

    public SubtitleSortOrder Calculate(CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();var sorted=(Entry[])_entries.Clone();
        int Compare(Entry first,Entry second)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var comparison=_key switch
            {
                SubtitleSortKey.Start=>first.Start.CompareTo(second.Start),SubtitleSortKey.End=>first.End.CompareTo(second.End),
                SubtitleSortKey.Layer=>first.Layer.CompareTo(second.Layer),SubtitleSortKey.Style=>CompareScalars(first.Style,second.Style),
                SubtitleSortKey.Actor=>CompareScalars(first.Actor,second.Actor),SubtitleSortKey.Effect=>CompareScalars(first.Effect,second.Effect),
                _=>throw new ArgumentException("Unknown sort key."),
            };
            return comparison!=0?comparison:first.Position.CompareTo(second.Position);
        }
        for(var start=0;start<sorted.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();if(!sorted[start].Selected){start++;continue;}
            var end=start+1;while(end<sorted.Length&&sorted[end].Selected)end++;
            // Upstream selected sorting works on contiguous blocks separately.
            try{Array.Sort(sorted,start,end-start,Comparer<Entry>.Create(Compare));}
            catch(InvalidOperationException exception)when(exception.InnerException is OperationCanceledException&&cancellationToken.IsCancellationRequested)
            {throw new OperationCanceledException(cancellationToken);}
            start=end;
        }
        cancellationToken.ThrowIfCancellationRequested();return new(this,sorted.Select(entry=>entry.Line).ToArray());
    }
    private static int CompareScalars(string first,string second)
    {
        var left=first.EnumerateRunes();var right=second.EnumerateRunes();
        while(true)
        {
            var hasLeft=left.MoveNext();var hasRight=right.MoveNext();
            if(!hasLeft||!hasRight)return hasLeft.CompareTo(hasRight);
            var comparison=left.Current.Value.CompareTo(right.Current.Value);if(comparison!=0)return comparison;
        }
    }
}

public sealed class SubtitleSortOrder
{
    internal SubtitleSortSnapshot Snapshot {get;}
    internal AssEvent[] Order {get;}
    public IReadOnlyList<AssEvent> Lines=>Array.AsReadOnly(Order);
    internal SubtitleSortOrder(SubtitleSortSnapshot snapshot,AssEvent[] order){Snapshot=snapshot;Order=order;}
}

public sealed partial class SubtitleEditor
{
    public SubtitleSortSnapshot CaptureSort(SubtitleSortKey key,IEnumerable<AssEvent>? selection=null)
    {
        if(!Enum.IsDefined(key))throw new ArgumentException("Unknown sort key.");
        var selected=selection is null?null:Ordered(selection).ToHashSet();
        return new(this,Document.Events.Select((line,index)=>new SubtitleSortSnapshot.Entry(line,index,selected is null||selected.Contains(line),
            line.StartMilliseconds??long.MaxValue,line.EndMilliseconds??long.MaxValue,
            int.TryParse(line.Layer,NumberStyles.Integer,CultureInfo.InvariantCulture,out var layer)?layer:int.MaxValue,line.Style,line.Actor,line.Effect)).ToArray(),key);
    }
    public void ApplySort(SubtitleSortOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        if(!ReferenceEquals(order.Snapshot.Editor,this)||order.Snapshot.Revision!=Document.Revision)
            throw new InvalidOperationException("Subtitles changed during sorting. Sort again.");
        if(Document.Events.SequenceEqual(order.Order))return;
        var before=Document.Source.ToArray();var index=0;
        var after=Document.Source.Select(slot=>slot.Record is AssEvent?slot with{Record=order.Order[index++]}:slot).ToArray();
        Undo.Execute(new DelegateUndoOperation("Sort subtitles",()=>Document.Restore(before,resetCollections:true),()=>
        {
            try{Document.Restore(after,resetCollections:true);}
            catch{Document.Restore(before,resetCollections:true);throw;}
        }));
    }
    public void SwapLines(IEnumerable<AssEvent> selection)
    {
        var lines=Ordered(selection);if(lines.Length!=2)throw new ArgumentException("Select exactly two lines to swap.");
        Structure("Swap subtitles",()=>Document.Swap(lines[0],lines[1]));
    }
}
