namespace Yoake.Core.Subtitles;

public static class VisualMeasurement
{
    // A detached measurement track; none of these changes touch the editor.
    // Keep shaping/font/run information, remove geometry and paint effects.
    public static string Track(AssDocument document,AssEvent line,string text,double width,double height)
    {
        if(!document.Events.Contains(line))throw new ArgumentException("Line does not belong to document.");
        var source=string.Concat(document.Source.Where(s=>s.Record is not AssEvent e||ReferenceEquals(e,line)).Select(s=>(s.Record?.Serialize()??s.Raw)+s.Ending));
        var clone=AssDocument.Parse(source);var target=clone.Events[0];
        var strip=new HashSet<string>(StringComparer.Ordinal){"pos","move","org","fr","frx","fry","frz","fscx","fscy","fsc","fax","fay","distort","clip","iclip","clippos","clips","bord","xbord","ybord","shad","xshad","yshad","blur","be","alpha","1a","2a","3a","4a","fad","fade","t","wtan","ctan"};
        foreach(var tag in AssSyntax.Tags(text).Reverse())if(strip.Contains(tag.Name))text=text[..tag.Start]+text[tag.End..];
        // Reassert neutral paint and geometry after every reset and at each run.
        const string neutral="\\frx0\\fry0\\frz0\\fax0\\fay0\\fscx100\\fscy100\\bord0\\shad0\\blur0\\be0\\alpha&H00&\\1c&HFFFFFF&";
        foreach(var tag in AssSyntax.Tags(text).Where(t=>t.Name=="r").Reverse())text=text[..tag.End]+neutral+text[tag.End..];
        target.Text="{\\pos("+width.ToString(System.Globalization.CultureInfo.InvariantCulture)+","+height.ToString(System.Globalization.CultureInfo.InvariantCulture)+")"+neutral+"}"+text;
        target.Set("Start","0:00:00.00");target.Set("End","0:00:10.00");target.Set("Effect","");
        clone.Touch();return clone.Serialize();
    }
}
