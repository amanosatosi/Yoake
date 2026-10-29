using Yoake.Core.Media;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class MotionTrackingTests
{
    [Fact]
    public void ScaleAndRotationAreEncodedAndConflictingAuthoredStateIsRejected()
    {
        TrackingPoint[] points = [new(0, 40, 40, 1, 1, 0), new(1, 42, 41, 1, 1.1, 5), new(2, 44, 42, 1, 1.2, 10)];
        var text = MotionTrackApply.Plan(@"{\future(x)}text", points, [0, .04, .08], 0, 0, 80, 1, 1,
            applyScale: true, applyRotation: true);
        Assert.Contains(@"\scale~+10", text); Assert.Contains(@"\frz~-5", text);
        Assert.EndsWith("text", text);
        Assert.Throws<ArgumentException>(() => MotionTrackApply.Plan(@"{\scale125}text", points, [0, .04, .08], 0, 0, 80, 1, 1, applyScale: true));
    }
    [Fact]
    public void ClipTrackingKeepsGeometryAndRefusesCompetingTransforms()
    {
        TrackingPoint[] points = [new(0, 40, 40), new(1, 42, 41), new(2, 44, 42)];
        var source = @"{\iclip(m 0 0 l 50 50)\future(x)}text";
        var text = MotionTrackApply.Plan(source, points, [0, .04, .08], 0, 0, 80, 1, 1, applyClip: true);
        Assert.Contains(@"\iclip(m 0 0 l 50 50)\future(x)", text);
        Assert.Contains(@"\clippos(~+4,~-2)", text);
        Assert.Throws<ArgumentException>(() => MotionTrackApply.Plan(@"{\clip(0,0,20,20)\clippos(2,3)}text", points, [0, .04, .08], 0, 0, 80, 1, 1, applyClip: true));
        Assert.Throws<ArgumentException>(() => MotionTrackApply.Plan("text", points, [0, .04, .08], 0, 0, 80, 1, 1, applyClip: true));
    }
    [Fact]
    public void MultipleMotionPlansPublishAsOneUndoAndRejectStaleRows()
    {
        var document = AssDocument.Parse("[Events]\nFormat: Start, End, Text\nDialogue: 0:00:00.00,0:00:00.08,first\nDialogue: 0:00:00.00,0:00:00.08,second\n");
        var editor = new SubtitleEditor(document); var before = document.Serialize();
        var first = document.Events[0]; var second = document.Events[1];
        SubtitleEditor.MotionPlan[] stale = [new(first, "first", "tracked first"), new(second, "old", "tracked second")];
        Assert.Throws<InvalidOperationException>(() => editor.ApplyMotionPlans(stale));
        Assert.Equal(before, document.Serialize()); Assert.False(editor.Undo.CanUndo);
        SubtitleEditor.MotionPlan[] plans = [new(first, "first", "tracked first"), new(second, "second", "tracked second")];
        editor.ApplyMotionPlans(plans);
        Assert.Equal("tracked first", first.Text); Assert.Equal("tracked second", second.Text);
        editor.Undo.Undo(); Assert.Equal(before, document.Serialize()); Assert.False(editor.Undo.CanUndo);
        editor.Undo.Redo(); Assert.Equal("tracked first", first.Text); Assert.Equal("tracked second", second.Text);
    }
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void RealSyntheticFramesTrackTranslationInBothDirections(int direction)
    {
        var reference = Frame(0, 0);
        var tracker = new MotionPatternTracker(reference, 128, 96, 128 * 4, new(40, 40, 20, 20, 64, 64));
        var previous = new TrackingPoint(0, 40, 40);
        for (var frame = 1; frame <= 5; frame++)
        {
            var pixels = Frame(direction * frame * 2, direction * frame);
            previous = tracker.Track(pixels, 128, 96, 128 * 4, previous, frame);
            Assert.InRange(previous.X, 40 + direction * frame * 2 - .4, 40 + direction * frame * 2 + .4);
            Assert.InRange(previous.Y, 40 + direction * frame - .4, 40 + direction * frame + .4);
            Assert.True(previous.Confidence > .9);
        }
    }

    [Fact]
    public void FlatPatternIsRejectedAndMissingObjectFails()
    {
        Assert.Throws<ArgumentException>(() => new MotionPatternTracker(new byte[128 * 96 * 4], 128, 96, 512, new(40, 40, 20, 20, 64, 64)));
        var tracker = new MotionPatternTracker(Frame(0, 0), 128, 96, 512, new(40, 40, 20, 20, 64, 64));
        Assert.True(tracker.Track(new byte[128 * 96 * 4], 128, 96, 512, new(0, 40, 40), 1).Confidence < .65);
    }

    [Fact]
    public void ApplyComposesWithPlacementAndPreservesExactSourceThroughUndo()
    {
        const string source = "[Events]\r\nFormat: Start, End, Style, Name, Effect, Unknown, Text\r\n  Dialogue :\t0:00:00.00,0:00:00.08,Default,Actor,fx,opaque,{\\pos(100,200)\\future(x)\\bord3}Hello\\N{\\1c&H123456&}မြန်မာ\r\n";
        var editor = new SubtitleEditor(AssDocument.Parse(source)); var line = editor.Document.Events[0];
        var original = line.Text;
        TrackingPoint[] points = [new(0, 40, 40), new(1, 42, 41), new(2, 44, 42)];
        editor.ApplyMotion(line, original, points, [0, .04, .08], 1, 1, 1);
        Assert.Single(editor.Document.Events);
        Assert.StartsWith(@"{\pos(100,200)\future(x)\bord3}{[yoake motion position]\pos(~-2,~+1)", line.Text);
        Assert.Contains(@"\t(0,40,\pos(~+2,~-1))", line.Text);
        Assert.EndsWith(@"Hello\N{\1c&H123456&}မြန်မာ", line.Text);
        Assert.DoesNotContain(@"\frz", line.Text);
        Assert.Equal("opaque", line.Get("Unknown")); Assert.Equal("Actor", line.Actor); Assert.Equal("fx", line.Effect);
        var applied = editor.Document.Serialize();
        Assert.Equal("Apply motion tracking", editor.Undo.NextUndoName);
        editor.Undo.Undo(); Assert.Equal(source, editor.Document.Serialize()); Assert.False(editor.Undo.CanUndo);
        editor.Undo.Redo(); Assert.Equal(applied, editor.Document.Serialize());
    }

    [Fact]
    public void NearlyStaticSequenceAddsNoTransformsAndLinearMotionIsCompact()
    {
        var points = Enumerable.Range(0, 300).Select(i => new TrackingPoint(i, i * .1, i * .2)).ToArray();
        var times = Enumerable.Range(0, 300).Select(i => i * .04).ToArray();
        var text = MotionTrackApply.Plan(@"{\future(x)}text", points, times, 0, 0, 11960, 1, 1);
        Assert.Equal(1, AssSyntax.Tags(text).Count(tag => tag.Name == "t"));
        var stationary = points.Select(p => p with { X = .1 * Math.Sin(p.Frame), Y = .1 * Math.Cos(p.Frame) }).ToArray();
        Assert.Equal(@"{\future(x)}text", MotionTrackApply.Plan(@"{\future(x)}text", stationary, times, 0, 0, 11960, 1, 1));
    }

    [Theory]
    [InlineData(@"{\move(0,0,10,10)}text")]
    [InlineData(@"{\t(0,100,\pos(10,20))}text")]
    public void CompetingAuthoredMovementIsRejected(string source)
        => Assert.Throws<ArgumentException>(() => MotionTrackApply.Plan(source, [new(0, 0, 0), new(1, 5, 5)], [0, .04], 0, 0, 40, 1, 1));

    [Fact]
    public void CancellationDoesNotMutateDocumentOrHistory()
    {
        var document = AssDocument.Parse("[Events]\nFormat: Start, End, Text\nDialogue: 0:00:00.00,0:00:00.08,text\n");
        var editor = new SubtitleEditor(document); var original = document.Serialize();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var tracker = new MotionPatternTracker(Frame(0, 0), 128, 96, 512, new(40, 40, 20, 20, 64, 64));
        Assert.Throws<OperationCanceledException>(() => tracker.Track(Frame(2, 1), 128, 96, 512, new(0, 40, 40), 1, cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => editor.ApplyMotion(document.Events[0], "text", [new(0, 40, 40), new(1, 42, 41)], [0, .04], 0, 1, 1, cancellation.Token));
        Assert.Equal(original, document.Serialize()); Assert.False(editor.Undo.CanUndo);
    }

    [Fact]
    public void PreparedApplyRejectsStaleSourceAndCancellationBeforePublication()
    {
        var document = AssDocument.Parse("[Events]\nFormat: Start, End, Text\nDialogue: 0:00:00.00,0:00:00.08,text\n");
        var editor = new SubtitleEditor(document); var line = document.Events[0];
        var prepared = MotionTrackApply.Plan("text", [new(0, 40, 40), new(1, 42, 41)], [0, .04], 0, 0, 80, 1, 1);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => editor.ApplyMotionPlan(line, "text", prepared, cancellation.Token));
        Assert.Equal("text", line.Text); Assert.False(editor.Undo.CanUndo);
        editor.SetField(line, "Text", "new authoring edit", "Author edit");
        var changed = document.Serialize();
        Assert.Throws<InvalidOperationException>(() => editor.ApplyMotionPlan(line, "text", prepared));
        Assert.Equal(changed, document.Serialize()); Assert.Equal("Author edit", editor.Undo.NextUndoName);
    }

    private static byte[] Frame(int dx, int dy)
    {
        var pixels = new byte[128 * 96 * 4];
        for (var y = 0; y < 96; y++)
            for (var x = 0; x < 128; x++)
            {
                var u = x - dx - 40; var v = y - dy - 40;
                var value = Math.Abs(u) <= 13 && Math.Abs(v) <= 13 ? (byte)Math.Clamp(120 + 50 * Math.Sin(u * .51) + 45 * Math.Cos(v * .43) + 25 * Math.Sin((u + v) * .27), 0, 255) : (byte)15;
                var offset = (y * 128 + x) * 4;
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = value; pixels[offset + 3] = 255;
            }
        return pixels;
    }
}
