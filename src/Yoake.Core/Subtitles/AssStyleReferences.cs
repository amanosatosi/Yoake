using System.Text;

namespace Yoake.Core.Subtitles;

public static class AssStyleReferences
{
    private static IEnumerable<(int Start, int Length)> Resets(string text, string name)
    {
        foreach (var tag in AssSyntax.Tags(text))
        {
            // Only complete, top-level override blocks. Transforms and literal
            // text are intentionally opaque to style-reference edits.
            if (tag.Name != "r" || tag.BlockEnd >= text.Length) continue;
            var value = text.AsSpan(tag.ValueStart, tag.End - tag.ValueStart);
            var trimmed = value.Trim();
            if (!trimmed.Equals(name, StringComparison.Ordinal)) continue;
            var leading = value.Length - value.TrimStart().Length;
            yield return (tag.ValueStart + leading, trimmed.Length);
        }
    }
    public static bool Uses(string text, string name) => Resets(text, name).Any();
    public static string Replace(string text, string oldName, string newName)
    {
        var spans = Resets(text, oldName).ToArray();
        if (spans.Length == 0) return text;
        var result = new StringBuilder(text);
        foreach (var span in spans.Reverse()) result.Remove(span.Start, span.Length).Insert(span.Start, newName);
        return result.ToString();
    }
}
