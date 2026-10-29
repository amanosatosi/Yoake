using System.Globalization;
using System.Text;
using Yoake.Core.Media;

namespace Yoake.Core.Subtitles;

public static class MotionTrackApply
{
    private readonly record struct Sample(long Time, double X, double Y, double Scale, double Rotation);
    public static string Plan(string source, IReadOnlyList<TrackingPoint> points, IReadOnlyList<double> frameTimes,
        int referenceFrame, long start, long end, double scaleX, double scaleY, CancellationToken token = default,
        bool applyScale = false, bool applyRotation = false, bool applyClip = false,
        bool applyX = true, bool applyY = true, bool optimize = true)
    {
        token.ThrowIfCancellationRequested();
        if (applyClip && applyRotation) throw new ArgumentException("Mangetsu clip tracking does not support rotation. Choose movement or movement and size.");
        if (end <= start || !double.IsFinite(scaleX) || !double.IsFinite(scaleY) || scaleX <= 0 || scaleY <= 0) throw new ArgumentException("Invalid subtitle timing or resolution.");
        var ordered = points.OrderBy(p => p.Frame).ToArray();
        if (ordered.Length < 2 || !ordered.Any(p => p.Frame == referenceFrame)) throw new ArgumentException("Track the reference frame and at least one adjacent frame first.");
        for (var i = 0; i < ordered.Length; i++)
        {
            var p = ordered[i];
            if (p.Frame < 0 || p.Frame >= frameTimes.Count || !double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Confidence) || p.Confidence < .65 ||
                !double.IsFinite(p.Scale) || p.Scale <= 0 || !double.IsFinite(p.Rotation) ||
                !double.IsFinite(frameTimes[p.Frame]) || i > 0 && (p.Frame != ordered[i - 1].Frame + 1 || frameTimes[p.Frame] <= frameTimes[ordered[i - 1].Frame]))
                throw new ArgumentException("The track has a missing or failed frame. Correct it and restart that section.");
        }
        var reference = ordered.Single(p => p.Frame == referenceFrame);
        var rotations = new double[ordered.Length];
        rotations[0] = ordered[0].Rotation;
        for (var i = 1; i < ordered.Length; i++)
            rotations[i] = rotations[i - 1] + Math.IEEERemainder(ordered[i].Rotation - ordered[i - 1].Rotation, 360);
        var referenceRotation = rotations[Array.FindIndex(ordered, p => p.Frame == referenceFrame)];
        List<Sample> samples = [];
        for (var index = 0; index < ordered.Length; index++)
        {
            var p = ordered[index];
            token.ThrowIfCancellationRequested();
            var time = (long)Math.Round(frameTimes[p.Frame] * 1000) - start;
            samples.Add(new(time, applyX ? (p.X - reference.X) * scaleX : 0, applyY ? (p.Y - reference.Y) * scaleY : 0,
                100 * (p.Scale / reference.Scale - 1), -(rotations[index] - referenceRotation)));
        }
        var duration = end - start;
        Sample At(long time)
        {
            if (time <= samples[0].Time) return samples[0] with { Time = time };
            for (var i = 1; i < samples.Count; i++)
                if (time <= samples[i].Time)
                {
                    var a = samples[i - 1]; var b = samples[i]; var t = (time - a.Time) / (double)(b.Time - a.Time);
                    return new(time, a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t,
                        a.Scale + (b.Scale - a.Scale) * t, a.Rotation + (b.Rotation - a.Rotation) * t);
                }
            return samples[^1] with { Time = time };
        }
        var bounded = new[] { At(0) }.Concat(samples.Where(s => s.Time > 0 && s.Time < duration)).Append(At(duration)).ToArray();
        if (bounded.All(s => s.X * s.X + s.Y * s.Y <= .35 * .35 && (!applyScale || Math.Abs(s.Scale) < .0000005) && (!applyRotation || Math.Abs(s.Rotation) < .0000005))) return source;
        // Split at the exact reference sample: fitting must never move its anchor.
        var anchor = Array.FindIndex(bounded, s => s.Time == (long)Math.Round(frameTimes[referenceFrame] * 1000) - start);
        var keep = new SortedSet<int> { 0, bounded.Length - 1 };
        if (anchor >= 0) keep.Add(anchor);
        if (!optimize || applyScale || applyRotation) for (var i = 0; i < bounded.Length; i++) keep.Add(i);
        var ranges = new Stack<(int First, int Last)>();
        var seeds = keep.ToArray();
        for (var i = 1; i < seeds.Length; i++) ranges.Push((seeds[i - 1], seeds[i]));
        while (ranges.TryPop(out var range))
        {
            token.ThrowIfCancellationRequested();
            var a = bounded[range.First]; var b = bounded[range.Last]; var worst = .35; var split = -1;
            for (var i = range.First + 1; i < range.Last; i++)
            {
                var t = (bounded[i].Time - a.Time) / (double)(b.Time - a.Time);
                var dx = bounded[i].X - a.X - (b.X - a.X) * t; var dy = bounded[i].Y - a.Y - (b.Y - a.Y) * t;
                var error = Math.Sqrt(dx * dx + dy * dy);
                if (error > worst) { worst = error; split = i; }
            }
            if (split >= 0) { keep.Add(split); ranges.Push((range.First, split)); ranges.Push((split, range.Last)); }
        }
        var payload = new StringBuilder();
        payload.Append(Position(bounded[0].X, bounded[0].Y, applyClip));
        if (applyScale) payload.Append(Scalar(applyClip ? "clips" : "scale", bounded[0].Scale));
        if (applyRotation) payload.Append(Scalar("frz", bounded[0].Rotation));
        var indices = keep.ToArray();
        for (var i = 1; i < indices.Length; i++)
        {
            var a = bounded[indices[i - 1]]; var b = bounded[indices[i]];
            var movement = Position(b.X - a.X, b.Y - a.Y, applyClip);
            if (applyScale) movement += Scalar(applyClip ? "clips" : "scale", b.Scale - a.Scale);
            if (applyRotation) movement += Scalar("frz", b.Rotation - a.Rotation);
            if (movement.Length > 0) payload.Append(CultureInfo.InvariantCulture, $@"\t({a.Time},{b.Time},{movement})");
        }
        if (payload.Length == 0) return source;
        var insertion = 0;
        foreach (var block in AssTextOperations.Blocks(source))
        {
            if (block.Start != insertion || !block.Closed) break;
            insertion = block.End;
        }
        var clipCount = 0;
        foreach (var span in AssSyntax.Tags(source))
        {
            var name = span.Name;
            if (applyClip && (name is "clip" or "iclip"))
            {
                if (++clipCount > 1 || span.Start >= insertion)
                    throw new ArgumentException("Clip tracking requires one static clip in the leading override blocks.");
            }
            if (!applyClip && (applyX || applyY) && (name is "move" or "mover" or "moves3" or "moves4" || name == "pos" && span.Start >= insertion ||
                name == "t" && ContainsPositionTag("{" + source[span.ValueStart..span.End] + "}")))
                throw new ArgumentException("Tracked position requires static authored placement. Remove authored movement or transformed/inline position tags first.");
            if (applyClip && name == "t" && ContainsClipTag("{" + source[span.ValueStart..span.End] + "}"))
                throw new ArgumentException("Animated clip geometry cannot be combined safely with clip tracking.");
        }
        if (applyScale || applyRotation || applyClip)
        {
            var hasClip = false;
            foreach (var item in AssSyntax.Tokenize(source))
            {
                if (item.Kind is not (AssSyntaxKind.Tag or AssSyntaxKind.MangetsuTag)) continue;
                var name = source.Substring(item.Start, item.Length);
                if (name is @"\clip" or @"\iclip") hasClip = true;
                if (name == @"\r" || (applyScale && name == (applyClip ? @"\clips" : @"\scale")) ||
                    (applyRotation && (name is @"\fr" or @"\frz")) || (applyClip && (name is @"\clippos" or @"\movevc")))
                    throw new ArgumentException("Tracking cannot safely compose with this line's authored scale, rotation, clip transform or style reset.");
            }
            if (applyClip && !hasClip) throw new ArgumentException("The subtitle has no clip to track.");
        }
        token.ThrowIfCancellationRequested();
        return source[..insertion] + "{[yoake motion position]" + payload + "}" + source[insertion..];
    }
    private static bool ContainsPositionTag(string text) => AssSyntax.Tokenize(text).Any(token =>
        (token.Kind is AssSyntaxKind.Tag or AssSyntaxKind.MangetsuTag) &&
        text.Substring(token.Start, token.Length) is @"\pos" or @"\move" or @"\mover" or @"\moves3" or @"\moves4");
    private static bool ContainsClipTag(string text) => AssSyntax.Tokenize(text).Any(token =>
        (token.Kind is AssSyntaxKind.Tag or AssSyntaxKind.MangetsuTag) &&
        (text.Substring(token.Start, token.Length) is @"\clip" or @"\iclip"));
    private static string Position(double x, double y, bool clip) => Math.Abs(x) < .0000005 && Math.Abs(y) < .0000005 ? "" : $@"\{(clip ? "clippos" : "pos")}({Relative(x)},{Relative(-y)})";
    private static string Scalar(string tag, double value) => Math.Abs(value) < .0000005 ? "" : $"\\{tag}{Relative(value)}";
    private static string Relative(double value) => (value < 0 ? "~-" : "~+") + Math.Abs(value).ToString("0.######", CultureInfo.InvariantCulture);
}

