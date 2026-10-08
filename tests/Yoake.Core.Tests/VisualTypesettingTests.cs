using Yoake.Core.Audio;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class VisualTypesettingTests
{
    private static readonly AssDocument Document=AssDocument.CreateEmpty();
    [Theory][InlineData(1,30,1050)][InlineData(2,960,1050)][InlineData(3,1890,1050)][InlineData(5,960,540)][InlineData(7,30,30)][InlineData(9,1890,30)]
    public void StandbyPositionUsesStyleAndInlineAlignment(int alignment,double x,double y)
    {
        var line=Document.NewEvent();line.Text="{\\an"+alignment+"}sign";
        Assert.Equal(new AssPoint(x,y),AssVisualTags.DefaultPosition(line,Document.Styles[0],1920,1080));
    }
    [Fact] public void CenterUsesAsymmetricMarginsAndLineOverrides()
    {
        var line=Document.NewEvent();line.Set("MarginL","100");line.Set("MarginR","40");
        Assert.Equal(new AssPoint(990,1050),AssVisualTags.DefaultPosition(line,Document.Styles[0],1920,1080));
    }
    [Theory][InlineData("\\move(0,10,100,110)",500,50,60)][InlineData("\\move(0,10,100,110,200,800)",500,50,60)][InlineData("\\move(0,10,100,110,200,800)",900,100,110)]
    public void MovePositionFollowsCurrentFrame(string tag,long time,double x,double y)=>Assert.Equal(new AssPoint(x,y),AssVisualTags.PositionAtTime("{"+tag+"}",time,1000));
    [Fact] public void QuickPositionShiftsMovementAndExplicitOriginWithoutChangingTiming()
    {
        const string source="{\\future(opaque)\\move(10,20,30,40,120,800)\\org(7,9)\\t(0,500,\\frz30)}日本語";
        var result=AssVisualTags.ShiftPosition(source,new(0,0),new(5,-2));
        Assert.Equal("{\\future(opaque)\\move(15,18,35,38,120,800)\\org(12,7)\\t(0,500,\\frz30)}日本語",result);
    }
    [Fact] public void EndpointAssignsFrameTimeAndMoveToggleRetainsStart()
    {
        var moved=AssVisualTags.MoveEndpoint("{\\move(10,20,30,40,100,800)}",1,new(2,3),600,1000);
        Assert.Equal(new AssMove(new(10,20),new(32,43),100,600),AssVisualTags.Move(moved));
        Assert.Equal("{\\pos(10,20)}",AssVisualTags.ToggleMove(moved,default,0,40));
        Assert.Equal("{\\move(9,8,9,8,40,80)}",AssVisualTags.ToggleMove("{\\pos(9,8)}",default,40,80));
    }
    [Fact] public void RotationScaleDefaultsAndStaticAliasesResolve()
    {
        var style=AssDocument.CreateEmpty().Styles[0];style.Set("Angle","12");style.Set("ScaleX","120");style.Set("ScaleY","80");
        Assert.Equal(new AssTransform(0,0,12,120,80,0,0),AssVisualTags.Transform("sign",style));
        Assert.Equal(new AssTransform(15,30,45,90,60,0.2,-0.1),AssVisualTags.Transform("{\\fr20\\frz45\\frx15\\fry30\\fscx90\\fscy60\\fax0.2\\fay-0.1\\t(\\frz90)}sign",style));
        Assert.Equal("{\\frz60\\frx15\\t(\\frz90)}sign",AssVisualTags.SetScalar("{\\fr20\\frx15\\t(\\frz90)}sign","frz",60));
    }
    [Fact] public void MangetsuRelativeScaleAndPostResetEditingStayEffective()
    {
        Assert.Equal(110,AssVisualTags.Scalar(@"{\fscx100\fscx+20\fscx-10}","fscx",100));
        Assert.Equal(-20,AssVisualTags.Scalar(@"{\frz-20}","frz",0));
        Assert.Equal(40,AssVisualTags.Scalar(AssVisualTags.SetScalar(@"{\fr20\r}sign","frz",40),"frz",0));
        Assert.Equal(75,AssVisualTags.Scalar(AssVisualTags.SetScalar(@"{\r}sign","fscx",75),"fscx",100));
    }
    [Fact] public void ProjectionUsesMangetsuZXYRotationAndLayoutDistance()
    {
        var transform=new AssTransform(60,0,0,100,100,0,0);
        var projected=VisualGeometry.Project(new(0,100),default,transform,2);
        Assert.Equal(50*625/(625-100*Math.Sin(Math.PI/3)),projected.Y,8);
        var y=VisualGeometry.Project(new(100,0),default,transform with{X=0,Y=60},2);
        Assert.Equal(50*625/(625+100*Math.Sin(Math.PI/3)),y.X,8);
    }
    [Fact] public void ModifierMathPreservesAspectConstrainsAndSnaps()
    {
        Assert.Equal(30,VisualGeometry.Snap(44,30));Assert.Equal(-30,VisualGeometry.Snap(-44,30));
        Assert.Equal(new AssPoint(125,62.5),VisualGeometry.Scale(new(100,50),new(24,4),false,true,true));
        Assert.Equal(new AssPoint(120,100),VisualGeometry.Scale(new(100,100),new(20,4),true,false,false));
        Assert.Equal(new AssPoint(0,0),VisualGeometry.Scale(new(100,100),new(-400,-500),false,false,true));
    }
    [Fact] public void ClipPositionRelativeAnimationAndScaleDoNotBakeGeometry()
    {
        const string source="{\\iclip(3,m 0 0 l 400 0 400 400)\\clippos(20,10)\\t(0,1000,\\clippos(~+40,~-20))\\future(x)}sign";
        Assert.Equal(new AssPoint(40,20),AssVisualTags.ClipOffset(source,500,1000));
        var result=AssVisualTags.TranslateClip(source,new(5,7));
        Assert.Contains("\\iclip(3,m 0 0 l 400 0 400 400)",result);Assert.Contains("\\clippos(~+5,~-7)",result);
        Assert.Contains("\\t(0,1000,\\clippos(~+40,~-20))",result);
        Assert.Equal(new AssPoint(45,27),AssVisualTags.ClipOffset(result,500,1000));
        const string absolute="{\\clip(0,0,100,100)\\t(0,1000,\\clippos(40,20))}";
        Assert.Equal(new AssPoint(25,17),AssVisualTags.ClipOffset(AssVisualTags.TranslateClip(absolute,new(5,7)),500,1000));
        var map=AssVisualTags.ClipTransform("{\\clip(100,80,300,240)\\clips125\\clippos(20,-10)}");
        Assert.Equal(new AssPoint(95,50),map.Map(new(100,80)));Assert.Equal(new AssPoint(100,80),map.Unmap(new(95,50)));
    }
    [Fact] public void RectangleAndInverseCreationAreLossless()
    {
        const string source="{\\unknown(a)\\t(0,500,\\clip(0,0,10,10))}sign";
        var result=AssVisualTags.SetRectangle(source,true,new(30,40),new(10,20));
        Assert.StartsWith("{\\iclip(10,20,30,40)",result);Assert.Contains("\\t(0,500,\\clip(0,0,10,10))",result);
        Assert.Equal(result.Replace("\\iclip(10,20,30,40)","\\clip(10,20,30,40)"),AssVisualTags.InvertClip(result));
    }
    [Fact] public void CubicSplitPreservesTheEntireCurve()
    {
        var path=AssVectorPath.Parse("m 0 0 b 0 100 100 100 100 0")!;var original=path.Curves().Single();path.Split(original,0.4);var curves=path.Curves().ToArray();
        for(var i=0;i<=100;i++){var t=i/100d;var point=t<=0.4?curves[0].At(t/0.4):curves[1].At((t-0.4)/0.6);Assert.True(VisualGeometry.Distance(original.At(t),point)<0.000001);}
        Assert.NotNull(AssVectorPath.Parse(path.Serialize(3),3));
    }
    [Fact] public void VectorConvertRemoveMultiPointAndFreehandProduceValidDrawings()
    {
        var path=AssVectorPath.Parse("m 0 0 l 100 100 200 0")!;path.Convert(path.Curves().First());Assert.True(path.Curves().First().Cubic);
        path.Translate(new HashSet<int>{0,1},new(4,5));Assert.Equal(new AssPoint(4,5),path.Handles().First().Point);
        path.Remove(1);Assert.False(path.Curves().First().Cubic);
        foreach(var smooth in new[]{false,true}){var free=AssVectorPath.Freehand([new(0,0),new(20,30),new(40,10)],smooth);Assert.NotNull(AssVectorPath.Parse(free.Serialize(1)));Assert.Equal(smooth,free.Curves().First().Cubic);}
        Assert.Null(AssVectorPath.Parse("m 0 0 b 1 2"));Assert.Null(AssVectorPath.Parse("m 0 0 x 1 2"));
    }
    [Fact] public void SplineExpansionMatchesMangetsusInitialContourAndNoCloseMove()
    {
        var path=AssVectorPath.Parse("m 0 0 s 60 0 60 60 0 60")!;
        var curve=Assert.Single(path.Curves());Assert.True(curve.Cubic);
        Assert.Equal(new AssPoint(50,10),curve.Start);Assert.Equal(new AssPoint(60,20),curve.Control1);
        Assert.Equal(new AssPoint(60,40),curve.Control2);Assert.Equal(new AssPoint(50,50),curve.End);
        var noClose=AssVectorPath.Parse("m 0 0 l 100 0 n 200 200 l 100 100")!;
        Assert.Equal(2,noClose.Curves().Count());Assert.Equal(new AssPoint(100,0),noClose.Curves().Last().Start);
        Assert.NotNull(AssVectorPath.Parse("m 0 0 s 60 0 60 60 0 60 c"));
    }
    [Fact] public void ClosingSegmentCanConvertAndLastPointCanBeRemovedLosslessly()
    {
        var path=AssVectorPath.Parse("m 0 0 l 100 0 100 100")!;
        path.Convert(path.Curves(true).Last());Assert.True(path.Curves().Last().Cubic);
        Assert.Equal(new AssPoint(0,0),path.Curves().Last().End);
        const string text=@"{\iclip(m 1 2)\clippos(5,6)\future(x)\t(\clip(0,0,5,5))}sign";
        Assert.Equal(@"{\clippos(5,6)\future(x)\t(\clip(0,0,5,5))}sign",AssVisualTags.RemoveClip(text));
    }
    [Fact] public void VectorScaleInverseAndTransformsSurviveIntentionalTopologyEdit()
    {
        const string source="{\\iclip(3,m 0 0 l 400 400)\\clippos(2,3)\\t(\\clippos(20,30))\\unknown(x)}sign";
        var clip=AssVisualTags.Clip(source)!;var path=AssVectorPath.Parse(clip.Drawing,clip.Scale)!;path.Split(path.Curves().First(),0.5);
        var result=AssVisualTags.SetVector(source,clip.Inverse,clip.Scale,path.Serialize(clip.Scale));
        Assert.Contains("\\iclip(3,m 0 0 l 200 200 l 400 400)",result);Assert.Contains("\\clippos(2,3)\\t(\\clippos(20,30))\\unknown(x)",result);
    }
    [Fact] public void LegacyDistortStaysUntouchedUntilIntentionalEightSlotEdit()
    {
        const string source="{\\distort(1,0,1,1,0,1)\\future(x)\\t(\\distort(2,0,1,1,0,1))}sign";
        Assert.Contains("\\distort(1,0,1,1,0,1)",AssVisualTags.SetOrigin(source,new(10,20)));
        var pins=AssVisualTags.Distort(source)!.ToArray();pins[0]=new(.2,.3);var result=AssVisualTags.SetDistort(source,pins);
        Assert.Equal(source.Replace("\\distort(1,0,1,1,0,1)","\\distort(1,0,1,1,0,1,0.2,0.3)"),result);Assert.DoesNotContain("\\perspective",result);
        Assert.Equal(pins,AssVisualTags.Distort(result));
    }
    [Fact] public void MeasurementSnapshotsKeepFormatsAndShapingWithoutMutatingSource()
    {
        var doc=AssDocument.Parse("[Script Info]\nPlayResX: 384\nPlayResY: 288\n[V4+ Styles]\nFormat: Name,Fontname,Fontsize,Alignment\nStyle: Default,Arial,40,5\n[Events]\nFormat: Start,End,Style,Text,Extra\nDialogue: 0:00:00.00,0:00:01.00,Default,{\\1c&H000000&\\distort(1,0,1,1,0,1)}日本語,opaque\nDialogue: 0:00:00.00,0:00:01.00,Default,other,keep\n");
        var original=doc.Serialize();var header=VisualMeasurement.Header(doc);Assert.DoesNotContain("Dialogue:",header);
        var snapshot=VisualMeasurement.Snapshot(doc.Events[0],doc.Events[0].Text);
        var track=AssDocument.Parse(VisualMeasurement.Track(header,snapshot,192,144));var measured=Assert.Single(track.Events);
        Assert.Equal("opaque",measured.Get("Extra"));Assert.Contains("日本語",measured.Text);Assert.Contains(@"\1c&HFFFFFF&",measured.Text);
        Assert.DoesNotContain(@"\1c&H000000&",measured.Text);Assert.DoesNotContain(@"\distort",measured.Text);
        Assert.Equal(original,doc.Serialize());
    }
    [Fact] public void ZoomAnchorsAndAmplitudeLinkHaveSeparateDomains()
    {
        Assert.Equal(20,AudioViewportMath.Span(AudioViewportMath.Zoom(20)),8);
        Assert.Equal(105,AudioViewportMath.AnchoredStart(100,20,10,110,1000));
        Assert.Equal(1,AudioViewportMath.Amplitude(50));Assert.Equal(8,AudioViewportMath.Amplitude(100));
        Assert.Equal(.5,AudioViewportMath.LinkedVolume(1));Assert.Equal(1,AudioViewportMath.LinkedVolume(8));
    }
}
