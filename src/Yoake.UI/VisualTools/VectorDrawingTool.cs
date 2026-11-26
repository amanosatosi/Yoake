using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Yoake.Core.Subtitles;
using Yoake.UI.ViewModels;
using static Yoake.Core.Subtitles.VisualGeometry;

namespace Yoake.UI.VisualTools;

public sealed class VectorDrawingTool : VisualTool
{
    private sealed record Source(AssDrawingRun? Run, AssVectorPath Path, string Serialized);

    private string? _cachedText;
    private Source? _cachedSource;
    private Source? _baseline;
    private AssDrawingProjection _mapping;
    private AssPoint _placement;
    private string _mode = "Select", _preset = "No preset";
    private readonly HashSet<int> _selected = [];
    private readonly List<AssPoint> _samples = [];
    private AssEvent? _selectionOwner;
    private int _hit;
    private bool _box;
    private Point _boxEnd;
    private HashSet<int> _boxBaseline = [];
    private AssBox _bounds;
    private AssPoint _pivot;
    private Point? _lastApplied;
    private KeyModifiers _lastModifiers;

    private Source? Read(string text)
    {
        if (_cachedText == text) return _cachedSource;
        _cachedText = text;
        var (run, insert) = AssDrawingEditing.FindIsolatedRun(text);
        var path = run is {} r ? AssVectorPath.Parse(text.Substring(r.Start, r.Length), r.Scale) :
            insert ? new AssVectorPath() : null;
        var scale = run?.Scale ?? 1;
        return _cachedSource = path is not null && ValidCoordinates(path, Math.ScaleB(1, scale - 1))
            ? new(run, path, path.Serialize(scale)) : null;
    }

    private static bool ValidCoordinates(AssVectorPath path, double factor) =>
        path.Handles().All(h => Math.Abs(h.Point.X * factor) < 33_554_432 &&
            Math.Abs(h.Point.Y * factor) < 33_554_432);

    private static bool CanMap(VisualLine line)
    {
        var t = line.Transform;
        if (t.ScaleX <= 0 || t.ScaleY <= 0 || Math.Abs(1 - t.ShearX * t.ShearY) < 1e-8) return false;

        foreach (var tag in AssVisualTags.Scan(line.Text))
        {
            if (tag.Name is "pbo" or "distort" or "z" or "ortho" or "scale" or "fsc" or "moves3" or "moves4" or "mover" or "movevc" or "ct" or "vert")
                return false;
            if (tag.Name == "pos" && AssVisualTags.Position(line.Text) is null ||
                tag.Name == "move" && AssVisualTags.Move(line.Text) is null ||
                tag.Name == "org" && AssVisualTags.Origin(line.Text) is null)
                return false;
            if (tag.Name is "pos" or "move" && tag.Arguments.Split(',').Any(arg => arg.TrimStart().StartsWith('+')))
                return false;
            if (tag.Name == "t" && AssVisualTags.Scan("{" + tag.Arguments + "}").Any(animated =>
                animated.Name is "fr" or "frz" or "frx" or "fry" or "fax" or "fay" or "fscx" or "fscy" or "pbo" or "z" or "distort" or "scale" or "fsc"))
                return false;
        }
        return true;
    }

    private static AssDrawingProjection GetMapping(VisualToolContext context, VisualLine line, AssBox bounds) =>
        new(line.Position, line.Origin, line.Transform, bounds, line.Alignment, context.Model.VisualLayoutScale);

    private static AssPoint Rotate(AssPoint p, double radians)
    {
        var (sin, cos) = Math.SinCos(radians);
        return new(p.X * cos - p.Y * sin, p.X * sin + p.Y * cos);
    }

