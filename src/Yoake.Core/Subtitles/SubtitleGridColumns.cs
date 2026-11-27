namespace Yoake.Core.Subtitles;

// Size against the document, not the currently realized/visible rows.
public static class SubtitleGridColumns
{
    private static readonly string[] Headers=["#","L","Start","End","Style","Actor","Effect","ML","MR","MV"];
    private static bool NonZero(string value)=>!string.IsNullOrWhiteSpace(value)&&(!int.TryParse(value,out var n)||n!=0);
    public static double[] Calculate(IReadOnlyList<AssEvent> events,Func<string,double> measure,double scale,IReadOnlyList<double> overrides)
    {
        var visible=new[]{true,false,true,true,true,false,false,false,false,false};
        var widths=Headers.Select(h=>Math.Max(24*scale,measure(h)+12*scale)).ToArray();
        var cap=600*scale;
        var numericLengths=new int[10];
        var unique=new HashSet<string>?[10];
        for(var i=4;i<=6;i++)unique[i]=new(StringComparer.Ordinal);
        foreach(var line in events)
        {
            string[] values=[line.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),line.DisplayLayer,line.DisplayStart,line.DisplayEnd,line.DisplayStyle,line.DisplayActor,line.DisplayEffect,line.DisplayMarginL,line.DisplayMarginR,line.DisplayMarginV];
            for(var i=0;i<10;i++)
            {
                if(i is 1 or 7 or 8 or 9)visible[i]|=NonZero(values[i]);
                else if(i is 5 or 6)visible[i]|=!string.IsNullOrWhiteSpace(values[i]);
                if(i is >=4 and <=6)
                {
                    if(widths[i]<cap&&unique[i]!.Add(values[i]))
                        widths[i]=Math.Min(cap,Math.Max(widths[i],measure(values[i])+12*scale));
                }
                else numericLengths[i]=Math.Max(numericLengths[i],values[i].Length);
            }
        }
        // Time and integer values use tabular-sized representative strings.
        for(var i=0;i<10;i++)
        {
            if(!visible[i]){widths[i]=0;continue;}
            if(numericLengths[i]>0)widths[i]=Math.Min(cap,Math.Max(widths[i],measure(new string('8',Math.Min(numericLengths[i],64)))+12*scale));
            if(i<overrides.Count&&overrides[i]>0)widths[i]=overrides[i]*scale;
            widths[i]=Math.Ceiling(widths[i]);
        }
        return widths;
    }
}
