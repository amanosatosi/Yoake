using System.Globalization;
using System.Text;

namespace Yoake.Core.Automation;

// The 3.2.2 parse_karaoke_data view is independent of line-end normalization.
// This parser only reads ASS; it never rewrites the document or unknown tags.
public static class AutomationKaraokeParser
{
    public static IReadOnlyDictionary<string, object?> Parse(AutomationLine line)
    {
        if (line.String("class") != "dialogue")
            throw new ArgumentException("Subtitle line must be a dialogue line.");
        var source = line.String("text");
        Dictionary<string, object?> syllables = new(StringComparer.Ordinal)
        {
            ["0"] = Syllable(0, 0, "", "", "")
        };
        var full = new StringBuilder(); var stripped = new StringBuilder();
        var start = 0; var duration = 0; var tag = "\\k"; var drawing = 0;
        void Finish()
        {
            syllables[syllables.Count.ToString(CultureInfo.InvariantCulture)] =
                Syllable(start, duration, tag, full.ToString(), stripped.ToString());
            full.Clear(); stripped.Clear();
        }
        for (var cursor = 0; cursor < source.Length;)
        {
            var close = source[cursor] == '{' ? source.IndexOf('}', cursor + 1) : -1;
            if (close < 0)
            {
                // An unclosed brace is plain text in 3.2.2 (VSFilter behavior).
                var end = source.IndexOf('{', cursor + 1);
                if (end < 0) end = source.Length;
                var text = source.AsSpan(cursor, end - cursor);
                full.Append(text); if (drawing == 0) stripped.Append(text);
                cursor = end; continue;
            }
            var content = source.AsSpan(cursor + 1, close - cursor - 1);
            if (content.IndexOf('\\') < 0)
            {
                // Empty override blocks contribute nothing; comments retain braces.
                if (!content.IsEmpty) full.Append(source.AsSpan(cursor, close - cursor + 1));
                cursor = close + 1; continue;
            }
            var open = false;
            foreach (var segment in Segments(source, cursor + 1, close))
            {
                var value = source.AsSpan(segment.Start, segment.Length);
                var nameLength = KaraokeName(value);
                if (nameLength > 0)
                {
                    if (open) { full.Append('}'); open = false; }
                    // Zero-duration, empty syllables are skipped, carrying their tags.
                    if (duration > 0 || stripped.Length > 0) Finish();
                    tag = value[..nameLength].ToString();
                    if (tag == "\\K") tag = "\\kf";
                    start = unchecked(start + duration);
                    duration = unchecked(Parameter(value[nameLength..]) * 10);
                }
                else
                {
                    if (!open) { full.Append('{'); open = true; }
                    full.Append(value);
                }
                // Match the old prefix-based tag recognition. Mangetsu tags remain
                // opaque here; \pbo is not the drawing-level tag.
                if (value.StartsWith("\\p", StringComparison.Ordinal) &&
                    !value.StartsWith("\\pbo", StringComparison.Ordinal) &&
                    !value.StartsWith("\\pos", StringComparison.Ordinal))
                    drawing = Parameter(value[2..]);
            }
            if (open) full.Append('}');
            cursor = close + 1;
        }
        Finish();
        return syllables;
    }

    private static Dictionary<string, object?> Syllable(int start, int duration, string tag, string text, string stripped) =>
        new(StringComparer.Ordinal)
        {
            ["duration"] = duration, ["start_time"] = start,
            ["end_time"] = unchecked(start + duration), ["tag"] = tag,
            ["text"] = text, ["text_stripped"] = stripped
        };

    // Top-level backslashes only: transforms and other parenthesized arguments
    // are kept together, rather than split into false karaoke tags.
    private static IEnumerable<(int Start, int Length)> Segments(string text, int start, int end)
    {
        var depth = 0; var previous = start;
        for (var i = start + 1; i < end; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')') depth = Math.Max(0, depth - 1);
            else if (text[i] == '\\' && depth == 0)
            {
                yield return (previous, i - previous); previous = i;
            }
        }
        if (previous < end) yield return (previous, end - previous);
    }

    private static int KaraokeName(ReadOnlySpan<char> tag)
    {
        if (tag.StartsWith("\\kf", StringComparison.Ordinal) || tag.StartsWith("\\ko", StringComparison.Ordinal)) return 3;
        // 3.2.2 has no \kt prototype: it is parsed as \k with a nonnumeric
        // parameter, giving zero duration. Preserve that compatibility quirk.
        return tag.StartsWith("\\k", StringComparison.Ordinal) || tag.StartsWith("\\K", StringComparison.Ordinal) ? 2 : 0;
    }

    private static int Parameter(ReadOnlySpan<char> text)
    {
        text = text.Trim();
        if (!text.IsEmpty && text[0] == '(') text = text[1..].TrimStart();
        var sign = 1; var i = 0;
        if (!text.IsEmpty && text[0] is '+' or '-') { sign = text[0] == '-' ? -1 : 1; i++; }
        var result = 0;
        while (i < text.Length && char.IsAsciiDigit(text[i])) result = unchecked(result * 10 + text[i++] - '0');
        return unchecked(result * sign);
    }
}
