using System.Text.RegularExpressions;

namespace Yoake.Core.Subtitles;

public sealed record SubtitleSearchOptions(string Query, bool MatchCase = false, bool RegularExpression = false);
public sealed record SubtitleSearchMatch(AssEvent Line, int Index, int Length);

public sealed class SubtitleSearch
{
    private readonly Regex _regex;
    private readonly bool _regularExpression;
    public SubtitleSearch(SubtitleSearchOptions options)
    {
        if (string.IsNullOrEmpty(options.Query)) throw new ArgumentException("Enter search text.");
        _regularExpression=options.RegularExpression;
        _regex = new Regex(options.RegularExpression ? options.Query : Regex.Escape(options.Query), options.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
    }
    public SubtitleSearchMatch? Find(IReadOnlyList<AssEvent> lines, AssEvent? current, int offset, bool backwards, CancellationToken token=default)
    {
        var matches=new List<(SubtitleSearchMatch Match,int Row)>();var currentRow=-1;
        for(var row=0;row<lines.Count;row++)
        {
            token.ThrowIfCancellationRequested();var line=lines[row];if(line==current)currentRow=row;
            foreach(Match match in _regex.Matches(line.Text))matches.Add((new(line,match.Index,match.Length),row));
        }
        if(matches.Count==0)return null;
        if(backwards)
        {
            for(var i=matches.Count-1;i>=0;i--)if(matches[i].Row<currentRow||matches[i].Row==currentRow&&matches[i].Match.Index<offset)return matches[i].Match;
            return matches[^1].Match;
        }
        foreach(var item in matches)if(item.Row>currentRow||item.Row==currentRow&&item.Match.Index>=offset)return item.Match;
        return matches[0].Match;
    }
    public void Replace(SubtitleEditor editor, SubtitleSearchMatch match, string replacement)
    {
        var found = _regex.Match(match.Line.Text, match.Index);
        if (!found.Success || found.Index != match.Index || found.Length != match.Length) throw new InvalidOperationException("Search result changed; find again.");
        editor.SetField(match.Line, "Text", match.Line.Text[..match.Index] + (_regularExpression?found.Result(replacement):replacement) + match.Line.Text[(match.Index + match.Length)..], "Replace subtitle text");
    }
    public sealed record Replacement(AssEvent Line,string Original,string Text);
    public Replacement[] PrepareReplacements(IEnumerable<AssEvent> lines,string replacement,CancellationToken token=default)
    {
        var edits=new List<Replacement>();
        foreach(var line in lines){token.ThrowIfCancellationRequested();var original=line.Text;var text=_regularExpression?_regex.Replace(original,replacement):_regex.Replace(original,_=>replacement);if(text!=original)edits.Add(new(line,original,text));}
        return edits.ToArray();
    }
    public static int ApplyReplacements(SubtitleEditor editor,IReadOnlyList<Replacement> edits)
    {
        if(edits.Any(e=>e.Line.Text!=e.Original))throw new InvalidOperationException("Subtitle text changed during search. Search again.");
        using var transaction=editor.Undo.BeginTransaction("Replace all subtitle text");
        foreach(var edit in edits)editor.SetField(edit.Line,"Text",edit.Text,"Replace subtitle text");
        transaction.Commit();return edits.Count;
    }
    public int ReplaceAll(SubtitleEditor editor,IEnumerable<AssEvent> lines,string replacement)=>ApplyReplacements(editor,PrepareReplacements(lines,replacement));
}
