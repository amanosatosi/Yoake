namespace Yoake.Core.Subtitles;

public static partial class AssVisualTags
{
    public static IReadOnlyList<string> NumericVisualTags { get; } = Array.AsReadOnly(new[]
    {
        "bord", "xbord", "ybord", "shad", "xshad", "yshad", "blur", "be",
        "fs", "fsp", "fscx", "fscy", "frz", "frx", "fry", "fax", "fay", "pbo"
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
        if (!NumericVisualTags.Contains(name)) throw new ArgumentOutOfRangeException(nameof(name));
        value = name switch
        {
            "fs" => Math.Max(1, value),
            "bord" or "xbord" or "ybord" or "shad" or "blur" or "fscx" or "fscy" => Math.Max(0, value),
            "be" => Math.Round(Math.Clamp(value, 0, 127)),
            _ => value
        };
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
}
