using System.Globalization;

namespace Yoake.Core.Subtitles;

public readonly record struct AssTagSpan(int Start, int Length, string Name, string Arguments);
public readonly record struct AssPoint(double X, double Y);
public sealed record AssClip(bool Inverse, int Scale, string Drawing, IReadOnlyList<AssPoint> Points, bool Rectangular);

public static partial class AssVisualTags
{
    // Scan override blocks, tracking balanced parentheses. A tag inside \t is
    // never mistaken for the static tag being edited. Source ranges are spliced.
    public static IReadOnlyList<AssTagSpan> Scan(string text)
    {
        var tags = new List<AssTagSpan>();
        foreach (var span in AssSyntax.Tags(text))
        {
            var args = text[span.ValueStart..span.End].TrimEnd();
            if (args.StartsWith('('))
            {
                if (!args.EndsWith(')')) continue;
                args = args[1..^1];
            }
            tags.Add(new(span.Start, span.End-span.Start, span.Name, args));
        }
        return tags;
    }
    private static string Number(double n) => n.ToString("0.###", CultureInfo.InvariantCulture);
    private static bool TryNumber(string text, out double n) => double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out n) && double.IsFinite(n);
    public static AssPoint? Position(string text)
    {
        var tag = Scan(text).FirstOrDefault(t => t.Name == "pos");
        if (tag.Length == 0) return null; var fields = tag.Arguments.Split(',');
        return fields.Length == 2 && TryNumber(fields[0], out var x) && TryNumber(fields[1], out var y) ? new(x, y) : null;
    }
    private static string Splice(string text, AssTagSpan tag, string replacement)
    {
        if (tag.Length > 0) return text[..tag.Start] + replacement + text[(tag.Start + tag.Length)..];
        return text.StartsWith('{') ? "{" + replacement + text[1..] : "{" + replacement + "}" + text;
    }
    public static int Alignment(string text,int fallback)
    {
        var tags=AssSyntax.Tags(text);var modern=tags.FirstOrDefault(t=>t.Name=="an");
        if(modern.Name is not null&&int.TryParse(text[modern.ValueStart..modern.End].Trim(),out var alignment)&&alignment is >=1 and <=9)return alignment;
        var legacy=tags.FirstOrDefault(t=>t.Name=="a");
        if(legacy.Name is not null&&int.TryParse(text[legacy.ValueStart..legacy.End].Trim(),out var value))
            return value switch{1=>1,2=>2,3=>3,5=>7,6=>8,7=>9,9=>4,10=>5,11=>6,_=>fallback};
        return fallback;
    }
    public static AssPoint? PositionAtTime(string text,long relativeMilliseconds,long durationMilliseconds)
    {
        if(Position(text) is {} position)return position;
        var move=Scan(text).FirstOrDefault(t=>t.Name=="move");if(move.Length==0)return null;
        var fields=move.Arguments.Split(',');if(fields.Length is not (4 or 6)||fields.Any(p=>!TryNumber(p,out _)))return null;
        var values=fields.Select(p=>double.Parse(p,CultureInfo.InvariantCulture)).ToArray();
        var start=fields.Length==6?values[4]:0;var end=fields.Length==6?values[5]:durationMilliseconds;
        if(start<=0&&end<=0){start=0;end=durationMilliseconds;}
        var fraction=relativeMilliseconds<=start?0:relativeMilliseconds>=end||end<=start?1:(relativeMilliseconds-start)/(end-start);
        return new(values[0]+(values[2]-values[0])*fraction,values[1]+(values[3]-values[1])*fraction);
    }
    public static string SetPosition(string text, AssPoint point)
    {
        var tags = Scan(text);
        if (tags.Any(t => t.Name == "move")) throw new InvalidOperationException("This line uses movement. Remove or edit its \\move tag before positioning it.");
        var position=tags.FirstOrDefault(t=>t.Name=="pos");
        if(position.Length>0&&(Position(text) is null||position.Arguments.Split(',').Any(p=>p.TrimStart().StartsWith('+'))))throw new InvalidOperationException("This position uses a Mangetsu expression or relative coordinates; edit that tag in the text panel.");
        return Splice(text, tags.FirstOrDefault(t => t.Name == "pos"), "\\pos(" + Number(point.X) + "," + Number(point.Y) + ")");
    }
    public static AssClip? Clip(string text)
    {
        var tag = Scan(text).LastOrDefault(t => t.Name is "clip" or "iclip"); if (tag.Length == 0) return null;
        var parts = tag.Arguments.Split(',');
        if (parts.Length == 4 && parts.All(p => TryNumber(p, out _)))
            return new(tag.Name == "iclip", 1, "", [new(double.Parse(parts[0], CultureInfo.InvariantCulture), double.Parse(parts[1], CultureInfo.InvariantCulture)), new(double.Parse(parts[2], CultureInfo.InvariantCulture), double.Parse(parts[3], CultureInfo.InvariantCulture))], true);
        var scale = 1; var drawing = tag.Arguments;
        if (parts.Length == 2) { if (!int.TryParse(parts[0].Trim(), out scale) || scale is < 1 or > 30) return null; drawing = parts[1]; }
        else if (parts.Length != 1) return null;
        var numbers = DrawingNumbers(drawing); if (numbers.Count == 0 || numbers.Count % 2 != 0) return null;
        var factor = Math.Pow(2, scale - 1); var points = new List<AssPoint>();
        for (var i = 0; i < numbers.Count; i += 2) points.Add(new(numbers[i].Value / factor, numbers[i + 1].Value / factor));
        return new(tag.Name == "iclip", scale, drawing, points, false);
    }
    private readonly record struct DrawingNumber(int Start, int Length, double Value);
    private static List<DrawingNumber> DrawingNumbers(string drawing)
    {
        var result = new List<DrawingNumber>();
        for (var i = 0; i < drawing.Length;)
        {
            if (!(char.IsAsciiDigit(drawing[i]) || drawing[i] is '-' or '+' or '.')) { i++; continue; }
            var start = i++; while (i < drawing.Length && (char.IsAsciiDigit(drawing[i]) || drawing[i] == '.')) i++;
            if (!TryNumber(drawing[start..i], out var value)) return [];
            result.Add(new(start, i - start, value));
        }
        return result;
    }
    public static string SetRectangle(string text, bool inverse, AssPoint a, AssPoint b)
    {
        var tag = Scan(text).LastOrDefault(t => t.Name is "clip" or "iclip");
        if(tag.Length>0&&Clip(text) is null)throw new InvalidOperationException("Unsupported clip syntax; edit the tag in the text panel.");
        return Splice(text, tag, (inverse ? "\\iclip(" : "\\clip(") + string.Join(',', Number(Math.Min(a.X, b.X)), Number(Math.Min(a.Y, b.Y)), Number(Math.Max(a.X, b.X)), Number(Math.Max(a.Y, b.Y))) + ")");
    }
    public static string TranslateClip(string text, AssPoint delta)
    {
        var clip=Clip(text)??throw new InvalidOperationException("Unsupported clip syntax; edit the tag in the text panel.");
        if(Scan(text).Any(t=>t.Name is "clippos" or "clips" || t.Name=="t"&&(t.Arguments.Contains("\\clippos",StringComparison.Ordinal)||t.Arguments.Contains("\\clips",StringComparison.Ordinal))))return TranslateClipOffset(text,delta);
        if(clip.Rectangular)return SetRectangle(text,clip.Inverse,new(clip.Points[0].X+delta.X,clip.Points[0].Y+delta.Y),new(clip.Points[1].X+delta.X,clip.Points[1].Y+delta.Y));
        var numbers=DrawingNumbers(clip.Drawing);var factor=Math.Pow(2,clip.Scale-1);var output=new System.Text.StringBuilder();var cursor=0;
        for(var i=0;i<numbers.Count;i++){var number=numbers[i];output.Append(clip.Drawing[cursor..number.Start]);output.Append(Number(number.Value+(i%2==0?delta.X:delta.Y)*factor));cursor=number.Start+number.Length;}
        output.Append(clip.Drawing[cursor..]);var tag=Scan(text).Last(t=>t.Name is "clip" or "iclip");var comma=tag.Arguments.IndexOf(',');var prefix=comma>=0?tag.Arguments[..(comma+1)]:"";
        return Splice(text,tag,"\\"+tag.Name+"("+prefix+output+")");
    }
    public static string MoveClipPoint(string text, int pointIndex, AssPoint point)
    {
        var clip = Clip(text) ?? throw new InvalidOperationException("No editable clip.");
        if (clip.Rectangular) return SetRectangle(text, clip.Inverse, pointIndex == 0 ? point : clip.Points[0], pointIndex == 1 ? point : clip.Points[1]);
        var numbers = DrawingNumbers(clip.Drawing); var factor = Math.Pow(2, clip.Scale - 1);
        if (pointIndex < 0 || pointIndex >= clip.Points.Count) throw new ArgumentOutOfRangeException(nameof(pointIndex));
        var x = numbers[pointIndex * 2]; var y = numbers[pointIndex * 2 + 1];
        var drawing = clip.Drawing[..x.Start] + Number(point.X * factor) + clip.Drawing[(x.Start + x.Length)..y.Start] + Number(point.Y * factor) + clip.Drawing[(y.Start + y.Length)..];
        var tag = Scan(text).Last(t => t.Name is "clip" or "iclip");
        // Preserve whether scale was explicitly written, even for scale 1.
        var comma = tag.Arguments.IndexOf(','); var prefix = comma >= 0 ? tag.Arguments[..(comma + 1)] : "";
        return Splice(text, tag, "\\" + tag.Name + "(" + prefix + drawing + ")");
    }
}
