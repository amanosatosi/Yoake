using System.Text;

namespace Yoake.Core.Subtitles;

public static partial class AssVisualTags
{
    public static IReadOnlyList<string> NumericVisualTags { get; } = Array.AsReadOnly(new[]
    {
        "bord", "xbord", "ybord", "shad", "xshad", "yshad", "blur", "be",
        "fs", "fsp", "fscx", "fscy", "frz", "frx", "fry", "fax", "fay", "pbo", "xblur", "yblur", "frs", "z"
    });

    public static double VisualScalar(string text, AssStyle? style, string name)
    {
        var field = name switch
        {
            "bord" or "xbord" or "ybord" => "Outline",
            "shad" or "xshad" or "yshad" => "Shadow",
            "fs" => "Fontsize",
            "fsp" => "Spacing",
            "fscx" => "ScaleX", "fscy" => "ScaleY", "frz" => "Angle",
            _ => ""
        };
        var fallback = field.Length > 0 && TryNumber(style?.Get(field) ?? "", out var value) ? value :
            name switch { "fs" => 20, "fscx" or "fscy" => 100, _ => 0 };
        // A later uniform border/shadow overrides an earlier axis-specific one.
        var uniform = name switch { "xbord" or "ybord" => "bord", "xshad" or "yshad" => "shad", _ => name };
        if (uniform == name) return Scalar(text, name, fallback);

        var result = fallback;
        foreach (var tag in InitialTags(text))
        {
            if (tag.Name == "r") result = fallback;
            else if ((tag.Name == name || tag.Name == uniform) &&
                TryNumber(text[tag.ValueStart..tag.End], out var number)) result = number;
        }
        return result;
    }

    public static string SetVisualScalar(string text, string name, double value)
    {
        value = NormalizeVisualScalar(name, value);
        var uniform = name switch { "xbord" or "ybord" => "bord", "xshad" or "yshad" => "shad", _ => name };
        if (uniform != name)
        {
            var tags = InitialTags(text).ToArray();
            var last = tags.LastOrDefault(t => t.Name == name || t.Name == uniform);
            if (last.Name == uniform)
                return text[..last.End] + "\\" + name + Number(value) + text[last.End..];
        }
        return SetScalar(text, name, value);
    }
    internal static double NormalizeVisualScalar(string name, double value)
    {
        if (!NumericVisualTags.Contains(name)) throw new ArgumentOutOfRangeException(nameof(name));
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        return name switch
        {
            "fs" => Math.Max(1, value),
            "bord" or "xbord" or "ybord" or "shad" or "blur" or "xblur" or "yblur" or "fscx" or "fscy" => Math.Max(0, value),
            "be" => Math.Round(Math.Clamp(value, 0, 127)),
            _ => value
        };
    }
}

public sealed class AssNumericTextGradient
{
    private readonly string _source, _tag;
    private readonly int[] _positions;
    private readonly double _value;

    public AssNumericTextGradient(string source, string tag, double value)
    {
        _source = source;
        _tag = tag;
        _value = value;
        _positions = new SubtitleSearchProjection(source, visibleText: true).GraphemeSourceIndices().ToArray();
    }

    public string Apply(double delta, bool reverse)
    {
        if (delta == 0 || _positions.Length < 2) return _source;
        var output = new StringBuilder(_source.Length);
        var cursor = 0;
        for (var index = 0; index < _positions.Length; index++)
        {
            var position = _positions[index];
            var step = reverse ? _positions.Length - index - 1 : index;
            var value = AssVisualTags.NormalizeVisualScalar(_tag, _value + delta * step);
            output.Append(_source, cursor, position - cursor);
            output.Append("{\\").Append(_tag).Append(value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append('}');
            cursor = position;
        }
        return output.Append(_source, cursor, _source.Length - cursor).ToString();
    }
}
