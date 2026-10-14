using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class ResolutionResampleTests
{
    private static SubtitleEditor Editor(string text="plain",string extraInfo="",string suffix="",bool comment=false,string effect="")=>new(AssDocument.Parse(
        "[Script Info]\r\nPlayResX: 640\r\nPlayResY: 480\r\nYCbCr Matrix: TV.709\r\n"+extraInfo+
        "[V4+ Styles]\nFormat: Name, Fontname, Fontsize, Outline, Shadow, Spacing, ScaleX, ScaleY, MarginL, MarginR, MarginV, Future\n"+
        "Style: Default,Arial,31,2,3,4,100,95,10,0,20,opaque\n"+
        "[Events]\nFormat: Layer,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Text,Future\n"+
        (comment?"Comment: ":"Dialogue: ")+"0,0:00:01.00,0:00:02.00,Default,役,0,10,20,"+effect+","+text+",keep\n"+suffix));
    private static ResamplePlan Plan(SubtitleEditor editor,ResampleAspect aspect=ResampleAspect.Stretch,int width=1280,int height=960,int left=0,int right=0,int top=0,int bottom=0)=>editor.CaptureResample().Calculate(new(640,480,width,height,aspect,left,right,top,bottom));
    private static string Text(string text,ResampleAspect aspect=ResampleAspect.Stretch,int width=1280,int height=960,int left=0,int right=0,int top=0,int bottom=0)
    {var editor=Editor(text);editor.ApplyResample(Plan(editor,aspect,width,height,left,right,top,bottom));return editor.Document.Events[0].Text;}
    [Theory]
    [InlineData(ResampleAspect.Stretch,0,0,2,1.5,4d/3)]
    [InlineData(ResampleAspect.AddBorders,106,0,1280d/852,1.5,1)]
    [InlineData(ResampleAspect.RemoveBorders,0,-60,2,2,1)]
    [InlineData(ResampleAspect.Manual,0,0,2,1.5,4d/3)]
    public void ExactAspectPolicies(ResampleAspect aspect,int left,int top,double x,double y,double stretch)
    {var g=ResampleGeometry.Calculate(new(640,480,1280,720,aspect));Assert.Equal(left,g.Left);Assert.Equal(top,g.Top);Assert.Equal(x,g.X,9);Assert.Equal(y,g.Y,9);Assert.Equal(stretch,g.Stretch,9);}
    [Theory]
    [InlineData(0,480,1280,720)] [InlineData(640,-1,1280,720)] [InlineData(640,480,1000000,720)] [InlineData(640,480,1280,0)]
    public void InvalidDimensionsAreRejected(int sx,int sy,int x,int y)=>Assert.Throws<ArgumentException>(()=>ResampleGeometry.Calculate(new(sx,sy,x,y)));
    [Fact] public void NegativeManualDimensionsAndUnknownPolicyAreRejected()
    {Assert.Throws<ArgumentException>(()=>ResampleGeometry.Calculate(new(640,480,1280,720,ResampleAspect.Manual,-640)));Assert.Throws<ArgumentException>(()=>ResampleGeometry.Calculate(new(640,480,1280,720,(ResampleAspect)99)));}
    [Fact] public void NearAspectThresholdDoesNotStretchFonts()
    {var g=ResampleGeometry.Calculate(new(848,480,1280,720));Assert.Equal(1,g.Stretch);Assert.Equal(1280d/848,g.X,9);}
    [Fact] public void StyleMetricsAndInheritedMarginsFollowSourceAndUndoExactly()
    {
        var editor=Editor();var source=editor.Document.Serialize();var plan=Plan(editor,width:1280,height:720);editor.ApplyResample(plan);var style=editor.Document.Styles[0];
        Assert.Equal("47",style.Get("Fontsize"));Assert.Equal("3",style.Get("Outline"));Assert.Equal("4.5",style.Get("Shadow"));Assert.Equal("6",style.Get("Spacing"));
        Assert.Equal("133.333333333",style.Get("ScaleX"));Assert.Equal("95",style.Get("ScaleY"));Assert.Equal("20",style.Get("MarginL"));
        Assert.Equal("opaque",style.Get("Future"));Assert.Equal("0",editor.Document.Events[0].MarginL);Assert.Equal("20",editor.Document.Events[0].MarginR);Assert.Equal("30",editor.Document.Events[0].MarginV);
        Assert.Equal("TV.709",editor.Document.GetScriptInfo("YCbCr Matrix"));Assert.Equal("0:00:01.00",editor.Document.Events[0].Start);
        editor.Undo.Undo();Assert.Equal(source,editor.Document.Serialize());Assert.False(editor.Undo.CanUndo);editor.Undo.Redo();Assert.Equal("1280",editor.Document.GetScriptInfo("PlayResX"));
    }
    [Fact] public void AbsoluteGeometryAndNestedTransformsPreserveTimesAlphaAndSeeds()
    {Assert.Equal(@"{\pos(20,40)\org(60,80)\move(2,4,6,8,100,500)\t(0,700,2,\fs40\t(2,\xbord6))\k23\1a&H80&\rnds17\blur1.25\be2}日本語",Text(@"{\pos(10,20)\org(30,40)\move(1,2,3,4,100,500)\t(0,700,2,\fs20\t(2,\xbord3))\k23\1a&H80&\rnds17\blur1.25\be2}日本語"));}
    [Fact] public void RectangularAndScaledVectorClipsUseSourceUnits()
    {Assert.Equal(@"{\clip(22,24,26,28)\iclip(3,m 80 80 l 88 96)}x",Text(@"{\clip(1,2,3,4)\iclip(3,m 0 0 l 4 8)}x",ResampleAspect.Manual,1320,980,10,10,10,0));}
    [Fact] public void VectorDrawingSyntaxAndSplineTopologyArePreserved()
    {Assert.Equal("{\\p2}M 0 0\tl 40 20  s 4 8 12 16 20 24 p 28 32 c{\\p0}text",Text("{\\p2}M 0 0\tl 20 10  s 2 4 6 8 10 12 p 14 16 c{\\p0}text"));}
    [Fact] public void DrawingsUseIndependentTextStretchWhileClipDoesNot()
    {Assert.Equal(@"{\p1\clip(m 0 0 l 20 15)}m 0 0 l 15 15",Text(@"{\p1\clip(m 0 0 l 10 10)}m 0 0 l 10 10",width:1280,height:720));}
    [Fact] public void AdjacentOverrideBlocksAndEmptyCommentsDoNotDesynchronizeDrawingState()
    {Assert.Equal(@"{\p1}{}{\bord2}m 0 0 l 20 20{\p0}{\pos(20,40)}text",Text(@"{\p1}{}{\bord1}m 0 0 l 10 10{\p0}{\pos(10,20)}text"));}
    [Fact] public void RelativePositionsDoNotAcquireBorderOffsets()
    {Assert.Equal(@"{\pos(~+20,~-40)\clippos(6,-8)\fs+12}x",Text(@"{\pos(~+10,~-20)\clippos(3,-4)\fs+6}x",ResampleAspect.Manual,1320,1000,10,10,10,10));}
    [Theory] [InlineData("0,0,1,0,1,1")] [InlineData("0,0,1,0,1,1,0,1")]
    public void MangetsuDistortionPinsRemainNormalized(string pins)
    {var text=@"{\distort("+pins+@")\pos(10,20)}x";Assert.Equal(@"{\distort("+pins+@")\pos(20,40)}x",Text(text));}
    [Fact] public void BoxPaddingIsSplitForAnisotropicGeometryAndRadiusWarns()
    {var editor=Editor(@"{\boxp5\boxr3}x");var plan=Plan(editor,width:1280,height:720);Assert.Contains(plan.Warnings,w=>w.Tag=="\\boxr");editor.ApplyResample(plan);Assert.Equal(@"{\boxpx10\boxpy7.5\boxr3}x",editor.Document.Events[0].Text);}
    [Fact] public void PositionedGradientScalesOnlyBounds()
    {Assert.Equal(@"{\1pgrd(20,40,60,80,45,&H112233&,0.5,&HAABBCC&)}x",Text(@"{\1pgrd(10,20,30,40,45,&H112233&,0.5,&HAABBCC&)}x"));}
    [Fact] public void AmbiguousGeometryAndMalformedVectorsArePreservedWithWarnings()
    {var source=@"{\future(2,3)\clip(m 0)\pos(expr,2)\mover(1,2,3)}x";var editor=Editor(source);var plan=Plan(editor);Assert.Equal(4,plan.Warnings.Count);editor.ApplyResample(plan);Assert.Equal(source,editor.Document.Events[0].Text);}
    [Theory] [InlineData("template line")] [InlineData("code once")]
    public void TemplateAndCodeCommentsAreExcluded(string effect)
    {var editor=Editor(@"{\pos(10,20)}!x!",comment:true,effect:effect);var line=editor.Document.Events[0];var fields=line.FieldNames.Select(line.Get).ToArray();editor.ApplyResample(Plan(editor));Assert.Equal(fields,line.FieldNames.Select(line.Get));}
    [Fact] public void LayoutResUsesExactSuppliedFormulaAndMatrixIsRetained()
    {var editor=Editor(extraInfo:"LayoutResX: 640\nLayoutResY: 480\n");editor.ApplyResample(Plan(editor,ResampleAspect.Manual,1280,1040,top:20,bottom:20));Assert.Equal("510",editor.Document.GetScriptInfo("LayoutResY"));Assert.Equal("628",editor.Document.GetScriptInfo("LayoutResX"));Assert.Equal("TV.709",editor.Document.GetScriptInfo("YCbCr Matrix"));}
    [Fact] public void MissingLayoutResIsNotCreated()
    {var editor=Editor();editor.ApplyResample(Plan(editor));Assert.Equal("",editor.Document.GetScriptInfo("LayoutResX"));Assert.Equal("",editor.Document.GetScriptInfo("LayoutResY"));}
    [Theory] [InlineData("",384,288)] [InlineData("PlayResY: 1024\n",1280,1024)] [InlineData("PlayResX: 1280\n",1280,1024)] [InlineData("PlayResY: 600\n",800,600)]
    public void MissingSourceResolutionUsesAegisubFallback(string info,int x,int y)
    {Assert.Equal((x,y),new SubtitleEditor(AssDocument.Parse("[Script Info]\n"+info)).CaptureResample().Resolution);}
    [Fact] public void NoOpDoesNotPublishDraftOrCreateUndo()
    {var editor=Editor();var source=editor.Document.Serialize();var plan=editor.CaptureResample(editor.Document.Events[0],new Dictionary<string,string>{{"Text","draft"}}).Calculate(new(640,480,640,480));Assert.False(plan.HasChanges);editor.ApplyResample(plan);Assert.Equal(source,editor.Document.Serialize());Assert.False(editor.Undo.CanUndo);}
    [Fact] public void ExplicitSourceOverrideStillUpdatesScriptHeaders()
    {var editor=Editor();var plan=editor.CaptureResample().Calculate(new(1280,720,1280,720));editor.ApplyResample(plan);Assert.Equal("1280",editor.Document.GetScriptInfo("PlayResX"));Assert.Equal("31",editor.Document.Styles[0].Get("Fontsize"));}
    [Fact] public void StaleAndForeignPlansCannotPublish()
    {var editor=Editor();var plan=Plan(editor);Assert.Throws<InvalidOperationException>(()=>Editor().ApplyResample(plan));editor.SetField(editor.Document.Events[0],"Text","new","Edit");var source=editor.Document.Serialize();Assert.Throws<InvalidOperationException>(()=>editor.ApplyResample(plan));Assert.Equal(source,editor.Document.Serialize());}
    [Fact] public void CancellationNeverChangesSource()
    {var editor=Editor();var source=editor.Document.Serialize();var plan=Plan(editor);using var cancellation=new CancellationTokenSource();cancellation.Cancel();Assert.Throws<OperationCanceledException>(()=>editor.CaptureResample().Calculate(new(640,480,1280,960),cancellation.Token));Assert.Throws<OperationCanceledException>(()=>editor.ApplyResample(plan,cancellation.Token));Assert.Equal(source,editor.Document.Serialize());}
    [Fact] public void ValidDraftAndResolutionChangeShareOneUndoAndRetainUnknownSource()
    {
        var suffix="[Fonts]\nfontname: keep.ttf\n!!!!\n[Future]\nraw: keep\n";var editor=Editor(suffix:suffix);var source=editor.Document.Serialize();var line=editor.Document.Events[0];
        var plan=editor.CaptureResample(line,new Dictionary<string,string>{{"Text","{\\pos(10,20)}draft\n日本語"},{"Name","draft actor"}}).Calculate(new(640,480,1280,960));
        editor.ApplyResample(plan);Assert.Equal(@"{\pos(20,40)}draft\N日本語",line.Text);Assert.Equal("draft actor",line.Actor);Assert.EndsWith(suffix,editor.Document.Serialize());editor.Undo.Undo();Assert.Equal(source,editor.Document.Serialize());Assert.False(editor.Undo.CanUndo);
    }
    [Fact] public void InvalidDraftCannotEnterTheSnapshot()
    {var editor=Editor();var source=editor.Document.Serialize();Assert.Throws<ArgumentException>(()=>editor.CaptureResample(editor.Document.Events[0],new Dictionary<string,string>{{"Start","unfinished"}}));Assert.Equal(source,editor.Document.Serialize());}
}
