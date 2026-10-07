using System.ComponentModel;

namespace Yoake.Core.Subtitles;

public abstract class AssRecord : INotifyPropertyChanged
{
    internal string Prefix;
    internal string[] Format;
    internal string[] Fields;
    private readonly Dictionary<string,int> _indices=new(StringComparer.OrdinalIgnoreCase);
    internal AssRecord(string prefix, string[] format, string[] fields)
    {
        (Prefix,Format,Fields)=(prefix,format,fields);
        for(var i=0;i<format.Length;i++)_indices.TryAdd(format[i],i);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<string> FieldNames => Array.AsReadOnly(Format);
    internal int Index(string name) => _indices.GetValueOrDefault(name,-1);
    public string Get(string name) { var i = Index(name); return i >= 0 && i < Fields.Length ? Fields[i] : ""; }
    public void Set(string name, string value)
    {
        var i = Index(name);
        if (i < 0) throw new InvalidOperationException($"ASS Format has no {name} field.");
        if (Get(name) == value) return;
        if (name.Equals("Text", StringComparison.OrdinalIgnoreCase)) value = value.Replace("\r\n", "\\N").Replace("\r", "\\N").Replace("\n", "\\N");
        else if (value.IndexOfAny([',', '\r', '\n']) >= 0) throw new ArgumentException($"{name} cannot contain commas or newlines.");
        if (Get(name) == value) return;
        if (Fields.Length <= i) Array.Resize(ref Fields, Format.Length);
        Fields[i] = value;
        Notify(name is "Name" or "Actor" ? "Actor" : name);
        if (name is "Start" or "End") { Notify(name + "Milliseconds"); Notify("Duration"); }
    }
    internal void RestoreFields(string[] fields) { Fields = (string[])fields.Clone(); Notify(""); }
    internal string Serialize() => Prefix + string.Join(',', Fields);
    protected void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class AssEvent : AssRecord
{
    internal AssEvent(string prefix, string[] format, string[] fields) : base(prefix, format, fields) { }
    public string Kind => Prefix[..Prefix.IndexOf(':')].Trim();
    public bool IsComment => Kind.Equals("Comment", StringComparison.OrdinalIgnoreCase);
    public int Number { get; private set; }
    public string Layer { get => Get(Index("Layer") >= 0 ? "Layer" : "Marked"); set => Set(Index("Layer") >= 0 ? "Layer" : "Marked", value); }
    public string Start { get => Get("Start"); set => Set("Start", value); }
    public string End { get => Get("End"); set => Set("End", value); }
    public string Style { get => Get("Style"); set => Set("Style", value); }
    public string Actor { get => Get(Index("Name") >= 0 ? "Name" : "Actor"); set => Set(Index("Name") >= 0 ? "Name" : "Actor", value); }
    public string MarginL { get => Get("MarginL"); set => Set("MarginL", value); }
    public string MarginR { get => Get("MarginR"); set => Set("MarginR", value); }
    public string MarginV { get => Get("MarginV"); set => Set("MarginV", value); }
    public string Effect { get => Get("Effect"); set => Set("Effect", value); }
    public string Text { get => Get("Text"); set => Set("Text", value); }
    private string? _startSource, _endSource;
    private long? _startTime, _endTime;
    private bool _isActive, _isCurrent;
    public bool IsCurrent {get=>_isCurrent;internal set{if(_isCurrent==value)return;_isCurrent=value;Notify(nameof(IsCurrent));}}
    public bool IsActive { get=>_isActive; internal set {if(_isActive==value)return;_isActive=value;Notify(nameof(IsActive));} }
    public long? StartMilliseconds {get {var source=Start;if(_startSource!=source){_startSource=source;_startTime=AssTime.TryParse(source,out var t)?t:null;}return _startTime;} }
    public long? EndMilliseconds {get {var source=End;if(_endSource!=source){_endSource=source;_endTime=AssTime.TryParse(source,out var t)?t:null;}return _endTime;} }
    public string Duration => StartMilliseconds is { } start && EndMilliseconds is { } end ? AssTime.Format(end - start) : "";
    internal void RestorePrefix(string prefix) { Prefix=prefix;Notify(nameof(Kind));Notify(nameof(IsComment)); }
    internal void SetKind(bool comment)
    {
        if(IsComment==comment)return;
        var colon=Prefix.IndexOf(':');var start=0;while(start<colon&&char.IsWhiteSpace(Prefix[start]))start++;
        var end=colon;while(end>start&&char.IsWhiteSpace(Prefix[end-1]))end--;
        RestorePrefix(Prefix[..start]+(comment?"Comment":"Dialogue")+Prefix[end..]);
    }
    internal void Renumber(int number) { if(Number==number)return; Number = number; Notify(nameof(Number)); }
    public AssEvent Clone() => new(Prefix, (string[])Format.Clone(), (string[])Fields.Clone());
}

public sealed class AssStyle : AssRecord
{
    internal AssStyle(string prefix, string[] format, string[] fields) : base(prefix, format, fields) { }
    public string Name { get => Get("Name"); set => Set("Name", value); }
    public AssStyle Clone() => new(Prefix, (string[])Format.Clone(), (string[])Fields.Clone());
}
