namespace Yoake.Core.Subtitles;

public static class VisualMeasurement
{
    // A detached measurement track; none of these changes touch the editor.
    // Keep shaping/font/run information, remove geometry and paint effects.
    public static string Header(AssDocument document)=>string.Concat(document.Source
        .Where(s=>s.Record is not AssEvent)
        .Select(s=>s.Record is AssStyle style?"Format: "+string.Join(',',style.FieldNames)+"\n"+style.Serialize()+s.Ending:(s.Record?.Serialize()??s.Raw)+s.Ending));
    public static string Snapshot(AssEvent line,string text)
    {
        var snapshot=line.Clone();snapshot.Text=text;
        return "[Events]\nFormat: "+string.Join(',',snapshot.FieldNames)+"\n"+snapshot.Serialize()+"\n";
    }
    public static string Track(AssDocument document,AssEvent line,string text,double x,double y)
    {
        if(!document.Events.Contains(line))throw new ArgumentException("Line does not belong to document.");
        return Track(Header(document),Snapshot(line,text),x,y);
    }
    // Only immutable source snapshots are used by the background shaper.
    public static string Track(string header,string snapshot,double x,double y)
    {
        var clone=AssDocument.Parse(header+"\n"+snapshot);var target=clone.Events.Single();var text=target.Text;
        var strip=new HashSet<string>(StringComparer.Ordinal){"pos","move","org","fr","frx","fry","frz","fscx","fscy","fsc","fax","fay","distort","clip","iclip","clippos","clips","bord","xbord","ybord","shad","xshad","yshad","blur","be","alpha","1a","2a","3a","4a","fad","fade","t","wtan","ctan","c","1c","2c","3c","4c","1grd","2grd","3grd","4grd"};
        foreach(var tag in AssSyntax.Tags(text).Reverse())if(strip.Contains(tag.Name))text=text[..tag.Start]+text[tag.End..];
        // Reassert neutral paint and geometry after every reset and at each run.
        const string neutral="\\frx0\\fry0\\frz0\\fax0\\fay0\\fscx100\\fscy100\\bord0\\shad0\\blur0\\be0\\alpha&H00&\\1c&HFFFFFF&";
        foreach(var tag in AssSyntax.Tags(text).Where(t=>t.Name=="r").Reverse())text=text[..tag.End]+neutral+text[tag.End..];
        target.Text="{\\pos("+x.ToString(System.Globalization.CultureInfo.InvariantCulture)+","+y.ToString(System.Globalization.CultureInfo.InvariantCulture)+")"+neutral+"}"+text;
        target.Set("Start","0:00:00.00");target.Set("End","0:00:10.00");target.Set("Effect","");
        clone.Touch();return clone.Serialize();
    }
}
