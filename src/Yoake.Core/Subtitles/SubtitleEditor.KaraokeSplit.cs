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
            // Static appearance state can be inherited. Time-relative effects,
            // drawings and unknown tag semantics still require explicit retiming.
            const string staticTags="an a pos org fn fs fscx fscy fsp fr frx fry frz fax fay fe b i u s c 1c 2c 3c 4c alpha 1a 2a 3a 4a bord xbord ybord shad xshad yshad blur be q r clip iclip furi furis furisx furisy furifsp furistyle furipos furiap furiplaceauto furichangepos vert vtype vdir vsp vcolsp";
            var allowed=staticTags.Split(' ').ToHashSet(StringComparer.Ordinal);
            if (AssSyntax.Tags(line.Text).Any(tag => tag.Name is not ("k" or "K" or "kf" or "ko" or "kO")&&!allowed.Contains(tag.Name)))
                throw new ArgumentException("Time-relative, drawing or unknown overrides cannot yet be split safely; the source was preserved.");
            var safe = StringInfo.ParseCombiningCharacters(karaoke.Text).Append(karaoke.Text.Length).ToHashSet();
            if (karaoke.Cuts.Any(cut => !safe.Contains(cut)))
                throw new ArgumentException("Karaoke crosses a grapheme boundary; the source was preserved.");
            var starts=karaoke.Insertions.ToArray();starts[0]=0;
            var groupStart=-1;var readingStart=-1;
            foreach(var token in AssSyntax.Tokenize(karaoke.Raw).Where(t=>t.Kind==AssSyntaxKind.Furigana))
                for(var p=token.Start;p<token.Start+token.Length;p++)
                {
                    if(karaoke.Raw[p]=='<') { groupStart=p;readingStart=-1; }
                    else if(karaoke.Raw[p]=='|'&&readingStart<0)readingStart=p+1;
                    else if(karaoke.Raw[p]=='>'&&groupStart>=0)
                    {
                        for(var i=1;i<starts.Length;i++)
                            if(starts[i]>groupStart&&starts[i]<=p)
                            {
                                if(starts[i]!=readingStart)throw new ArgumentException("Karaoke splits inside a furigana reading; the source was preserved.");
                                starts[i]=groupStart;
                            }
                        groupStart=-1;
                    }
                }
            var time = line.StartMilliseconds ?? throw new ArgumentException("Use valid start times before splitting karaoke.");
            var lineEnd=line.EndMilliseconds??throw new ArgumentException("Use valid end times before splitting karaoke.");
            if(lineEnd<time)throw new ArgumentException("End time must not precede start time.");
            var parts = new List<AssEvent>();
            for (var i = 0; i < karaoke.Tags.Length; i++)
            {
                var start = starts[i];
                var end = i + 1 < karaoke.Tags.Length ? starts[i + 1] : karaoke.Raw.Length;
                var insertion = karaoke.Insertions[i];
                if (start > insertion || insertion > end || end > karaoke.Raw.Length)
                    throw new ArgumentException("Karaoke boundaries cannot be split safely; the source was preserved.");
                var duration = karaoke.Durations[i];
                var next = i+1==karaoke.Tags.Length?lineEnd:Math.Min(checked(time + duration),lineEnd);
                var copy = line.Clone();
                copy.Start = AssTime.Format(time); copy.End = AssTime.Format(next);
                copy.Text = AssTextOperations.OverridesBefore(karaoke.Raw,start) + karaoke.Raw[start..insertion] + "{\\" + karaoke.Tags[i] +
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
