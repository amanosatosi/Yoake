using System.Globalization;

namespace Yoake.Core.Subtitles;

public readonly record struct CylindricalColor(double Hue, double Saturation, double Component);
public static class ColorSpace
{
    public static CylindricalColor Hsv(AssColor c)
    {
        var r=c.Red/255d;var g=c.Green/255d;var b=c.Blue/255d;
        var max=Math.Max(r,Math.Max(g,b));var min=Math.Min(r,Math.Min(g,b));var d=max-min;
        var h=d==0?0:max==r?60*((g-b)/d%6):max==g?60*((b-r)/d+2):60*((r-g)/d+4);
        return new((h+360)%360,max==0?0:d/max,max);
    }
    public static CylindricalColor Hsl(AssColor c)
    {
        var hsv=Hsv(c);var l=hsv.Component*(1-hsv.Saturation/2);
        return new(hsv.Hue,l is 0 or 1?0:(hsv.Component-l)/Math.Min(l,1-l),l);
    }
    public static AssColor FromHsv(double h,double s,double v,byte transparency=0)
    {
        h=(h%360+360)%360;s=Math.Clamp(s,0,1);v=Math.Clamp(v,0,1);
        var c=v*s;var x=c*(1-Math.Abs(h/60%2-1));var m=v-c;
        var (r,g,b)=h switch{<60=>(c,x,0d),<120=>(x,c,0d),<180=>(0d,c,x),<240=>(0d,x,c),<300=>(x,0d,c),_=>(c,0d,x)};
        return new(Byte(r+m),Byte(g+m),Byte(b+m),transparency);
    }
    public static AssColor FromHsl(double h,double s,double l,byte transparency=0)
    {
        s=Math.Clamp(s,0,1);l=Math.Clamp(l,0,1);var v=l+s*Math.Min(l,1-l);
        return FromHsv(h,v==0?0:2*(1-l/v),v,transparency);
    }
    public static AssColor Spectrum(double hue,double x,double y,byte alpha=0)=>FromHsv(hue,Math.Clamp(x,0,1),1-Math.Clamp(y,0,1),alpha);
    public static string Html(AssColor c)=>$"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}";
    public static bool TryHtml(string? text,byte alpha,out AssColor color)
    {
        color=default;var hex=(text??"").Trim().TrimStart('#');
        if(hex.Length!=6||!uint.TryParse(hex,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var rgb))return false;
        color=new((byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb,alpha);return true;
    }
    private static byte Byte(double v)=>(byte)Math.Round(Math.Clamp(v,0,1)*255);
}
