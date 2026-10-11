using System.Globalization;
using Yoake.Core.Media;

namespace Yoake.Core.Subtitles;

public enum SubtitleJoinMode { Concatenate, KeepFirst, Karaoke }
public enum CursorSplitTiming { Preserve, Estimate, VideoFrame }

public sealed partial class SubtitleEditor
{
    public void ClearText(AssEvent line,bool preserveTags)
    {
        Require(line);SetField(line,"Text",preserveTags?AssTextOperations.ClearVisibleText(line.Text):"",preserveTags?"Clear visible text":"Clear line text");
    }
    public AssEvent SplitAtCursor(AssEvent line,int cursor,CursorSplitTiming mode,FrameTiming? timing=null,int frame=0,int? videoFrameCount=null)
    {
        Require(line);if(!Enum.IsDefined(mode))throw new ArgumentException("Unknown split timing.",nameof(mode));
        var text=line.Text;cursor=AssText.SafeSplitIndex(text,cursor);
        if(cursor<=0||cursor>=text.Length)throw new ArgumentException("Place the cursor between characters outside override blocks.");
        if(line.StartMilliseconds is not {} start||line.EndMilliseconds is not {} end||end<start)throw new ArgumentException("Invalid subtitle timing.");
        var second=line.Clone();second.Text=AssTextOperations.OverridesBefore(text,cursor)+text[cursor..];
        var split=start;
        if(mode==CursorSplitTiming.Estimate)
        {
            var left=AssTextOperations.VisibleGraphemeCount(text[..cursor]);var right=AssTextOperations.VisibleGraphemeCount(text[cursor..]);
            if(left+right>0)split=checked(start+(long)((Int128)(end-start)*left/(left+right)));
            split=Math.Clamp(checked((split+5)/10*10),start,end);
        }
        else if(mode==CursorSplitTiming.VideoFrame)
        {
            ArgumentNullException.ThrowIfNull(timing);
            if(frame<0||frame>=(videoFrameCount??timing.Count))throw new ArgumentOutOfRangeException(nameof(frame));
            var first=timing.FrameAtMilliseconds(start,FrameTimeKind.Start);var last=timing.FrameAtMilliseconds(end,FrameTimeKind.End);
            split=Math.Clamp(timing.SubtitleBoundary(Math.Clamp(frame,Math.Min(first,last),Math.Max(first,last)),FrameTimeKind.End),start,end);
        }
        if(mode!=CursorSplitTiming.Preserve)second.Start=AssTime.Format(split);
        using var transaction=Undo.BeginTransaction("Split at cursor");
        SetField(line,"Text",text[..cursor],"Split at cursor");
        if(mode!=CursorSplitTiming.Preserve)SetField(line,"End",AssTime.Format(split),"Split at cursor");
        var before=Document.Events.ElementAtOrDefault(Document.Events.IndexOf(line)+1);
        Structure("Insert split line",()=>Document.Insert(second,before));transaction.Commit();return second;
    }
    public IReadOnlyList<AssEvent> SplitAtFrame(IEnumerable<AssEvent> selection,FrameTiming timing,int frame,bool beforeFrame,int? videoFrameCount=null)
    {
        ArgumentNullException.ThrowIfNull(timing);
        if(frame<0||frame>=(videoFrameCount??timing.Count))throw new ArgumentOutOfRangeException(nameof(frame));
        var lines=Ordered(selection);if(lines.Length==0)return [];
        var plans=lines.Select(line=>
        {
            if(line.StartMilliseconds is not {} start||line.EndMilliseconds is not {} end||end<start)throw new ArgumentException("Invalid subtitle timing.");
            var copy=line.Clone();var split=timing.SubtitleBoundary(frame,beforeFrame?FrameTimeKind.Start:FrameTimeKind.End);
            var inside=frame>=timing.FrameAtMilliseconds(start,FrameTimeKind.Start)&&frame<=timing.FrameAtMilliseconds(end,FrameTimeKind.End);
            var divide=inside&&split>start&&split<end;
            if(divide)copy.Start=AssTime.Format(split);
            else
            {
                var begin=timing.SubtitleBoundary(frame,FrameTimeKind.Start);var finish=timing.SubtitleBoundary(frame,FrameTimeKind.End);
                copy.Start=AssTime.Format(begin);copy.End=AssTime.Format(Math.Max(checked(begin+10),finish));
            }
            return (Line:line,Copy:copy,Divide:divide,Split:split);
        }).ToArray();
        using var transaction=Undo.BeginTransaction(beforeFrame?"Split before current frame":"Split after current frame");
        foreach(var plan in plans)if(plan.Divide)SetField(plan.Line,"End",AssTime.Format(plan.Split),"Split at frame");
        InsertCopies(lines,plans.Select(plan=>plan.Copy).ToArray(),"Insert frame splits");transaction.Commit();
        return plans.Select(plan=>plan.Copy).ToArray();
    }
    private void InsertCopies(AssEvent[] lines,AssEvent[] copies,string name)
    {
        var original=Document.Events.ToArray();var selected=lines.ToHashSet();var mapping=lines.Zip(copies).ToDictionary(pair=>pair.First,pair=>pair.Second);
        List<(AssEvent? Before,AssEvent[] Copies)> blocks=[];
        for(var index=0;index<original.Length;)
        {
            if(!selected.Contains(original[index])){index++;continue;}
            var first=index;while(index<original.Length&&selected.Contains(original[index]))index++;
            blocks.Add((original.ElementAtOrDefault(index),original[first..index].Select(line=>mapping[line]).ToArray()));
        }
        Structure(name,()=>{foreach(var block in blocks)foreach(var copy in block.Copies)Document.Insert(copy,block.Before);});
    }
    public AssEvent? Join(IEnumerable<AssEvent> selection,SubtitleJoinMode mode)
    {
        if(!Enum.IsDefined(mode))throw new ArgumentException("Unknown join mode.",nameof(mode));
        var lines=Ordered(selection);if(lines.Length<2)return lines.FirstOrDefault();
        foreach(var line in lines)if(line.StartMilliseconds is not {} begin||line.EndMilliseconds is not {} finish||finish<begin)throw new ArgumentException("Invalid subtitle timing.");
        var text=mode switch
        {
            SubtitleJoinMode.Concatenate=>string.Join(" ",lines.Select(line=>line.Text)),
            SubtitleJoinMode.KeepFirst=>lines[0].Text,
            SubtitleJoinMode.Karaoke=>string.Concat(lines.Select(line=>"{\\k"+Math.Max(0,((line.EndMilliseconds??0)-(line.StartMilliseconds??0))/10).ToString(CultureInfo.InvariantCulture)+"}"+line.Text)),
            _=>throw new ArgumentException("Unknown join mode.",nameof(mode)),
        };
        var end=lines.Max(line=>line.EndMilliseconds??0);
        if(lines[0].StartMilliseconds is not {} start||end<start)throw new ArgumentException("Invalid subtitle timing.");
        using var transaction=Undo.BeginTransaction(mode==SubtitleJoinMode.Karaoke?"Join as karaoke":"Join subtitles");
        SetField(lines[0],"Text",text,"Join subtitles");SetField(lines[0],"End",AssTime.Format(end),"Join subtitles");
        Delete(lines.Skip(1));transaction.Commit();return lines[0];
    }
}
