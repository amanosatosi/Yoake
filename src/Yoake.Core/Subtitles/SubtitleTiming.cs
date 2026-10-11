using Yoake.Core.Media;
using Yoake.Core.Undo;

namespace Yoake.Core.Subtitles;

public enum SubtitleTimingFields { Both, Start, End }
public enum SubtitleTimingScope { All, Selected, SelectionOnward }
public enum SubtitleShiftUnit { Milliseconds, Frames }
public sealed record SubtitleShiftRequest(long Amount,SubtitleShiftUnit Unit=SubtitleShiftUnit.Milliseconds,
    SubtitleTimingFields Fields=SubtitleTimingFields.Both,SubtitleTimingScope Scope=SubtitleTimingScope.Selected);

// A snapshot of the existing editor's fields. No second document or clock.
public sealed class SubtitleTimingSnapshot
{
    internal sealed record Row(AssEvent Line,string Start,string End,bool Selected);
    internal SubtitleEditor Editor {get;}
    internal long Revision {get;}
    private readonly Row[] _rows;
    internal SubtitleTimingSnapshot(SubtitleEditor editor,Row[] rows){Editor=editor;Revision=editor.Document.Revision;_rows=rows;}
    private static (long Start,long End) Read(Row row)
    {
        if(!AssTime.TryParse(row.Start,out var start)||!AssTime.TryParse(row.End,out var end)||end<start)
            throw new ArgumentException("A subtitle has invalid timing. Correct its start and end first.");
        return(start,end);
    }
    private static long Round(long value)
    {
        value=Math.Max(0,value);value=checked((value+5)/10*10);
        if(!AssTime.TryParse(AssTime.Format(value),out _))throw new ArgumentException("The resulting time is outside the supported range.");
        return value;
    }
    private SubtitleTimingPlan Plan(string label,IEnumerable<(Row Row,long Start,long End)> changes,CancellationToken token)
    {
        List<SubtitleTimingPlan.Change> edits=[];
        foreach(var (row,start,end) in changes)
        {
            token.ThrowIfCancellationRequested();var old=Read(row);
            var s=start==old.Start?start:Round(start);var e=end==old.End?end:Round(end);
            if(e<s)throw new ArgumentException("This operation would place an end before its start. Nothing was changed.");
            if(old.Start!=s||old.End!=e)edits.Add(new(row.Line,s==old.Start?row.Start:AssTime.Format(s),e==old.End?row.End:AssTime.Format(e)));
        }
        token.ThrowIfCancellationRequested();return new(this,label,edits.ToArray());
    }
    public SubtitleTimingPlan Shift(SubtitleShiftRequest request,FrameTiming? timing=null,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(request);token.ThrowIfCancellationRequested();
        if(!Enum.IsDefined(request.Unit)||!Enum.IsDefined(request.Fields)||!Enum.IsDefined(request.Scope))throw new ArgumentException("Unknown shift option.");
        if(request.Unit==SubtitleShiftUnit.Frames&&(timing is null||request.Amount is <int.MinValue or >int.MaxValue))
            throw new ArgumentException("Frame shifting requires a loaded frame map and an integer frame count.");
        if(request.Scope!=SubtitleTimingScope.All&&!_rows.Any(row=>row.Selected))throw new ArgumentException("Select subtitle rows to shift.");
        if(request.Amount==0)return Plan("Shift subtitle times",[],token);
        var firstSelected=Array.FindIndex(_rows,row=>row.Selected);
        long ShiftTime(long value,FrameTimeKind kind)=>request.Unit==SubtitleShiftUnit.Milliseconds?checked(value+request.Amount)
            :timing!.MillisecondsAtFrame(checked(timing.FrameAtMilliseconds(value,kind)+(int)request.Amount),kind);
        IEnumerable<(Row,long,long)> Changes()
        {
            for(var index=0;index<_rows.Length;index++)
            {
                token.ThrowIfCancellationRequested();var row=_rows[index];
                if(request.Scope==SubtitleTimingScope.Selected&&!row.Selected||request.Scope==SubtitleTimingScope.SelectionOnward&&index<firstSelected)continue;
                var (start,end)=Read(row);
                yield return(row,request.Fields==SubtitleTimingFields.End?start:ShiftTime(start,FrameTimeKind.Start),
                    request.Fields==SubtitleTimingFields.Start?end:ShiftTime(end,FrameTimeKind.End));
            }
        }
        return Plan("Shift subtitle times",Changes(),token);
    }
    public bool CanAdjoin=>_rows.Any(row=>row.Selected)&&_rows.SkipWhile(row=>!row.Selected).TakeWhile(row=>row.Selected).Count()==_rows.Count(row=>row.Selected);
    public SubtitleTimingPlan Adjoin(bool setStart,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();if(!CanAdjoin)throw new ArgumentException("Select one line or one continuous block to make timing continuous.");
        var single=_rows.Count(row=>row.Selected)==1;
        IEnumerable<(Row,long,long)> Changes()
        {
            for(var index=1;index<_rows.Length;index++)
            {
                token.ThrowIfCancellationRequested();var previous=_rows[index-1];var current=_rows[index];
                if(setStart&&current.Selected&&(single||previous.Selected))yield return(current,Read(previous).End,Read(current).End);
                else if(!setStart&&previous.Selected&&(single||current.Selected))yield return(previous,Read(previous).Start,Read(current).Start);
            }
        }
        return Plan(setStart?"Make start timing continuous":"Make end timing continuous",Changes(),token);
    }
    public SubtitleTimingPlan Snap(FrameTiming timing,int frame,bool setStart,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(timing);token.ThrowIfCancellationRequested();
        var start=timing.SubtitleBoundary(frame,FrameTimeKind.Start);var end=timing.SubtitleBoundary(frame,FrameTimeKind.End);
        return Plan(setStart?"Snap starts to video":"Snap ends to video",_rows.Where(row=>row.Selected).Select(row=>
        {var old=Read(row);return(row,setStart?start:Math.Min(old.Start,start),setStart?Math.Max(old.End,end):end);}),token);
    }
    public SubtitleTimingPlan ShiftToFrame(AssEvent active,FrameTiming timing,int frame,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(timing);token.ThrowIfCancellationRequested();
        var row=_rows.SingleOrDefault(row=>ReferenceEquals(row.Line,active)&&row.Selected)??throw new ArgumentException("The active line must be selected.");
        var amount=checked(Math.Max(0,timing.MillisecondsAtFrame(frame,FrameTimeKind.Start))-Read(row).Start);
        return Shift(new(amount),token:token);
    }
    public SubtitleTimingPlan SnapScene(FrameTiming timing,int frame,IReadOnlyList<int> keyframes,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(timing);ArgumentNullException.ThrowIfNull(keyframes);token.ThrowIfCancellationRequested();
        if(keyframes.Count==0)throw new ArgumentException("No keyframes are available.");
        var previous=0;var next=timing.Count;
        for(var index=0;index<keyframes.Count;index++)
        {
            token.ThrowIfCancellationRequested();var key=keyframes[index];
            if(key<0||key>=timing.Count||index>0&&key<=keyframes[index-1])throw new ArgumentException("Keyframes must be ordered, unique and inside the video.");
            if(key<=frame)previous=key;else if(next==timing.Count)next=key;
        }
        var start=timing.SubtitleBoundary(previous,FrameTimeKind.Start);var end=timing.SubtitleBoundary(next-1,FrameTimeKind.End);
        return Plan("Snap subtitles to scene",_rows.Where(row=>row.Selected).Select(row=>(row,start,end)),token);
    }
}

