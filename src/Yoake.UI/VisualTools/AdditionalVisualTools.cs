using Avalonia;
using Yoake.Core.Subtitles;
using Yoake.UI.ViewModels;
using static Yoake.Core.Subtitles.VisualGeometry;

namespace Yoake.UI.VisualTools;

public sealed class ShearTool : VisualTool
{
    public override void Render(VisualCanvas canvas)
    {
        if (canvas.Context.Active is not {} line) return;
        var extent = canvas.Context.ScriptDistance(44);
        AssPoint Project(double x, double y) =>
            VisualGeometry.Project(new(x * extent, y * extent), line.Position, line.Transform, canvas.Context.Model.VisualLayoutScale);
        for (var i = -1; i <= 1; i++)
        {
            canvas.Line(Project(i, -1), Project(i, 1));
            canvas.Line(Project(-1, i), Project(1, i));
        }
        canvas.Handle(Project(0, 0));
        canvas.Label(FormattableString.Invariant($"fax {line.Transform.ShearX:0.###}  fay {line.Transform.ShearY:0.###}"),
            canvas.Screen(line.Position) + new Vector(10, 10));
    }

    public override bool Press(VisualToolContext context, VisualPointer pointer) =>
        Begin(context, pointer, "Shear subtitles");

    public override void Move(VisualToolContext context, VisualPointer pointer)
    {
        if (Initial is null) return;
        var screen = pointer.Screen - Anchor.Screen;
        var delta = new AssPoint(screen.X / 100, -screen.Y / 100);
        if (pointer.Shift) delta = Axis(delta);
        if (pointer.Ctrl) delta = new(Snap(delta.X, .1), Snap(delta.Y, .1));
        context.Apply(line =>
        {
            var text = line.Text;
            if (delta.X != 0) text = AssVisualTags.SetScalar(text, "fax", line.Transform.ShearX + delta.X);
            if (delta.Y != 0) text = AssVisualTags.SetScalar(text, "fay", line.Transform.ShearY + delta.Y);
            return text;
        });
    }
}

public sealed class ShiftGeometryTool : VisualTool
{
    public override void Render(VisualCanvas canvas)
    {
        foreach (var line in canvas.Context.Lines)
        {
            canvas.Handle(line.Position, HandleShape.Square, primary: line.Active);
            if (line.Origin != line.Position)
            {
                canvas.Line(line.Position, line.Origin, dashed: true);
                canvas.Handle(line.Origin, HandleShape.Triangle, primary: line.Active);
            }
            if (AssVisualTags.Clip(line.Text) is { Rectangular: true } clip)
            {
                var transform = AssVisualTags.ClipTransform(line.Text, line.RelativeTime, line.Duration);
                canvas.Rectangle(transform.Map(clip.Points[0]), transform.Map(clip.Points[1]), clip.Inverse, shade: false);
            }
        }
    }

    public override bool Press(VisualToolContext context, VisualPointer pointer) =>
        Begin(context, pointer, "Shift subtitle geometry");

    public override void Move(VisualToolContext context, VisualPointer pointer)
    {
        if (Initial is null) return;
        var delta = Delta(pointer);
        context.Apply(line =>
        {
            if (delta == default) return line.Text;
            var shifted = AssVisualTags.ShiftPosition(line.Text, line.DefaultPosition, delta);
            return AssVisualTags.Clip(line.Text) is null ? shifted : AssVisualTags.TranslateClip(shifted, delta);
        });
    }
}

public sealed class NumericTagsTool : VisualTool
{
    private readonly Dictionary<AssEvent, (int Index, double Value)> _values = [];
    private string _tag = "bord", _operation = "Adjust";
    private double _activeValue;

    public override void Render(VisualCanvas canvas)
    {
        if (canvas.Context.Active is not {} line) return;
        var tag = canvas.Context.Model.VisualNumericTag;
        var value = AssVisualTags.VisualScalar(line.Text, line.Style, tag);
        var p = canvas.Screen(line.Position);
        canvas.Handle(line.Position);
        canvas.ScreenLine(p - new Vector(42, 0), p + new Vector(42, 0));
        canvas.Label(FormattableString.Invariant($"{tag} {value:0.###}"), p + new Vector(12, 10));
    }

    public override bool Press(VisualToolContext context, VisualPointer pointer)
    {
        _tag = context.Model.VisualNumericTag;
        _operation = context.Model.VisualTagOperation;
        if (!Begin(context, pointer, "Edit ASS " + _tag)) return false;
        _values.Clear();
        foreach (var line in context.Lines)
            _values.Add(line.Line, (_values.Count, AssVisualTags.VisualScalar(line.Text, line.Style, _tag)));
        _activeValue = _values[Initial!.Line].Value;
        return true;
    }

    public override void Move(VisualToolContext context, VisualPointer pointer)
    {
        if (Initial is null) return;
        var delta = (pointer.Screen.X - Anchor.Screen.X) * (pointer.Shift ? .02 : .2);
        if (pointer.Ctrl) delta = Snap(delta, 1);
        context.Apply(line =>
        {
            if (delta == 0 || !_values.TryGetValue(line.Line, out var original)) return line.Text;
            var value = _operation switch
            {
                "Set" => _activeValue + delta,
                "By line" => original.Value + delta * original.Index,
                "Reverse" => original.Value + delta * (_values.Count - original.Index - 1),
                _ => original.Value + delta
            };
            return value == original.Value ? line.Text : AssVisualTags.SetVisualScalar(line.Text, _tag, value);
        });
    }

    public override void Cancel()
    {
        base.Cancel();
        _values.Clear();
    }
}
