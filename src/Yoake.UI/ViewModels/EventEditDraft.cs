using System.ComponentModel;
using System.Runtime.CompilerServices;
using Yoake.Core.Subtitles;
using Yoake.Core.Media;
using System.Globalization;

namespace Yoake.UI.ViewModels;

public sealed class EventEditDraft : INotifyPropertyChanged
{
    private readonly Dictionary<string,string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly AssEvent _line;
    public EventEditDraft(AssEvent line)
    {
        _line = line;
        foreach (var name in line.FieldNames) _values[name] = line.Get(name);
    }
    internal AssEvent Line => _line;
    internal EventEditDraft(AssEvent line, IReadOnlyDictionary<string,string> recovered) : this(line)
    {
        foreach(var pair in recovered) if(_values.ContainsKey(pair.Key)) _values[pair.Key]=pair.Value;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyDictionary<string,string> Values => _values;
    public bool IsChanged => _values.Any(p => _line.Get(p.Key) != p.Value);
    // The audio controller owns endpoints once its markers are committed.
    // Keep the same draft and all text/metadata edits, rebasing only timing.
    public void SynchronizeTiming()
    {
        _values["Start"]=_line.Start;_values["End"]=_line.End;
        PropertyChanged?.Invoke(this,new(nameof(Start)));PropertyChanged?.Invoke(this,new(nameof(End)));
        PropertyChanged?.Invoke(this,new(nameof(Duration)));PropertyChanged?.Invoke(this,new(nameof(IsChanged)));NotifyTimingDisplay();
    }
    private string Read(string name) => _values.GetValueOrDefault(name, "");
    private void Write(string? value, string name, [CallerMemberName] string? property = null) { value=string.IsNullOrWhiteSpace(value)&&(name is "MarginL" or "MarginR" or "MarginV" or "Layer" or "Marked")?"0":value??""; if (!_values.ContainsKey(name) || Read(name) == value) return; _values[name] = value; PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(property)); PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(IsChanged))); if(name is "Start" or "End"){PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(Duration)));NotifyTimingDisplay();} }
    public string Layer { get => Read(_line.FieldNames.Any(n=>n.Equals("Layer",StringComparison.OrdinalIgnoreCase)) ? "Layer" : "Marked"); set => Write(value,_line.FieldNames.Any(n=>n.Equals("Layer",StringComparison.OrdinalIgnoreCase)) ? "Layer" : "Marked"); }
    public string Start { get => Read("Start"); set => Write(value,"Start"); }
    public string End { get => Read("End"); set => Write(value,"End"); }
    public string Style { get => Read("Style"); set { if (value is not null) Write(value,"Style"); } }
    public string Actor { get => Read(_line.FieldNames.Any(n=>n.Equals("Name",StringComparison.OrdinalIgnoreCase)) ? "Name" : "Actor"); set => Write(value,_line.FieldNames.Any(n=>n.Equals("Name",StringComparison.OrdinalIgnoreCase)) ? "Name" : "Actor"); }
    public string Effect { get => Read("Effect"); set => Write(value,"Effect"); }
    public string MarginL { get => Read("MarginL"); set => Write(value,"MarginL"); }
    public string MarginR { get => Read("MarginR"); set => Write(value,"MarginR"); }
    public string MarginV { get => Read("MarginV"); set => Write(value,"MarginV"); }
    public string Text { get => Read("Text"); set => Write(value,"Text"); }
    public decimal? LayerNumber {get=>Number(Layer);set{if(value is {} n)Layer=n.ToString(System.Globalization.CultureInfo.InvariantCulture);}}
    public decimal? LeftMargin {get=>Number(MarginL);set=>MarginL=(value??0).ToString(CultureInfo.InvariantCulture);}
    public decimal? RightMargin {get=>Number(MarginR);set=>MarginR=(value??0).ToString(CultureInfo.InvariantCulture);}
    public decimal? VerticalMargin {get=>Number(MarginV);set=>MarginV=(value??0).ToString(CultureInfo.InvariantCulture);}
    public string Duration => AssTime.TryParse(Start,out var start)&&AssTime.TryParse(End,out var end)?AssTime.Format(end-start):"—";
    private FrameTiming? _frameTiming;
    internal void SetTimingProjection(FrameTiming? timing)
    {
        if(ReferenceEquals(_frameTiming,timing))return;_frameTiming=timing;NotifyTimingDisplay();
    }
    private void NotifyTimingDisplay()
    {
        PropertyChanged?.Invoke(this,new(nameof(TimingStart)));PropertyChanged?.Invoke(this,new(nameof(TimingEnd)));PropertyChanged?.Invoke(this,new(nameof(TimingDuration)));
    }
    private string DisplayBoundary(string time,FrameTimeKind kind)
        =>_frameTiming is {} map&&AssTime.TryParse(time,out var ms)?map.FrameAtMilliseconds(ms,kind).ToString(CultureInfo.InvariantCulture):time;
    private string ParseBoundary(string value,FrameTimeKind kind)
    {
        if(_frameTiming is not {} map)return value;
        if(int.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out var frame))
        {
            try{return AssTime.Format(map.SubtitleBoundary(frame,kind));}catch(OverflowException){return "invalid frame";}
        }
        // Invalid frame input remains invalid instead of publishing an old timing.
        return value.Contains(':')?"invalid frame":value;
    }
    public string TimingStart{get=>DisplayBoundary(Start,FrameTimeKind.Start);set=>Start=ParseBoundary(value,FrameTimeKind.Start);}
    public string TimingEnd{get=>DisplayBoundary(End,FrameTimeKind.End);set=>End=ParseBoundary(value,FrameTimeKind.End);}
    public string TimingDuration
    {
        get
        {
            if(_frameTiming is not {} map)return Duration;
            if(!AssTime.TryParse(Start,out var start)||!AssTime.TryParse(End,out var end))return "";
            return Math.Max(0L,(long)map.FrameAtMilliseconds(end,FrameTimeKind.End)-map.FrameAtMilliseconds(start,FrameTimeKind.Start)+1).ToString(CultureInfo.InvariantCulture);
        }
        set
        {
            if(!AssTime.TryParse(Start,out var start))return;
            if(_frameTiming is {} map)
            {
                if(int.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out var duration)&&duration>0)
                {
                    var last=(long)map.FrameAtMilliseconds(start,FrameTimeKind.Start)+duration-1;
                    if(last is >=0 and <=int.MaxValue)
                    {try{End=AssTime.Format(map.SubtitleBoundary((int)last,FrameTimeKind.End));}catch(OverflowException){return;}}
                }
            }
            else if(AssTime.TryParse(value,out var timeDuration))End=AssTime.Format(start+timeDuration);
        }
    }
    private static decimal? Number(string text)=>decimal.TryParse(text,System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture,out var n)?n:null;
}
