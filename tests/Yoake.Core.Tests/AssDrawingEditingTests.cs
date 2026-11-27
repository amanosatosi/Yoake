using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class AssDrawingEditingTests
{
    [Theory]
    [InlineData("Rectangle", 0)]
    [InlineData("Ellipse", 4)]
    [InlineData("Rounded box", 4)]
    [InlineData("Triangle", 0)]
    [InlineData("Diamond", 0)]
    [InlineData("Star", 0)]
    public void PresetsSerializeAsScaledDrawings(string preset, int curves)
    {
        var box = new AssBox(20, 40, 100, 80);
        var path = AssDrawingEditing.CreatePreset(preset, box);
        var reloaded = AssVectorPath.Parse(path.Serialize(3), 3)!;
        Assert.Equal(box, AssDrawingEditing.ControlBounds(reloaded));
        Assert.Equal(curves, reloaded.Curves().Count(curve => curve.Cubic));
    }

    [Fact]
    public void DrawingReplacementKeepsOtherOverrideBlocksAndScale()
    {
        const string source = @"{\an7\pos(20,30)\custom(keep)\p3}m 0 0 l 80 0 80 80{\p0}{memo}";
        var run = AssDrawingEditing.FindIsolatedRun(source).Run!.Value;
        Assert.Equal(3, run.Scale);
        var replacement = AssDrawingEditing.CreatePreset("Triangle", new(0, 0, 20, 20));
        Assert.Equal(@"{\an7\pos(20,30)\custom(keep)\p3}m 40 0 l 80 80 l 0 80{\p0}{memo}", run.Replace(source, replacement));
    }

    [Theory]
    [InlineData(@"hello{\p1}m 0 0 l 10 10{\p0}")]
    [InlineData(@"{\p1}m 0 0{\r}visible")]
    [InlineData(@"{\p1}m 0 0{\t(0,1,\frz10)}l 10 10")]
    [InlineData(@"{\p1}m 0 0{\unfinished")]
    public void MixedOrUnclosedSourceIsNotTreatedAsReplaceableDrawing(string source)
    {
        var result = AssDrawingEditing.FindIsolatedRun(source);
        Assert.Null(result.Run);
        Assert.False(result.CanInsert);
    }

    [Fact]
    public void OrdinaryDialogueCanBecomeDrawingWithoutLosingItsTextOrTags()
    {
        const string source=@"{\an7\bord0}日本語\Nမြန်မာ";
        var prepared=AssDrawingEditing.PrepareNewDrawing(source);
        Assert.Equal(@"{\an7\bord0}{日本語\Nမြန်မာ}",prepared);
        var drawing=AssDrawingEditing.Insert(prepared!,"m 0 0 l 20 0 l 20 20");
        Assert.NotNull(AssDrawingEditing.FindIsolatedRun(drawing).Run);
        Assert.Contains(@"{日本語\Nမြန်မာ}",drawing);
        Assert.Null(AssDrawingEditing.PrepareNewDrawing(@"text{\p1}m 0 0 l 10 10{\p0}"));
        Assert.Null(AssDrawingEditing.PrepareNewDrawing(@"literal \bord5"));
    }

    [Fact]
    public void NumericAxisTagWinsOverUniformWithoutRewritingTheTransform()
    {
        const string source = @"{\xbord2\bord5\t(0,100,\xbord7)\unknown(keep)}sign{\xbord8}next";
        Assert.Equal(5, AssVisualTags.VisualScalar(source, null, "xbord"));
        Assert.Equal(@"{\xbord2\bord5\xbord9\t(0,100,\xbord7)\unknown(keep)}sign{\xbord8}next",
            AssVisualTags.SetVisualScalar(source, "xbord", 9));
    }
    [Theory]
    [InlineData(7)]
    [InlineData(5)]
    [InlineData(3)]
    public void ProjectionRoundTripsAndKeepsUntouchedVerticesWhenBoundsChange(int alignment)
    {
        var position = new AssPoint(200, 180);
        var origin = new AssPoint(160, 130);
        var transform = new AssTransform(22, -31, 17, 125, 75, .2, -.1);
        var projection = new AssDrawingProjection(position, origin, transform, new(0, 0, 80, 60), alignment, .75);
        var vertex = new AssPoint(42, 27);
        var mapped = projection.Map(vertex);
        Assert.True(VisualGeometry.Distance(vertex, projection.Unmap(mapped)) < 1e-8);
        var changed = new AssBox(0, 0, 100, 72);
        var correction = projection.AlignmentCorrection(changed, alignment);
        var next = new AssDrawingProjection(position, origin, transform, changed, alignment, .75);
        Assert.True(VisualGeometry.Distance(mapped, next.Map(VisualGeometry.Add(vertex, correction))) < 1e-8);
    }

    [Fact]
    public void TextGradientUsesGraphemesAndPreservesOverrideBlocks()
    {
        const string source = "A{note}\u0301{\\unknown(keep)}😀\\Nあ";
        var gradient = new AssNumericTextGradient(source, "bord", 1);
        Assert.Equal("{\\bord1}A{note}\u0301{\\unknown(keep)}{\\bord3}😀\\N{\\bord5}あ", gradient.Apply(2, false));
        Assert.Equal("{\\bord5}A{note}\u0301{\\unknown(keep)}{\\bord3}😀\\N{\\bord1}あ", gradient.Apply(2, true));
        Assert.Equal(source, gradient.Apply(0, false));
    }

    [Fact]
    public void TrackedMoveReplacesPositionAndKeepsUnrelatedSource()
    {
        var source = @"{\an7\pos(2,3)\org(4,5)\unknown(keep)}Sign";
        var result = AssVisualTags.SetTrackedMove(source, new(new(2,3),new(22,15),200,600));
        Assert.Null(AssVisualTags.Position(result));
        Assert.Equal(new AssMove(new(2,3),new(22,15),200,600), AssVisualTags.Move(result)!.Value);
        Assert.Contains(@"\org(4,5)\unknown(keep)", result);
    }
}
