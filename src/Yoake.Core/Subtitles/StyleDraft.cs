using System.Globalization;

namespace Yoake.Core.Subtitles;

public sealed class StyleDraft
{
    private readonly AssStyle _source;
    private readonly Dictionary<string,string> _values = new(StringComparer.OrdinalIgnoreCase);
    public StyleDraft(AssStyle source) { _source=source; foreach(var field in source.FieldNames) _values[field]=source.Get(field); }
    public IReadOnlyDictionary<string,string> Values => _values;
    public string Get(string field) => _values.GetValueOrDefault(field,"");
    public bool Has(string field) => _values.ContainsKey(field);
    public bool IsChanged => _values.Any(p=>_source.Get(p.Key)!=p.Value);
    public void Set(string field,string value) { if(!Has(field))throw new ArgumentException($"Style Format has no {field} field."); _values[field]=value; }
    public decimal Number(string field,decimal fallback=0) => decimal.TryParse(Get(field),NumberStyles.Float,CultureInfo.InvariantCulture,out var value)?value:fallback;
    public void SetNumber(string field,decimal value) => Set(field,value.ToString(CultureInfo.InvariantCulture));
    public bool Flag(string field) => int.TryParse(Get(field),out var value)&&value!=0;
    public void SetFlag(string field,bool value) => Set(field,value?"-1":"0");
    public void SetColor(string field,AssColor color) => Set(field,color.StyleValue);
    public AssStyle Preview() { Validate(); var copy=_source.Clone();foreach(var field in _values)copy.Set(field.Key,field.Value);return copy; }
    public void Apply(SubtitleEditor editor) { Validate(); editor.EditStyle(_source,_values); }
    public void Validate() => ValidateValues(_values.Where(p=>_source.Get(p.Key)!=p.Value).ToDictionary(p=>p.Key,p=>p.Value,StringComparer.OrdinalIgnoreCase));
    public static void ValidateValues(IReadOnlyDictionary<string,string> values)
    {
        foreach(var pair in values)
        {
            var field=pair.Key.ToLowerInvariant();
            if(field is "fontsize" or "scalex" or "scaley" or "spacing" or "angle" or "outline" or "shadow")
            {
                if(!decimal.TryParse(pair.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var value))throw new ArgumentException($"{pair.Key} must be a finite number.");
                if(field is "fontsize" or "scalex" or "scaley" && value<=0 || field is "outline" or "shadow" && value<0)throw new ArgumentException($"Invalid {pair.Key}.");
            }
            if(field is "primarycolour" or "secondarycolour" or "outlinecolour" or "backcolour" && !AssColor.TryParse(pair.Value,out _))throw new ArgumentException($"{pair.Key}: enter ASS &HAABBGGRR color.");
            if(field is "alignment" or "borderstyle" or "marginl" or "marginr" or "marginv" or "encoding" or "bold" or "italic" or "underline" or "strikeout")
            {
                if(!int.TryParse(pair.Value,NumberStyles.Integer,CultureInfo.InvariantCulture,out var value) || field=="alignment"&&value is <1 or >9 || field.StartsWith("margin",StringComparison.Ordinal)&&value<0)
                    throw new ArgumentException($"Invalid {pair.Key}.");
            }
        }
    }
}

public static class FontAvailability
{
    public static bool IsInstalled(string exactName,IEnumerable<string> names) => names.Contains(exactName,StringComparer.OrdinalIgnoreCase);
    // A missing family is a valid ASS source value, never an instruction to
    // substitute a different name. The native renderer owns font fallback.
    public static string PreserveName(string exactName,IEnumerable<string> installed) => exactName;
}
