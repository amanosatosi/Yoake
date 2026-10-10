namespace Yoake.Core.Subtitles;

// Exact RGBA identity (ASS transparency included), most recent first. Separate
// from settings snapshots so a dialog cannot overwrite newer workspace settings.
public sealed class RecentColorStore(string path)
{
    public const int Capacity=32;
    public IReadOnlyList<AssColor> Load()
    {
        if(!File.Exists(path))return [];
        return File.ReadAllLines(path).Select(s=>AssColor.TryParse(s,out var c)?(AssColor?)c:null)
            .OfType<AssColor>().Distinct().Take(Capacity).ToArray();
    }
    public void Remember(AssColor color)
    {
        var colors=new[]{color}.Concat(Load()).Distinct().Take(Capacity).Select(c=>c.StyleValue);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllLines(temporary,colors);File.Move(temporary,path,true);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
