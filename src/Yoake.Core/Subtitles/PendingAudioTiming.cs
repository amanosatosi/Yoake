using Yoake.Core.Audio;

namespace Yoake.Core.Subtitles;

// Transient markers owned by one existing editor. They are not a second
// subtitle document, an edit draft or a playback clock.
public sealed class PendingAudioTiming(SubtitleEditor editor)
{
    private sealed record Entry(string Start,string End,AudioClipInterval Range);
    private readonly Dictionary<AssEvent,Entry> _pending=[];
    private HashSet<AssEvent> _owned=[];
    private long _ownershipRevision=-1;
    private long _generation;
    public int Count {get{Reconcile();return _pending.Count;}}
    private void Require(AssEvent line)
    {
        Reconcile();if(!_owned.Contains(line))throw new InvalidOperationException("Timing belongs to another subtitle document.");
    }
    public void Reconcile()
    {
        if(_ownershipRevision==editor.Document.Revision)return;
        _ownershipRevision=editor.Document.Revision;_owned=editor.Document.Events.ToHashSet();
        foreach(var pair in _pending.ToArray())
            if(!_owned.Contains(pair.Key)||pair.Key.Start!=pair.Value.Start||pair.Key.End!=pair.Value.End)
            {_pending.Remove(pair.Key);_generation++;}
    }
    public AudioClipInterval? Get(AssEvent line)
    {Reconcile();return _pending.TryGetValue(line,out var entry)?entry.Range:null;}
    public void Stage(AssEvent line,AudioClipInterval range)
    {
        Require(line);Validate(range);
        if(line.StartMilliseconds is not {} start||line.EndMilliseconds is not {} end||end<start)
            throw new ArgumentException("Correct the stored subtitle timing before adjusting audio markers.");
        if(range.StartMilliseconds==start&&range.EndMilliseconds==end)_pending.Remove(line);
        else _pending[line]=new(line.Start,line.End,range);
        _generation++;
    }
    public void Clear(AssEvent line){Require(line);if(_pending.Remove(line))_generation++;}
    public static AudioClipInterval Adjust(AudioClipInterval range,long startDelta,long endDelta)
    {
        Validate(range);
        var start=Math.Clamp(checked(range.StartMilliseconds+startDelta),0,range.EndMilliseconds);
        var end=Math.Max(start,checked(range.EndMilliseconds+endDelta));
        var result=new AudioClipInterval(start,end);Validate(result);return result;
    }
    public static void Validate(AudioClipInterval range)
    {
        if(range.StartMilliseconds<0||range.EndMilliseconds<range.StartMilliseconds||
            !AssTime.TryParse(AssTime.Format(range.EndMilliseconds),out _))
            throw new ArgumentException("Audio timing must have a non-negative start and an end at or after it.");
    }
    public sealed class Checkpoint
    {
        internal AssEvent Line {get;}
        internal string Start {get;}
        internal string End {get;}
        internal AudioClipInterval? Range {get;}
        internal PendingAudioTiming Owner {get;}
        internal Checkpoint(PendingAudioTiming owner,AssEvent line,AudioClipInterval? range)
        {Owner=owner;Line=line;Start=line.Start;End=line.End;Range=range;}
    }
    public Checkpoint Capture(AssEvent line){Require(line);return new(this,line,Get(line));}
    public void Restore(Checkpoint checkpoint)
    {
        if(!ReferenceEquals(checkpoint.Owner,this))throw new InvalidOperationException("Foreign audio timing checkpoint.");
        Reconcile();
        // A canceled gesture cannot restore obsolete markers after a source edit.
        if(!_owned.Contains(checkpoint.Line)||checkpoint.Line.Start!=checkpoint.Start||checkpoint.Line.End!=checkpoint.End)return;
        if(checkpoint.Range is {} range)Stage(checkpoint.Line,range);else Clear(checkpoint.Line);
    }
    public sealed class CommitPlan
    {
        internal PendingAudioTiming Owner {get;}
        internal long Generation {get;}
        internal SubtitleTimingPlan Timing {get;}
        internal CommitPlan(PendingAudioTiming owner,long generation,SubtitleTimingPlan timing)
        {Owner=owner;Generation=generation;Timing=timing;}
    }
    public CommitPlan CaptureCommit(CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();Reconcile();
        List<SubtitleTimingPlan.Change> changes=[];
        foreach(var (line,entry) in _pending)
        {
            token.ThrowIfCancellationRequested();Validate(entry.Range);
            changes.Add(new(line,entry.Range.StartMilliseconds==line.StartMilliseconds?line.Start:AssTime.Format(entry.Range.StartMilliseconds),
                entry.Range.EndMilliseconds==line.EndMilliseconds?line.End:AssTime.Format(entry.Range.EndMilliseconds)));
        }
        var snapshot=new SubtitleTimingSnapshot(editor,[],null,null);
        return new(this,_generation,new(snapshot,"Commit audio timing",changes.ToArray()));
    }
    public int Commit(CommitPlan plan,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        if(!ReferenceEquals(plan.Owner,this)||plan.Generation!=_generation)
            throw new InvalidOperationException("Audio markers changed before committing. Try again.");
        var count=editor.ApplyTiming(plan.Timing);
        _pending.Clear();_generation++;return count;
    }
}
