using System.Text.Json;

namespace Yoake.UI.Services;

public sealed record VisualPalette(string Primary,string Detail,string Warning);

/// <summary>Profile-local, data-only palette; no XAML execution or reflection serialization.</summary>
public static class VisualPaletteFile
{
    public static VisualPalette LightDefault {get;}=new("#B65B15","#2379B3","#C43D47");
    public static VisualPalette DarkDefault {get;}=new("#F5AD62","#62B5EF","#EE7272");
    public const string DefaultJson="""
        {
          "light": { "primary": "#B65B15", "detail": "#2379B3", "warning": "#C43D47" },
          "dark": { "primary": "#F5AD62", "detail": "#62B5EF", "warning": "#EE7272" }
        }
        """;

    public static (VisualPalette Light,VisualPalette Dark) Parse(string json,Action<string>? warning=null)
    {
        try
        {
            using var document=JsonDocument.Parse(json);
            if(document.RootElement.ValueKind!=JsonValueKind.Object)throw new JsonException("Expected a JSON object.");
            VisualPalette Read(string name,VisualPalette fallback)
            {
                if(!document.RootElement.TryGetProperty(name,out var section))return fallback;
                if(section.ValueKind!=JsonValueKind.Object){warning?.Invoke("Invalid visual palette: "+name);return fallback;}
                string Field(string key,string original)
                {
                    if(!section.TryGetProperty(key,out var value))return original;
                    if(value.ValueKind==JsonValueKind.String&&value.GetString() is {} text&&text.Length==7&&text[0]=='#'&&text.Skip(1).All(Uri.IsHexDigit))return text;
                    warning?.Invoke("Invalid visual palette color: "+name+"."+key);
                    return original;
                }
                return new(Field("primary",fallback.Primary),Field("detail",fallback.Detail),Field("warning",fallback.Warning));
            }
            return (Read("light",LightDefault),Read("dark",DarkDefault));
        }
        catch(JsonException ex){warning?.Invoke("Invalid visual palette JSON: "+ex.Message);return (LightDefault,DarkDefault);}
    }

    public static (VisualPalette Light,VisualPalette Dark) Load(string profileDirectory,Action<string>? warning=null)
    {
        try
        {
            var directory=Path.Combine(profileDirectory,"themes");
            var path=Path.Combine(directory,"visual-tools.json");
            if(!File.Exists(path))
            {
                Directory.CreateDirectory(directory);
                // Create only; never replace a user's custom palette.
                using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write);
                using var writer=new StreamWriter(stream);
                writer.Write(DefaultJson);
                return (LightDefault,DarkDefault);
            }
            return Parse(File.ReadAllText(path),warning);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
        {warning?.Invoke("Could not load visual palette: "+ex.Message);return (LightDefault,DarkDefault);}
    }
}