public sealed partial class SubtitleEditor
{
    public readonly record struct MotionPlan(AssEvent Line, string ExpectedSource, string Text);
    public void ApplyMotion(AssEvent line, string expectedSource, IReadOnlyList<TrackingPoint> points, IReadOnlyList<double> frameTimes,
        int referenceFrame, double scaleX, double scaleY, CancellationToken token = default)
    {
        Require(line); token.ThrowIfCancellationRequested();
        if (line.Text != expectedSource) throw new InvalidOperationException("The subtitle changed while tracking. Reopen motion tracking for this line.");
        var text = MotionTrackApply.Plan(line.Text, points, frameTimes, referenceFrame,
            line.StartMilliseconds ?? 0, line.EndMilliseconds ?? 0, scaleX, scaleY, token);
        ApplyMotionPlan(line, expectedSource, text, token);
    }
    // Planning can run on a worker. Publication alone enters the document's
    // existing undo boundary on its owning UI thread.
    public void ApplyMotionPlan(AssEvent line, string expectedSource, string text, CancellationToken token = default)
        => ApplyMotionPlans([new(line, expectedSource, text)], token);

    public void ApplyMotionPlans(IReadOnlyList<MotionPlan> plans, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var unique = new HashSet<AssEvent>();
        foreach (var plan in plans)
        {
            Require(plan.Line);
            if (!unique.Add(plan.Line) || plan.Line.Text != plan.ExpectedSource)
                throw new InvalidOperationException("The subtitle selection changed while preparing motion. Reopen motion tracking before applying.");
        }
        using var transaction = Undo.BeginTransaction("Apply motion tracking");
        foreach (var plan in plans)
        {
            token.ThrowIfCancellationRequested();
            SetField(plan.Line, "Text", plan.Text, "Apply motion tracking");
        }
        token.ThrowIfCancellationRequested(); transaction.Commit();
    }
}
