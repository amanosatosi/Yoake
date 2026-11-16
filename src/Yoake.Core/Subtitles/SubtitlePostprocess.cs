using Yoake.Core.Media;

namespace Yoake.Core.Subtitles;

public sealed record SubtitlePostprocessOptions
{
    public string[]? Styles { get; init; }
    public bool SelectedOnly { get; init; }=true;
    public bool AddLeadIn { get; init; }
    public bool AddLeadOut { get; init; }
    public int LeadIn { get; init; }=100;
    public int LeadOut { get; init; }=350;
    public bool MakeAdjacent { get; init; }=true;
    public int MaximumGap { get; init; }=250;
    public int MaximumOverlap { get; init; }=100;
    public double AdjacentBias { get; init; }=0.5;
    public bool SnapKeyframes { get; init; }
    public int StartBefore { get; init; }=50;
    public int StartAfter { get; init; }=50;
    public int EndBefore { get; init; }=50;
    public int EndAfter { get; init; }=50;
    public SubtitlePostprocessOptions Normalize()=>this with {
        Styles=Styles?.Where(s=>!string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).ToArray(),
        LeadIn=Math.Clamp(LeadIn,0,60000),LeadOut=Math.Clamp(LeadOut,0,60000),
        MaximumGap=Math.Clamp(MaximumGap,0,60000),MaximumOverlap=Math.Clamp(MaximumOverlap,0,60000),
        AdjacentBias=double.IsFinite(AdjacentBias)?Math.Clamp(AdjacentBias,0,1):0.5,
        StartBefore=Math.Clamp(StartBefore,0,60000),StartAfter=Math.Clamp(StartAfter,0,60000),EndBefore=Math.Clamp(EndBefore,0,60000),EndAfter=Math.Clamp(EndAfter,0,60000) };
    internal void Validate()
    {
        if(new[] { LeadIn,LeadOut,MaximumGap,MaximumOverlap,StartBefore,StartAfter,EndBefore,EndAfter }.Any(n=>n is <0 or >60000)||!double.IsFinite(AdjacentBias)||AdjacentBias is <0 or >1)
            throw new ArgumentException("Timing thresholds must be between 0 and 60000 ms, and bias between 0 and 1.");
    }
}

public sealed partial class SubtitleTimingSnapshot
{
    // Same operation order as Aegisub 3.2.2: leads, adjacency, then keyframes.
    // Sorted sets replace its quadratic collision scans for large episodes.
    public SubtitleTimingPlan Postprocess(SubtitlePostprocessOptions options,FrameTiming? timing=null,IReadOnlyList<int>? keyframes=null,int? videoFrameCount=null,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(options);options.Validate();token.ThrowIfCancellationRequested();
        var styles=options.Styles?.ToHashSet(StringComparer.Ordinal);
        var rows=_rows.Where(r=>!r.IsComment&&!r.IsInherited&&(!options.SelectedOnly||r.Selected)&&(styles is null||styles.Contains(r.Style))).Select(r=>(Row:r,Time:Read(r))).OrderBy(r=>r.Time.Start).ToArray();
        var starts=rows.Select(r=>r.Time.Start).ToArray();var ends=rows.Select(r=>r.Time.End).ToArray();
        if(options.AddLeadIn&&options.LeadIn>0)
        {
            var previousEnds=new SortedSet<long>();
            for(var i=0;i<rows.Length;i++)
            {
                token.ThrowIfCancellationRequested();var candidate=Math.Max(0,starts[i]-options.LeadIn);
                using var prior=previousEnds.GetViewBetween(0,starts[i]).Reverse().GetEnumerator();
                if(prior.MoveNext())candidate=Math.Max(candidate,prior.Current);
                starts[i]=candidate;previousEnds.Add(ends[i]);
            }
        }
        if(options.AddLeadOut&&options.LeadOut>0)
        {
            var followingStarts=new SortedSet<long>();
            for(var i=rows.Length-1;i>=0;i--)
            {
                token.ThrowIfCancellationRequested();var candidate=checked(ends[i]+options.LeadOut);
                using var next=followingStarts.GetViewBetween(ends[i],long.MaxValue).GetEnumerator();
                if(next.MoveNext())candidate=Math.Min(candidate,next.Current);
                ends[i]=candidate;followingStarts.Add(starts[i]);
            }
        }
        if(options.MakeAdjacent)
            for(var i=1;i<rows.Length;i++)
            {
                token.ThrowIfCancellationRequested();var distance=starts[i]-ends[i-1];
                if(distance<0&&-distance<=options.MaximumOverlap||distance>0&&distance<=options.MaximumGap)
                {var boundary=ends[i-1]+(long)(distance*options.AdjacentBias);starts[i]=boundary;ends[i-1]=boundary;}
            }
        if(options.SnapKeyframes)
        {
            if(timing is null||keyframes is null||keyframes.Count==0)throw new ArgumentException("Load timecodes/video and keyframes before snapping.");
            var count=videoFrameCount??timing.Count;
            for(var i=0;i<keyframes.Count;i++)
                if(keyframes[i]<0||keyframes[i]>=count||i>0&&keyframes[i]<=keyframes[i-1])throw new ArgumentException("Keyframes must be ordered, unique and inside the frame map.");
            var keys=(videoFrameCount is >0?keyframes.Append(count-1):keyframes).Distinct().Order().ToArray();
            int Closest(int frame)
            {
                var found=Array.BinarySearch(keys,frame);if(found>=0)return keys[found];var next=~found;
                if(next==0)return keys[0];if(next==keys.Length)return keys[^1];
                return keys[next]-frame<frame-keys[next-1]?keys[next]:keys[next-1];
            }
            for(var i=0;i<rows.Length;i++)
            {
                token.ThrowIfCancellationRequested();var startFrame=timing.FrameAtMilliseconds(starts[i],FrameTimeKind.Start);var closest=Closest(startFrame);var time=timing.SubtitleBoundary(closest,FrameTimeKind.Start);
                if(closest>startFrame&&time-starts[i]<=options.StartBefore||closest<startFrame&&starts[i]-time<=options.StartAfter)starts[i]=time;
                var endFrame=timing.FrameAtMilliseconds(ends[i],FrameTimeKind.End);closest=Closest(endFrame)-1;time=timing.SubtitleBoundary(closest,FrameTimeKind.End);
                if(closest>endFrame&&time-ends[i]<=options.EndBefore||closest<endFrame&&ends[i]-time<=options.EndAfter)ends[i]=time;
            }
        }
        var plan=Plan("Timing Postprocessor",rows.Select((r,i)=>(r.Row,starts[i],ends[i])),token);
        plan.IncludesDraft=DraftLine is not null&&rows.Any(r=>ReferenceEquals(r.Row.Line,DraftLine));return plan;
    }
}