    public override void Render(VisualCanvas canvas)
    {
        if (canvas.Context.Active is not {} line || Read(line.Text) is not {} source || !CanMap(line)) return;
        var bounds = AssDrawingEditing.ControlBounds(source.Path);
        var map = GetMapping(canvas.Context, line, bounds);
        Point Screen(AssPoint point) => canvas.Screen(map.Map(point));
        var mode = canvas.Context.Model.DrawingMode;
        if (source.Path.Commands.Count > 0)
        {
            // Reject non-renderable coordinates before passing them to Avalonia.
            if (source.Path.Handles().Any(h =>
                {
                    var p = Screen(h.Point);
                    return !(Math.Abs(p.X) < 1_000_000 && Math.Abs(p.Y) < 1_000_000);
                }))
                return;
            canvas.Path(VectorClipTool.Geometry(source.Path, Screen));
            if (mode is "Move" or "Resize" or "Rotate")
            {
                var a = map.Map(bounds.Map(new(0, 0)));
                var b = map.Map(bounds.Map(new(1, 0)));
                var c = map.Map(bounds.Map(new(1, 1)));
                var d = map.Map(bounds.Map(new(0, 1)));
                canvas.Line(a, b); canvas.Line(b, c); canvas.Line(c, d); canvas.Line(d, a);
                foreach (var point in new[] { a, b, c, d }) canvas.Handle(point);
                canvas.Handle(map.Map(bounds.Map(new(.5, .5))), HandleShape.Circle);
            }
            else
            {
                foreach (var curve in source.Path.Curves())
                    if (curve.Cubic)
                    {
                        canvas.ScreenLine(Screen(curve.Start), Screen(curve.Control1), Brushes.Gold, true);
                        canvas.ScreenLine(Screen(curve.Control2), Screen(curve.End), Brushes.Gold, true);
                    }
                foreach (var handle in source.Path.Handles())
                    canvas.Handle(map.Map(handle.Point), handle.Control ? HandleShape.Square : HandleShape.Circle,
                        ReferenceEquals(_selectionOwner, line.Line) && _selected.Contains(handle.Index), primary: !handle.Control);
                if (_baseline is null && canvas.Inside && canvas.Context.Model.DrawingPreset == "No preset" && mode is ("Line" or "Bicubic"))
                    canvas.Line(map.Map(source.Path.Commands[^1].Points[^1]), canvas.Pointer.Script, Brushes.Gold, true);
            }
        }

        if (_box) canvas.Rectangle(Anchor.Script, canvas.Context.Script(_boxEnd), false, false);
    }

    public override bool Press(VisualToolContext context, VisualPointer pointer)
    {
        if (context.Active is not {} line) return false;
        if (Read(line.Text) is not {} source || !CanMap(line))
        {
            context.Model.ReportUnsupportedDrawing();
            return false;
        }
        if (!ReferenceEquals(_selectionOwner, line.Line))
        {
            _selectionOwner = line.Line;
            _selected.Clear();
        }
        _baseline = source;
        _bounds = AssDrawingEditing.ControlBounds(source.Path);
        _placement = line.Position;
        if (source.Run is null && AssVisualTags.Position(line.Text) is null && AssVisualTags.Move(line.Text) is null)
        {
            _placement = pointer.Script;
            line = line with { Position = _placement, Origin = AssVisualTags.Origin(line.Text) ?? _placement };
        }
        _mapping = GetMapping(context, line, _bounds);
        _mode = context.Model.DrawingMode;
        _preset = context.Model.DrawingPreset;
        _lastApplied = null;
        var local = _mapping.Unmap(pointer.Script);
        if (!double.IsFinite(local.X) || !double.IsFinite(local.Y)) return false;
        _hit = -1;
        foreach (var handle in source.Path.Handles().Reverse())
            if (context.Near(_mapping.Map(handle.Point), pointer.Screen))
            { _hit = handle.Index; break; }

        if (_preset == "No preset")
        {
            if (_mode == "Select")
            {
                if (_hit < 0)
                {
                    Anchor = pointer;
                    _box = true;
                    _boxEnd = pointer.Screen;
                    _boxBaseline = pointer.Ctrl ? new(_selected) : [];
                    if (!pointer.Ctrl) _selected.Clear();
                    return true;
                }
                if (pointer.Ctrl && !_selected.Add(_hit)) { _selected.Remove(_hit); return false; }
                if (!pointer.Ctrl && !_selected.Contains(_hit)) { _selected.Clear(); _selected.Add(_hit); }
            }
            if (_mode == "Remove" && _hit < 0) return false;
            if ((_mode is "Move" or "Resize" or "Rotate") && source.Run is null) return false;
            if (_mode == "Resize")
            {
                if (_bounds.Width <= 0 || _bounds.Height <= 0) return false;
                var corners = new AssPoint[] { new(0, 0), new(1, 0), new(1, 1), new(0, 1) };
                var corner = Array.FindIndex(corners, p => context.Near(_mapping.Map(_bounds.Map(p)), pointer.Screen, 14));
                if (corner < 0) return false;
                var dragged = _bounds.Map(corners[corner]);
                _pivot = _bounds.Map(corners[(corner + 2) % 4]);
                _resizeStart = dragged;
            }
            if (_mode == "Rotate")
            {
                _pivot = _mapping.Map(_bounds.Map(new(.5, .5)));
                if (context.Near(_pivot, pointer.Screen, 2)) return false;
            }
        }

        (AssCurve Curve, double T, double Distance)? nearest = null;
        if (_preset == "No preset" && _mode is "Convert" or "Insert")
        {
            nearest = source.Path.Nearest(local);
            if (nearest is not {} segment || !context.Near(_mapping.Map(segment.Curve.At(segment.T)), pointer.Screen, 12))
                return false;
        }

        _samples.Clear();
        _samples.Add(local);
        if (!Begin(context, pointer, "Edit ASS drawing")) return false;
        if (nearest is {} edge)
        {
            var changed = source.Path.Copy();
            if (_mode == "Convert") changed.Convert(edge.Curve); else changed.Split(edge.Curve, edge.T);
            Apply(context, changed);
        }
        else if (_mode == "Remove" && _preset == "No preset")
        {
            var changed = source.Path.Copy();
            changed.Remove(_hit);
            Apply(context, changed);
            _selected.Clear();
        }
        else if (_preset == "No preset" && _mode is "Line" or "Bicubic" or "Contour") Move(context, pointer);
        return true;
    }

