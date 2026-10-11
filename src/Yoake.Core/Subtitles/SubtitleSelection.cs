using Yoake.Core.Media;

namespace Yoake.Core.Subtitles;

public enum SubtitleSelectionMode { Contains, Exact, RegularExpression }
public enum SubtitleSelectionAction { Replace, Add, Subtract, Intersect }
public sealed record SubtitleSelectionOptions
{
    public string Query {get;init;}="";
    public SubtitleSearchField Field {get;init;}
    public SubtitleSelectionMode Mode {get;init;}
    public SubtitleSelectionAction Action {get;init;}
    public bool MatchCase {get;init;}
    public bool Invert {get;init;}
    public bool IncludeDialogue {get;init;}=true;
    public bool IncludeComments {get;init;}
    public bool RawSource {get;init;}
    public SubtitleSelectionOptions Normalize()=>this with
    {
        Query=Query is {Length:<=16384}?Query:"",Field=Enum.IsDefined(Field)?Field:SubtitleSearchField.Text,
        Mode=Enum.IsDefined(Mode)?Mode:SubtitleSelectionMode.Contains,Action=Enum.IsDefined(Action)?Action:SubtitleSelectionAction.Replace,
        IncludeDialogue=IncludeDialogue||!IncludeComments,
    };
    internal SubtitleSearch? Matcher()
    {
        if(Query is null||Query.Length>16384||!Enum.IsDefined(Field)||!Enum.IsDefined(Mode)||!Enum.IsDefined(Action)||!IncludeDialogue&&!IncludeComments)
            throw new ArgumentException("Choose valid matching criteria and include dialogues, comments, or both.");
        return Query.Length==0?null:new(new(Query,MatchCase,Mode==SubtitleSelectionMode.RegularExpression,!RawSource,false,Mode==SubtitleSelectionMode.Exact,Field));
    }
}

// Immutable primitive snapshots for background matching over the existing
// document. Line references identify the result; background work never reads them.
public sealed class SubtitleSelectionSnapshot
{
    private sealed record Row(SubtitleSearchInput Input,long? Start,long? End,bool Selected);
    private readonly Row[] _rows;
    private readonly SubtitleEditor _editor;
    private readonly long _revision;
    private readonly SubtitleSelectionOptions _options;
    private readonly SubtitleSearch? _matcher;
    private SubtitleSelectionSnapshot(SubtitleEditor editor,Row[] rows,SubtitleSelectionOptions options,SubtitleSearch? matcher)
    {_editor=editor;_revision=editor.Document.Revision;_rows=rows;_options=options;_matcher=matcher;}
    public static SubtitleSelectionSnapshot Capture(SubtitleEditor editor,IReadOnlyList<AssEvent> selection,SubtitleSelectionOptions options,
        AssEvent? draftLine=null,IReadOnlyDictionary<string,string>? draftValues=null,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(editor);ArgumentNullException.ThrowIfNull(selection);ArgumentNullException.ThrowIfNull(options);token.ThrowIfCancellationRequested();
        var matcher=options.Matcher();var selected=selection.ToHashSet();var all=editor.Document.Events.ToArray();var owned=all.ToHashSet();
        if(selected.Any(line=>!owned.Contains(line)))throw new ArgumentException("Selection belongs to another document.");
        List<Row> rows=[];
        foreach(var line in all)
        {
            token.ThrowIfCancellationRequested();var field=options.Field switch
            {SubtitleSearchField.Text=>"Text",SubtitleSearchField.Style=>"Style",SubtitleSearchField.Actor=>line.Index("Name")>=0?"Name":"Actor",_=>"Effect"};
            var source=line.Get(field);
            if(ReferenceEquals(line,draftLine)&&draftValues is not null&&draftValues.TryGetValue(field,out var value))source=value;
            rows.Add(new(new(line,source,field,line.IsComment),line.StartMilliseconds,line.EndMilliseconds,selected.Contains(line)));
        }
        return new(editor,rows.ToArray(),options,matcher);
    }
    public SubtitleSelectionResult Match(CancellationToken token=default)
    {
        List<AssEvent> result=[];var matches=0;
        foreach(var row in _rows)
        {
            token.ThrowIfCancellationRequested();var eligible=row.Input.Comment?_options.IncludeComments:_options.IncludeDialogue;
            var matched=false;
            if(eligible)
            {
                var match=_matcher is not null?_matcher.HasMatchSnapshot(row.Input,token)
                    :_options.Mode!=SubtitleSelectionMode.Exact||new SubtitleSearchProjection(row.Input.Source,_options.Field==SubtitleSearchField.Text&&!_options.RawSource).Text.Length==0;
                matched=_options.Invert?!match:match;if(matched)matches++;
            }
            var keep=_options.Action switch
            {SubtitleSelectionAction.Replace=>matched,SubtitleSelectionAction.Add=>row.Selected||matched,SubtitleSelectionAction.Subtract=>row.Selected&&!matched,_=>row.Selected&&matched};
            if(keep)result.Add(row.Input.Line);
        }
        token.ThrowIfCancellationRequested();return new(this,result.ToArray(),matches);
    }
    public SubtitleSelectionResult Visible(FrameTiming timing,int frame,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(timing);if(frame<0)throw new ArgumentException("Video frame must be non-negative.");List<AssEvent> selected=[];
        foreach(var row in _rows)
        {
            token.ThrowIfCancellationRequested();
            // The release iterates all event rows, including comments, and uses
            // inclusive START/END frame conversions rather than timestamp tests.
            if(row.Start is {} start&&row.End is {} end&&end>=start&&timing.FrameAtMilliseconds(start,FrameTimeKind.Start)<=frame&&timing.FrameAtMilliseconds(end,FrameTimeKind.End)>=frame)selected.Add(row.Input.Line);
        }
        token.ThrowIfCancellationRequested();return new(this,selected.ToArray(),selected.Count);
    }
    internal void Validate(SubtitleEditor editor)
    {if(!ReferenceEquals(editor,_editor)||editor.Document.Revision!=_revision)throw new InvalidOperationException("Subtitle document changed; select again.");}
}

public sealed class SubtitleSelectionResult
{
    private readonly SubtitleSelectionSnapshot _snapshot;
    public IReadOnlyList<AssEvent> Lines {get;}
    public int MatchCount {get;}
    internal SubtitleSelectionResult(SubtitleSelectionSnapshot snapshot,AssEvent[] lines,int matches)
    {_snapshot=snapshot;Lines=Array.AsReadOnly(lines);MatchCount=matches;}
    public AssEvent? ActiveLine(AssEvent? previous,bool firstVisible=false)
        =>Lines.Count==0||!firstVisible&&previous is not null&&Lines.Contains(previous)?previous:Lines[0];
    public void Validate(SubtitleEditor editor)=>_snapshot.Validate(editor);
}
