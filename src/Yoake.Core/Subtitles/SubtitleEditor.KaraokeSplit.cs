using System.Globalization;

namespace Yoake.Core.Subtitles;

public sealed partial class SubtitleEditor
{
    // Build every replacement before touching the document so invalid timing in
    // any selected line cannot leave a partially split selection.
    public IReadOnlyList<AssEvent> SplitByKaraoke(IEnumerable<AssEvent> selection)
    {
        var replacements = new List<(AssEvent Original, AssEvent[] Lines)>();
        foreach (var line in Ordered(selection))
        {
            var karaoke = KaraokeSource.Parse(line.Text);
            if (karaoke.Tags.Length < 2) continue;
            // Formatting and unknown overrides may affect later syllables and
            // transforms are relative to the original line start. Until those
            // effects can be carried across faithfully, keep the source intact.
            if (AssSyntax.Tags(line.Text).Any(tag => tag.Name is not ("k" or "K" or "kf" or "ko" or "kO")))
                throw new ArgumentException("This line contains additional overrides that cannot yet be split safely; the source was preserved.");
            // A split must not turn one grapheme or a Mangetsu ruby group into
            // invalid independent strings. Keep such source untouched.
            var safe = StringInfo.ParseCombiningCharacters(karaoke.Text).Append(karaoke.Text.Length).ToHashSet();
            if (karaoke.Cuts.Any(cut => !safe.Contains(cut)) || AssSyntax.Tokenize(karaoke.Raw).Any(token => token.Kind == AssSyntaxKind.Furigana))
                throw new ArgumentException("Karaoke crosses a grapheme or furigana group; the source was preserved.");
            var time = line.StartMilliseconds ?? throw new ArgumentException("Use valid start times before splitting karaoke.");
            var parts = new List<AssEvent>();
            for (var i = 0; i < karaoke.Tags.Length; i++)
            {
                var start = i == 0 ? 0 : karaoke.Insertions[i];
                var end = i + 1 < karaoke.Tags.Length ? karaoke.Insertions[i + 1] : karaoke.Raw.Length;
                var insertion = karaoke.Insertions[i];
                if (start > insertion || insertion > end || end > karaoke.Raw.Length)
                    throw new ArgumentException("Karaoke boundaries cannot be split safely; the source was preserved.");
                var duration = karaoke.Durations[i];
                var next = checked(time + duration);
                var copy = line.Clone();
                copy.Start = AssTime.Format(time); copy.End = AssTime.Format(next);
                copy.Text = karaoke.Raw[start..insertion] + "{\\" + karaoke.Tags[i] +
                    (duration / 10).ToString(CultureInfo.InvariantCulture) + "}" + karaoke.Raw[insertion..end];
                parts.Add(copy); time = next;
            }
            replacements.Add((line, parts.ToArray()));
        }
        if (replacements.Count == 0) return [];
        Structure("Split by karaoke", () =>
        {
            foreach (var replacement in replacements)
            {
                foreach (var line in replacement.Lines) Document.Insert(line, replacement.Original);
                Document.Remove(replacement.Original);
            }
        });
        return replacements.SelectMany(replacement => replacement.Lines).ToArray();
    }
}