public sealed class SubtitleTimingPlan
{
    internal sealed record Change(AssEvent Line,string Start,string End);
    internal SubtitleTimingSnapshot Snapshot {get;}
    internal Change[] Changes {get;}
    public string Label {get;}
    public int Count=>Changes.Length;
    internal SubtitleTimingPlan(SubtitleTimingSnapshot snapshot,string label,Change[] changes){Snapshot=snapshot;Label=label;Changes=changes;}
}

public sealed partial class SubtitleEditor
{
    public SubtitleTimingSnapshot CaptureTiming(IEnumerable<AssEvent> selection)
    {
        var selected=Ordered(selection).ToHashSet();
        return new(this,Document.Events.Select(line=>new SubtitleTimingSnapshot.Row(line,line.Start,line.End,selected.Contains(line))).ToArray());
    }
    public int ApplyTiming(SubtitleTimingPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if(!ReferenceEquals(plan.Snapshot.Editor,this)||plan.Snapshot.Revision!=Document.Revision)
            throw new InvalidOperationException("Subtitles changed during timing calculation. Try again.");
        if(plan.Count==0)return 0;
        // Validate every field before publishing any mutation. Ownership was
        // checked by the snapshot/revision, avoiding a source scan per field.
        var fields=plan.Changes.Select(change=>
        {
            var before=(string[])change.Line.Fields.Clone();var probe=change.Line.Clone();
            probe.Set("Start",change.Start);probe.Set("End",change.End);
            return(Line:change.Line,Before:before,After:(string[])probe.Fields.Clone());
        }).ToArray();
        void Restore(bool after)
        {
            using var update=Document.BeginUpdate();
            try{foreach(var entry in fields)entry.Line.RestoreFields(after?entry.After:entry.Before);}
            catch{foreach(var entry in fields)entry.Line.RestoreFields(after?entry.Before:entry.After);throw;}
        }
        Undo.Execute(new DelegateUndoOperation(plan.Label,()=>Restore(false),()=>Restore(true)));return plan.Count;
    }
}
