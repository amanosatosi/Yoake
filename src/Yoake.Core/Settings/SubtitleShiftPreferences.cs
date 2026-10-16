using Yoake.Core.Subtitles;

namespace Yoake.Core.Settings;

public sealed record SubtitleShiftPreferences
{
    public long Milliseconds {get;init;}=0;
    public int Frames {get;init;}=0;
    public bool ByFrames {get;init;}
    public bool Backward {get;init;}
    public SubtitleTimingFields Fields {get;init;}=SubtitleTimingFields.Both;
    public SubtitleTimingScope Scope {get;init;}=SubtitleTimingScope.Selected;
    public SubtitleShiftPreferences Normalize()=>this with
    {
        Milliseconds=Milliseconds>=0&&Milliseconds<=int.MaxValue*3_600_000L?Milliseconds:0,
        Frames=Math.Max(0,Frames),Fields=Enum.IsDefined(Fields)?Fields:SubtitleTimingFields.Both,
        Scope=Enum.IsDefined(Scope)?Scope:SubtitleTimingScope.Selected,
    };
    public SubtitleShiftRequest Request()=>new(checked((Backward?-1L:1L)*(ByFrames?Frames:Milliseconds)),
        ByFrames?SubtitleShiftUnit.Frames:SubtitleShiftUnit.Milliseconds,Fields,Scope);
}
public sealed record SubtitleShiftHistoryEntry(DateTimeOffset When,string Document,string Rows,SubtitleShiftPreferences Preferences)
{
    public override string ToString()=>$"{When.LocalDateTime:g} · {Document} · {(Preferences.Backward?"−":"+")}{(Preferences.ByFrames?Preferences.Frames+" frames":AssTime.Format(Preferences.Milliseconds))} · {Preferences.Fields} · {Rows}";
}
