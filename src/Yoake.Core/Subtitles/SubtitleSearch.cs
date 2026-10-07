using System.Text.RegularExpressions;

namespace Yoake.Core.Subtitles;

public sealed record SubtitleSearchOptions(string Query, bool MatchCase = false, bool RegularExpression = false);
public sealed record SubtitleSearchMatch(AssEvent Line, int Index, int Length);

public sealed class SubtitleSearch
{
    private readonly Regex _regex;
    public SubtitleSearch(SubtitleSearchOptions options)
    {
        if (string.IsNullOrEmpty(options.Query)) throw new ArgumentException("Enter search text.");
        _regex = new Regex(options.RegularExpression ? options.Query : Regex.Escape(options.Query), options.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
    }
    public SubtitleSearchMatch? Find(IReadOnlyList<AssEvent> lines, AssEvent? current, int offset, bool backwards)
    {
        var matches = lines.SelectMany(l => _regex.Matches(l.Text).Cast<Match>().Select(m => new SubtitleSearchMatch(l, m.Index, m.Length))).ToArray();
        if (matches.Length == 0) return null;
        var row = current is null ? -1 : lines.ToList().IndexOf(current);
        var match = backwards ? matches.LastOrDefault(m => lines.ToList().IndexOf(m.Line) < row || m.Line == current && m.Index < offset) : matches.FirstOrDefault(m => lines.ToList().IndexOf(m.Line) > row || m.Line == current && m.Index >= offset);
        return match ?? (backwards ? matches[^1] : matches[0]);
    }
    public void Replace(SubtitleEditor editor, SubtitleSearchMatch match, string replacement)
    {
        var found = _regex.Match(match.Line.Text, match.Index);
        if (!found.Success || found.Index != match.Index || found.Length != match.Length) throw new InvalidOperationException("Search result changed; find again.");
        editor.SetField(match.Line, "Text", match.Line.Text[..match.Index] + found.Result(replacement) + match.Line.Text[(match.Index + match.Length)..], "Replace subtitle text");
    }
    public int ReplaceAll(SubtitleEditor editor, IEnumerable<AssEvent> lines, string replacement)
    {
        // Compute all results before changing anything; invalid replacements and
        // regex timeouts cannot leave half of a document changed.
        var edits = lines.Select(l => (Line: l, Text: _regex.Replace(l.Text, replacement))).Where(p => p.Text != p.Line.Text).ToArray();
        using var transaction = editor.Undo.BeginTransaction("Replace all subtitle text");
        foreach (var edit in edits) editor.SetField(edit.Line, "Text", edit.Text, "Replace subtitle text");
        transaction.Commit(); return edits.Length;
    }
}
