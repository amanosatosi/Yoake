using Yoake.Core.Subtitles;
using Yoake.Core.Media;

namespace Yoake.Core.Tests;

public sealed class SubtitlePostprocessTests
{
    private static AssEvent Add(SubtitleEditor editor,long start,long end,string style="Default")
    {var line=editor.Insert(null,true);line.Start=AssTime.Format(start);line.End=AssTime.Format(end);line.Style=style;line.Text=@"{\future(opaque)}日本語 မြန်မာ é";return line;}
    [Fact]
    public void LeadsAvoidNewCollisionsAndPublishInOneUndoStep()
    {
        var editor=new SubtitleEditor(AssDocument.CreateEmpty());var a=Add(editor,1000,2000);var b=Add(editor,2100,3000);var before=editor.Document.Serialize();
        var plan=editor.CaptureTiming([a,b]).Postprocess(new() { AddLeadIn=true,LeadIn=200,AddLeadOut=true,LeadOut=200,MakeAdjacent=false });
        Assert.Equal(before,editor.Document.Serialize());Assert.Equal(2,editor.ApplyTiming(plan));
        Assert.Equal(800,a.StartMilliseconds);Assert.Equal(2000,a.EndMilliseconds);Assert.Equal(2000,b.StartMilliseconds);Assert.Equal(3200,b.EndMilliseconds);
        Assert.Contains(@"\future(opaque)",a.Text);var after=editor.Document.Serialize();editor.Undo.Undo();Assert.Equal(before,editor.Document.Serialize());editor.Undo.Redo();Assert.Equal(after,editor.Document.Serialize());
    }
    [Fact]
    public void AdjacencyUsesStableTimingOrderStyleScopeAndBias()
    {
        var editor=new SubtitleEditor(AssDocument.CreateEmpty());var later=Add(editor,1200,2200);var earlier=Add(editor,0,1000);var ignored=Add(editor,1100,1300,"Sign");
        var plan=editor.CaptureTiming([earlier,later,ignored]).Postprocess(new() { Styles=["Default"],MaximumGap=250,AdjacentBias=0.25 });
        editor.ApplyTiming(plan);Assert.Same(later,editor.Document.Events[0]);Assert.Equal(1050,earlier.EndMilliseconds);Assert.Equal(1050,later.StartMilliseconds);
        Assert.Equal(1100,ignored.StartMilliseconds);Assert.Equal(1300,ignored.EndMilliseconds);
    }
    [Fact]
    public void ExistingOverlapsAreNotMistakenForNewLeadCollisions()
    {
        var editor=new SubtitleEditor(AssDocument.CreateEmpty());var a=Add(editor,1000,2200);var b=Add(editor,2100,3000);
        editor.ApplyTiming(editor.CaptureTiming([a,b]).Postprocess(new() { AddLeadIn=true,LeadIn=200,AddLeadOut=true,LeadOut=200,MakeAdjacent=false }));
        Assert.Equal(800,a.StartMilliseconds);Assert.Equal(2400,a.EndMilliseconds);Assert.Equal(1900,b.StartMilliseconds);Assert.Equal(3200,b.EndMilliseconds);
    }
    [Fact]
    public void VfrKeyframeEndsSnapBeforeTheKeyframeUsingTheCentralMap()
    {
        var editor=new SubtitleEditor(AssDocument.CreateEmpty());var line=Add(editor,120,180);
        var timing=new FrameTiming([0,.04,.10,.14,.20,.24],25);
        var options=new SubtitlePostprocessOptions { MakeAdjacent=false,SnapKeyframes=true,StartBefore=0,StartAfter=0,EndBefore=100,EndAfter=0 };
        editor.ApplyTiming(editor.CaptureTiming([line]).Postprocess(options,timing,[5]));
        Assert.Equal(120,line.StartMilliseconds);Assert.Equal(220,line.EndMilliseconds);
    }
    [Fact]
    public void CommentsInheritedAndUnselectedRowsRemainUntouched()
    {
        var editor=new SubtitleEditor(AssDocument.CreateEmpty());var selected=Add(editor,1000,2000);var other=Add(editor,3000,4000);
        var inherited=Add(editor,5000,6000);inherited.Text="{uketsugu}source";
        editor.SetMangetsuMetadata(MangetsuMetadataKind.Style,"Default",@"{\vert1}");var metadata=editor.Document.Events[0];var original=metadata.Start;
        editor.ApplyTiming(editor.CaptureTiming([selected,inherited,metadata]).Postprocess(new() { AddLeadIn=true,MakeAdjacent=false }));
        Assert.Equal(900,selected.StartMilliseconds);Assert.Equal(3000,other.StartMilliseconds);Assert.Equal(5000,inherited.StartMilliseconds);Assert.Equal(original,metadata.Start);
    }
    [Fact]
    public void InvalidResultCancellationAndStalePlanCannotPartiallyApply()
    {
        var editor=new SubtitleEditor(AssDocument.CreateEmpty());var a=Add(editor,1000,2000);var b=Add(editor,1900,1950);var before=editor.Document.Serialize();
        Assert.Throws<ArgumentException>(()=>editor.CaptureTiming([a,b]).Postprocess(new() { AdjacentBias=0,MaximumOverlap=200 }));Assert.Equal(before,editor.Document.Serialize());
        Assert.Throws<OperationCanceledException>(()=>editor.CaptureTiming([a,b]).Postprocess(new(),token:new CancellationToken(true)));
        var plan=editor.CaptureTiming([a]).Postprocess(new() { AddLeadIn=true,MakeAdjacent=false });b.Text="changed";var changed=editor.Document.Serialize();
        Assert.Throws<InvalidOperationException>(()=>editor.ApplyTiming(plan));Assert.Equal(changed,editor.Document.Serialize());
    }
    [Fact]
    public void LargeEpisodeAvoidsQuadraticCollisionScans()
    {
        var source=AssDocument.CreateEmpty().Serialize()+string.Concat(Enumerable.Range(0,10000).Select(i=>$"Dialogue: 0,{AssTime.Format(i*2000L+500)},{AssTime.Format(i*2000L+1500)},Default,,0,0,0,,text\n"));
        var editor=new SubtitleEditor(AssDocument.Parse(source));
        var plan=editor.CaptureTiming([]).Postprocess(new() { SelectedOnly=false,AddLeadIn=true,AddLeadOut=true,MakeAdjacent=false });
        Assert.Equal(10000,plan.Count);Assert.Equal(source,editor.Document.Serialize());
    }
}
