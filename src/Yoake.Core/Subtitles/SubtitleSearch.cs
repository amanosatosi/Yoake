using System.Text.RegularExpressions;

namespace Yoake.Core.Subtitles;

public enum SubtitleSearchField { Text, Style, Actor, Effect }
public sealed record SubtitleSearchOptions(string Query,bool MatchCase=false,bool RegularExpression=false,
    bool SkipTags=false,bool IgnoreComments=false,bool WholeField=false,SubtitleSearchField Field=SubtitleSearchField.Text);
public sealed record SubtitleSearchMatch(AssEvent Line,int Index,int Length)
{
    public string? OriginalSource {get;init;}
    public string FieldName {get;init;}="Text";
}
public sealed record SubtitleSearchInput(AssEvent Line,string Source,string FieldName,bool Comment);

public sealed class SubtitleSearch
{
    private readonly Regex _regex;
    private readonly SubtitleSearchOptions _options;
    private sealed record Candidate(SubtitleSearchProjection Projection,Match Match,SubtitleSearchMatch Result);
    public SubtitleSearch(SubtitleSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if(string.IsNullOrEmpty(options.Query))throw new ArgumentException("Enter search text.");
        if(options.Query.Length>16384)throw new ArgumentException("Search text is too long.");
        if(!Enum.IsDefined(options.Field))throw new ArgumentException("Unknown search field.");
        _options=options;
        try
        {
            _regex=new(options.RegularExpression?options.Query:Regex.Escape(options.Query),
                RegexOptions.CultureInvariant|(options.MatchCase?RegexOptions.None:RegexOptions.IgnoreCase),TimeSpan.FromMilliseconds(250));
        }
        catch(RegexParseException exception)
        {
            // Search owns the validation contract; callers must not need to
            // understand a provider-specific parser exception to report input.
            throw new ArgumentException("Invalid regular expression: "+exception.Message,nameof(options),exception);
        }
    }
    // Capture primitive fields on the owning thread. Background work reads only
    // these strings and metadata, never live editor fields or another document.
    public SubtitleSearchInput[] CaptureInputs(IEnumerable<AssEvent> lines,CancellationToken token=default)
    {
        List<SubtitleSearchInput> inputs=[];HashSet<AssEvent> seen=[];
        foreach(var line in lines)
        {
            token.ThrowIfCancellationRequested();if(!seen.Add(line))continue;
            var field=_options.Field switch
            {
                SubtitleSearchField.Text=>"Text",SubtitleSearchField.Style=>"Style",
                SubtitleSearchField.Actor=>line.Index("Name")>=0?"Name":"Actor",SubtitleSearchField.Effect=>"Effect",
                _=>throw new ArgumentException("Unknown search field."),
            };
            inputs.Add(new(line,line.Get(field),field,line.IsComment));
        }
        return inputs.ToArray();
    }
    private IEnumerable<Candidate> Matches(SubtitleSearchInput input,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();if(_options.IgnoreComments&&input.Comment)yield break;
        var projection=new SubtitleSearchProjection(input.Source,_options.Field==SubtitleSearchField.Text&&_options.SkipTags);
        for(var match=_regex.Match(projection.Text);match.Success;match=match.NextMatch())
        {
            token.ThrowIfCancellationRequested();
            if(_options.WholeField&&(match.Index!=0||match.Length!=projection.Text.Length))continue;
            // .NET regex operates on UTF-16. Never expose a match which would
            // replace half a surrogate pair or a user-perceived character.
            if(!projection.IsWholeGraphemeRange(match.Index,match.Length))continue;
            yield return new(projection,match,new(input.Line,projection.SourceIndex(match.Index),projection.SourceLength(match.Index,match.Length))
                {OriginalSource=input.Source,FieldName=input.FieldName});
        }
    }
    public SubtitleSearchMatch? Find(IReadOnlyList<AssEvent> lines,AssEvent? current,int offset,bool backwards,CancellationToken token=default)
        =>FindSnapshot(CaptureInputs(lines,token),current,offset,backwards,token);
    public SubtitleSearchMatch? FindSnapshot(IReadOnlyList<SubtitleSearchInput> inputs,AssEvent? current,int offset,bool backwards,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();if(inputs.Count==0)return null;
        var currentRow=-1;for(var index=0;index<inputs.Count;index++)if(ReferenceEquals(inputs[index].Line,current)){currentRow=index;break;}
        SubtitleSearchMatch? InRow(int row,Func<SubtitleSearchMatch,bool>? predicate=null)
        {
            SubtitleSearchMatch? last=null;
            foreach(var candidate in Matches(inputs[row],token))
            {
                if(predicate is not null&&!predicate(candidate.Result))continue;
                if(!backwards)return candidate.Result;last=candidate.Result;
            }
            return last;
        }
        if(currentRow<0)
        {
            for(var step=0;step<inputs.Count;step++)if(InRow(backwards?inputs.Count-1-step:step) is {} found)return found;
            return null;
        }
        if(_options.Field==SubtitleSearchField.Text&&InRow(currentRow,result=>backwards?result.Index<offset:result.Index>=offset) is {} local)return local;
        for(var step=1;step<inputs.Count;step++)
        {
            var row=(currentRow+(backwards?-step:step)+inputs.Count)%inputs.Count;
            if(InRow(row) is {} found)return found;
        }
        return InRow(currentRow,_options.Field==SubtitleSearchField.Text?result=>backwards?result.Index>=offset:result.Index<offset:null);
    }
    public void Replace(SubtitleEditor editor,SubtitleSearchMatch match,string replacement)
    {
        if(!editor.Document.Events.Contains(match.Line))throw new InvalidOperationException("Search result belongs to another document.");
        var input=CaptureInputs([match.Line]).Single();
        var edit=PrepareReplacement(input,match,replacement);
        editor.SetField(edit.Line,edit.FieldName,edit.Text,"Replace subtitle text");
    }
    public Replacement PrepareReplacement(SubtitleSearchInput input,SubtitleSearchMatch match,string replacement,CancellationToken token=default)
    {
        if(!ReferenceEquals(input.Line,match.Line))throw new InvalidOperationException("Search result belongs to another line.");
        if(match.OriginalSource is not null&&input.Source!=match.OriginalSource)throw new InvalidOperationException("Search result changed; find again.");
        var found=Matches(input,token).FirstOrDefault(candidate=>candidate.Result.Index==match.Index&&candidate.Result.Length==match.Length&&candidate.Result.FieldName==match.FieldName)
            ??throw new InvalidOperationException("Search result changed; find again.");
        var text=found.Projection.Replace(input.Source,found.Match.Index,found.Match.Length,_options.RegularExpression?found.Match.Result(replacement):replacement);
        return new(input.Line,input.Source,text,input.FieldName);
    }
    public sealed record Replacement(AssEvent Line,string Original,string Text,string FieldName="Text");
    public Replacement[] PrepareReplacements(IEnumerable<AssEvent> lines,string replacement,CancellationToken token=default)
        =>PrepareSnapshotReplacements(CaptureInputs(lines,token),replacement,token);
    public Replacement[] PrepareSnapshotReplacements(IReadOnlyList<SubtitleSearchInput> inputs,string replacement,CancellationToken token=default)
    {
        List<Replacement> edits=[];
        foreach(var input in inputs)
        {
            token.ThrowIfCancellationRequested();var text=input.Source;SubtitleSearchProjection? projection=null;
            List<(int Index,int Length,string Text)> patches=[];
            foreach(var found in Matches(input,token))
            {
                projection=found.Projection;patches.Add((found.Match.Index,found.Match.Length,_options.RegularExpression?found.Match.Result(replacement):replacement));
            }
            token.ThrowIfCancellationRequested();if(projection is not null)text=projection.ReplaceMany(patches,token);
            if(text!=input.Source)edits.Add(new(input.Line,input.Source,text,input.FieldName));
        }
        return edits.ToArray();
    }
    public static int ApplyReplacements(SubtitleEditor editor,IReadOnlyList<Replacement> edits)
    {
        var owned=editor.Document.Events.ToHashSet();
        if(edits.Any(edit=>!owned.Contains(edit.Line)||edit.Line.Get(edit.FieldName)!=edit.Original))throw new InvalidOperationException("Subtitles changed during search. Search again.");
        using var transaction=editor.Undo.BeginTransaction("Replace all subtitle text");
        foreach(var edit in edits)editor.SetField(edit.Line,edit.FieldName,edit.Text,"Replace subtitle text");
        transaction.Commit();return edits.Count;
    }
    public int ReplaceAll(SubtitleEditor editor,IEnumerable<AssEvent> lines,string replacement)=>ApplyReplacements(editor,PrepareReplacements(lines,replacement));
}
