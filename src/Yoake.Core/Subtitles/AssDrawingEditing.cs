using System.Globalization;

namespace Yoake.Core.Subtitles;

public readonly record struct AssDrawingRun(int Start, int Length, int Scale)
{
    public string Replace(string source, AssVectorPath drawing) =>
        Replace(source, drawing.Serialize(Scale));

    public string Replace(string source, string drawing) =>
        source[..Start] + drawing + source[(Start + Length)..];
}

public static class AssDrawingEditing
{
    // Drawing placement on the canvas needs an isolated layout run. Mixed text
    // and separate drawing runs share layout advances with adjacent glyphs.
    public static (AssDrawingRun? Run, bool CanInsert) FindIsolatedRun(string source)
    {
        AssDrawingRun? run = null;
        var scale = 0;
        var cursor = 0;
        bool ReadContent(int end)
        {
            var content = source.AsSpan(cursor, end - cursor);
            var trimmed = content.Trim();
            if (trimmed.IsEmpty) return true;
            if (run is not null || scale is < 1 or > 30) return false;
            run = new(cursor + content.Length - content.TrimStart().Length, trimmed.Length, scale);
            return true;
        }

        foreach (var block in AssTextOperations.Blocks(source))
        {
            if (!block.Closed || !ReadContent(block.Start)) return (null, false);
            foreach (var tag in AssSyntax.Tags(source[block.Start..block.End]))
            {
                if (tag.Name == "r") scale = 0;
                if (tag.Name == "p" && !int.TryParse(
                    source.AsSpan(block.Start + tag.ValueStart, tag.End - tag.ValueStart),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out scale))
                    return (null, false);
            }
            cursor = block.End;
        }
        if (!ReadContent(source.Length)) return (null, false);
        return (run, run is null);
    }

    public static string Insert(string source, string drawing) =>
        source + @"{\p1}" + drawing + @"{\p0}";

    public static AssBox ControlBounds(AssVectorPath path, double coordinateFactor = 1, bool quantize = false)
    {
        var left = double.PositiveInfinity;
        var top = double.PositiveInfinity;
        var right = double.NegativeInfinity;
        var bottom = double.NegativeInfinity;
        foreach (var (_, point, _) in path.Handles())
        {
            var x = quantize ? Math.Round(point.X * coordinateFactor, MidpointRounding.AwayFromZero) / coordinateFactor : point.X;
            var y = quantize ? Math.Round(point.Y * coordinateFactor, MidpointRounding.AwayFromZero) / coordinateFactor : point.Y;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }
        return double.IsFinite(left) ? new(left, top, right - left, bottom - top) : default;
    }

    public static AssVectorPath Map(AssVectorPath path, Func<AssPoint, AssPoint> map)
    {
        var result = new AssVectorPath();
        foreach (var command in path.Commands)
            result.Commands.Add(new(command.Kind, command.Points.Select(map).ToArray()));
        return result;
    }

    private const double CircleControl = 0.2761423749;

    public static AssVectorPath CreatePreset(string name, AssBox box)
    {
        var path = new AssVectorPath();
        void Vertex(double x, double y) => path.Append(box.Map(new(x, y)), false);
        void Curve(double x1, double y1, double x2, double y2, double x, double y) =>
            path.Commands.Add(new('b', [box.Map(new(x1, y1)), box.Map(new(x2, y2)), box.Map(new(x, y))]));

        switch (name)
        {
            case "Ellipse":
                var k = CircleControl;
                Vertex(.5, 0);
                Curve(.5+k, 0, 1, .5-k, 1, .5);
                Curve(1, .5+k, .5+k, 1, .5, 1);
                Curve(.5-k, 1, 0, .5+k, 0, .5);
                Curve(0, .5-k, .5-k, 0, .5, 0);
                break;
            case "Rounded box":
                Vertex(.2, 0); Vertex(.8, 0);
                Curve(.91, 0, 1, .09, 1, .2); Vertex(1, .8);
                Curve(1, .91, .91, 1, .8, 1); Vertex(.2, 1);
                Curve(.09, 1, 0, .91, 0, .8); Vertex(0, .2);
                Curve(0, .09, .09, 0, .2, 0);
                break;
            case "Triangle":
                Vertex(.5, 0); Vertex(1, 1); Vertex(0, 1);
                break;
            case "Diamond":
                Vertex(.5, 0); Vertex(1, .5); Vertex(.5, 1); Vertex(0, .5);
                break;
            case "Star":
                for (var i = 0; i < 10; i++)
                {
                    var angle = i * Math.PI / 5 - Math.PI / 2;
                    var radius = i % 2 == 0 ? .5 : .22;
                    Vertex(.5 + radius * Math.Cos(angle), .5 + radius * Math.Sin(angle));
                }
                var bounds = ControlBounds(path);
                return Map(path, p => box.Map(bounds.Unmap(p)));
            case "Rectangle":
                Vertex(0, 0); Vertex(1, 0); Vertex(1, 1); Vertex(0, 1);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(name));
        }
        return path;
    }
}
