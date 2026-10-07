using System.ComponentModel;
using System.Runtime.CompilerServices;
using Yoake.Core.Subtitles;

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
    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyDictionary<string,string> Values => _values;
    public bool IsChanged => _values.Any(p => _line.Get(p.Key) != p.Value);
    private string Read(string name) => _values.GetValueOrDefault(name, "");
    private void Write(string value, string name, [CallerMemberName] string? property = null) { if (!_values.ContainsKey(name) || Read(name) == value) return; _values[name] = value; PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(property)); PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(IsChanged))); if(name is "Start" or "End")PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(Duration))); }
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
    public decimal? LeftMargin {get=>Number(MarginL);set{if(value is {} n)MarginL=n.ToString(System.Globalization.CultureInfo.InvariantCulture);}}
    public decimal? RightMargin {get=>Number(MarginR);set{if(value is {} n)MarginR=n.ToString(System.Globalization.CultureInfo.InvariantCulture);}}
    public decimal? VerticalMargin {get=>Number(MarginV);set{if(value is {} n)MarginV=n.ToString(System.Globalization.CultureInfo.InvariantCulture);}}
    public string Duration => AssTime.TryParse(Start,out var start)&&AssTime.TryParse(End,out var end)?AssTime.Format(end-start):"—";
    private static decimal? Number(string text)=>decimal.TryParse(text,System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture,out var n)?n:null;
}
