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
    public void NumericAxisTagWinsOverUniformWithoutRewritingTheTransform()
    {
        const string source = @"{\xbord2\bord5\t(0,100,\xbord7)\unknown(keep)}sign{\xbord8}next";
        Assert.Equal(5, AssVisualTags.VisualScalar(source, null, "xbord"));
        Assert.Equal(@"{\xbord2\bord5\xbord9\t(0,100,\xbord7)\unknown(keep)}sign{\xbord8}next",
            AssVisualTags.SetVisualScalar(source, "xbord", 9));
    }
}