    private AssPoint _resizeStart;

    public override void Move(VisualToolContext context, VisualPointer pointer)
    {
        if (_box && _baseline is {} boxSource)
        {
            _boxEnd = pointer.Screen;
            var box = new Rect(Anchor.Screen, _boxEnd);
            _selected.Clear();
            _selected.UnionWith(_boxBaseline);
            foreach (var handle in boxSource.Path.Handles())
                if (box.Contains(context.Screen(_mapping.Map(handle.Point)))) _selected.Add(handle.Index);
            return;
        }
        if (Initial is not {} line || _baseline is not {} source) return;
        if (_lastApplied == pointer.Screen && _lastModifiers == pointer.Modifiers) return;
        _lastApplied = pointer.Screen;
        _lastModifiers = pointer.Modifiers;
        var delta = Subtract(pointer.Script, Anchor.Script);
        var point = _mapping.Unmap(pointer.Script);
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) return;
        if (_preset != "No preset")
        {
            var end = pointer.Script;
            if (pointer.Shift)
            {
                var side = Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y));
                end = Add(Anchor.Script, new(Math.CopySign(side, delta.X), Math.CopySign(side, delta.Y)));
            }
            var width = Math.Abs(end.X - Anchor.Script.X);
            var height = Math.Abs(end.Y - Anchor.Script.Y);
            if (width < 1 || height < 1) { context.Apply(l => l.Text); return; }
            var box = new AssBox(Math.Min(end.X, Anchor.Script.X), Math.Min(end.Y, Anchor.Script.Y), width, height);
            Apply(context, AssDrawingEditing.Map(AssDrawingEditing.CreatePreset(_preset, box), _mapping.Unmap));
            return;
        }

        switch (_mode)
        {
            case "Move":
                context.Apply(l => !ReferenceEquals(l.Line, line.Line) || delta == default ? l.Text :
                    AssVisualTags.ShiftPosition(l.Text, l.DefaultPosition, pointer.Shift ? Axis(delta) : delta));
                return;
            case "Resize":
                var start = Subtract(_resizeStart, _pivot);
                var end = Add(start, Subtract(point, _mapping.Unmap(Anchor.Script)));
                var sx = Math.Clamp(end.X / start.X, .01, 100);
                var sy = Math.Clamp(end.Y / start.Y, .01, 100);
                if (pointer.Shift) sx = sy = Math.Abs(sx - 1) >= Math.Abs(sy - 1) ? sx : sy;
                Apply(context, AssDrawingEditing.Map(source.Path,
                    p => Add(_pivot, new((p.X - _pivot.X) * sx, (p.Y - _pivot.Y) * sy))));
                return;
            case "Rotate":
                var angle = Angle(pointer.Script, _pivot) - Angle(Anchor.Script, _pivot);
                if (pointer.Ctrl) angle = Snap(angle, 15);
                Apply(context, AssDrawingEditing.Map(source.Path, p =>
                    _mapping.Unmap(Add(_pivot, Rotate(Subtract(_mapping.Map(p), _pivot), -angle * Math.PI / 180)))));
                return;
            case "Freehand":
            case "Smooth":
            case "Spline":
                if (Distance(_mapping.Map(_samples[^1]), pointer.Script) >= context.ScriptDistance(4))
                    _samples.Add(point);
                if (_samples.Count > 1) Apply(context, SampledPath());
                return;
            case "Select":
                var changed = source.Path.Copy();
                changed.Translate(_selected, Subtract(_mapping.Unmap(Add(Anchor.Script, Delta(pointer))), _mapping.Unmap(Anchor.Script)));
                Apply(context, changed);
                return;
            case "Contour":
                var contour = source.Path.Copy();
                contour.Commands.Add(new('m', [point]));
                Apply(context, contour);
                return;
            case "Line":
                var straight = source.Path.Copy();
                if (straight.Commands.Count == 0)
                {
                    var first = _mapping.Unmap(Anchor.Script);
                    straight.Append(first, false);
                    if (Distance(first, point) >= 1) straight.Append(point, false);
                }
                else straight.Append(point, false);
                Apply(context, straight);
                return;
            case "Bicubic":
                var curved = source.Path.Copy();
                // Click places the endpoint; dragging supplies its incoming
                // tangent instead of moving the endpoint. Consecutive cubic
                // segments reflect the previous handle for a smooth join.
                var endpoint = _mapping.Unmap(Anchor.Script);
                if (curved.Commands.Count == 0) curved.Append(endpoint, false);
                else
                {
                    var previous = curved.Commands[^1];
                    var from = previous.Points[^1];
                    var firstControl = previous.Kind == 'b'
                        ? Add(from, Subtract(from, previous.Points[1]))
                        : Lerp(from, endpoint, .25);
                    var secondControl = Distance(endpoint, point) >= 1
                        ? Subtract(endpoint, Subtract(point, endpoint))
                        : Lerp(from, endpoint, .75);
                    curved.Commands.Add(new('b', [firstControl, secondControl, endpoint]));
                }
                Apply(context, curved);
                return;
        }
    }

    private AssVectorPath SampledPath() => _mode == "Spline" ? AssVectorPath.Spline(_samples) : AssVectorPath.Freehand(_samples, _mode == "Smooth");

    public override void Release(VisualToolContext context, VisualPointer pointer)
    {
        Move(context, pointer);
        if (Initial is null || _preset != "No preset" || _mode is not ("Freehand" or "Smooth" or "Spline")) return;
        var point = _mapping.Unmap(pointer.Script);
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || Distance(_samples[^1], point) < .01) return;
        _samples.Add(point);
        Apply(context, SampledPath());
    }

    private void Apply(VisualToolContext context, AssVectorPath path)
    {
        if (Initial is not {} line || _baseline is not {} source) return;
        var scale = source.Run?.Scale ?? 1;
        var factor = Math.ScaleB(1, scale - 1);
        // Compare the bounds in the same integer drawing space as the renderer.
        // A uniform correction changes the drawing's local origin while keeping
        // its ASS position, move endpoints and rotation origin intact.
        if (!ValidCoordinates(path, factor)) return;
        var bounds = AssDrawingEditing.ControlBounds(path, factor, quantize: true);
        var corrected = _mapping.AlignmentCorrection(bounds, line.Alignment);
        if (corrected != default) path = AssDrawingEditing.Map(path, p => Add(p, corrected));
        if (corrected != default && !ValidCoordinates(path, factor)) return;

        var serialized = path.Serialize(scale);
        if (serialized == source.Serialized)
        {
            context.Apply(l => l.Text);
            return;
        }
        var edited = source.Run is {} run ? run.Replace(line.Text, serialized) : AssDrawingEditing.Insert(line.Text, serialized);
        if (source.Run is null && AssVisualTags.Move(line.Text) is null && AssVisualTags.Position(line.Text) is null)
            edited = AssVisualTags.SetPosition(edited, _placement);
        context.Apply(l => ReferenceEquals(l.Line, line.Line) ? edited : l.Text);
    }

    public override void Cancel()
    {
        base.Cancel();
        _baseline = null;
        _samples.Clear();
        _box = false;
    }
}
