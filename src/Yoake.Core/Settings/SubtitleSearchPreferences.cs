using Yoake.Core.Subtitles;

namespace Yoake.Core.Settings;

public sealed record SubtitleSearchPreferences
{
    public string Query {get;init;}="";
    public string Replacement {get;init;}="";
    public bool MatchCase {get;init;}
    public bool RegularExpression {get;init;}
    public bool RawSource {get;init;}
    public bool IgnoreComments {get;init;}
    public bool WholeField {get;init;}
    public bool SelectionOnly {get;init;}
    public SubtitleSearchField Field {get;init;}
    public SubtitleSearchPreferences Normalize()=>this with{Query=Query is {Length:<=16384}?Query:"",Replacement=Replacement??"",Field=Enum.IsDefined(Field)?Field:SubtitleSearchField.Text};
    public SubtitleSearchOptions Options()=>new(Query,MatchCase,RegularExpression,!RawSource,IgnoreComments,WholeField,Field);
}
