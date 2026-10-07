using System.Text.Json;
using System.Text.Json.Serialization;

namespace Yoake.Core.Subtitles;

public sealed class StyleCollection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string AssSource { get; set; } = "";
}
public sealed class StyleLibraryData
{
    public int Version { get; set; } = 1;
    public List<StyleCollection> Collections { get; set; } = [];
}
[JsonSourceGenerationOptions(WriteIndented=true)]
[JsonSerializable(typeof(StyleLibraryData))]
internal partial class StyleLibraryJsonContext : JsonSerializerContext;

// Local source-preserving ASS collections. A library's history is independent
// of every open document, and copying crosses the boundary via cloned records.
public sealed class StyleLibraryStore
{
    private readonly string _path;
    private readonly StyleLibraryData _data;
    private readonly Dictionary<Guid,SubtitleEditor> _editors=[];
    public StyleLibraryStore(string path)
    {
        _path=path;
        if(File.Exists(path))
        {
            using var stream=File.OpenRead(path);
            _data=JsonSerializer.Deserialize(stream,StyleLibraryJsonContext.Default.StyleLibraryData)??throw new InvalidDataException("Style library is null.");
            if(_data.Version!=1 || _data.Collections is null || _data.Collections.Any(c=>c is null||c.Id==Guid.Empty||string.IsNullOrWhiteSpace(c.Name)||c.AssSource is null) || _data.Collections.Select(c=>c.Id).Distinct().Count()!=_data.Collections.Count)
                throw new InvalidDataException("Unsupported or damaged style library; the original file was left unchanged.");
        }
        else _data=new();
    }
    public IReadOnlyList<StyleCollection> Collections => _data.Collections;
    public StyleCollection Create(string name)
    {
        ValidateName(name); var collection=new StyleCollection{Name=name,AssSource="[V4+ Styles]\n"};_data.Collections.Add(collection);Save();return collection;
    }
    public void Rename(StyleCollection collection,string name) { Require(collection);ValidateName(name,collection);collection.Name=name;Save(); }
    public void Delete(StyleCollection collection) { Require(collection);_data.Collections.Remove(collection);_editors.Remove(collection.Id);Save(); }
    public SubtitleEditor Editor(StyleCollection collection)
    {
        Require(collection);if(!_editors.TryGetValue(collection.Id,out var editor))_editors[collection.Id]=editor=new(AssDocument.Parse(collection.AssSource));return editor;
    }
    public static AssStyle Copy(AssStyle source,SubtitleEditor destination) => destination.AddStyle(source);
    public void DeletePreset(StyleCollection collection,AssStyle style) { Editor(collection).DeleteLibraryStyle(style);Save(); }
    public IReadOnlyList<AssStyle> Import(AssDocument source,SubtitleEditor destination)
    {
        // Each copied style retains its own Format, including future fields.
        List<AssStyle> result=[];foreach(var style in source.Styles)result.Add(Copy(style,destination));return result;
    }
    public void Save()
    {
        foreach(var collection in _data.Collections)if(_editors.TryGetValue(collection.Id,out var editor))collection.AssSource=editor.Document.Serialize();
        var directory=Path.GetDirectoryName(Path.GetFullPath(_path))!;Directory.CreateDirectory(directory);
        var temporary=_path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { using(var stream=File.Create(temporary)){JsonSerializer.Serialize(stream,_data,StyleLibraryJsonContext.Default.StyleLibraryData);stream.Flush(true);}File.Move(temporary,_path,true); }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
    }
    private void Require(StyleCollection collection) { if(!_data.Collections.Contains(collection))throw new InvalidOperationException("Collection belongs to another library."); }
    private void ValidateName(string name,StyleCollection? current=null)
    {
        if(string.IsNullOrWhiteSpace(name)||_data.Collections.Any(c=>c!=current&&c.Name.Equals(name,StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("Collection names must be nonempty and unique.");
    }
}
