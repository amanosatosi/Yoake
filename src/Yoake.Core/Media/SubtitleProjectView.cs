using System.Globalization;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Media;

/// <summary>Aegisub project view fields use zero-based rows/frames and fractional zoom.</summary>
public sealed record SubtitleProjectView(int? ActiveLine, int? ScrollPosition, int? VideoPosition, double? VideoZoom, VideoAspect? Aspect)
{
    public static SubtitleProjectView Read(AssDocument document)
    {
        var values = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Active Line", "Scroll Position", "Video Position", "Video Zoom Percent", "Video AR Mode", "Video AR Value" };
        var inside = false;
        foreach(var line in document.Source)
        {
            if(line.IsSectionHeader){inside=line.Raw.Trim().Equals(SubtitleMediaLinks.Section,StringComparison.OrdinalIgnoreCase);continue;}
            if(!inside)continue;
            var colon=line.Raw.IndexOf(':');if(colon<=0)continue;
            var key=line.Raw[..colon].Trim();
            if(keys.Contains(key))values.TryAdd(key,line.Raw[(colon+1)..].Trim());
            if(values.Count==keys.Count)break;
        }
        int? Index(string key)=>int.TryParse(values.GetValueOrDefault(key),NumberStyles.Integer,CultureInfo.InvariantCulture,out var value)&&value>=0?value:null;
        double? Number(string key)=>double.TryParse(values.GetValueOrDefault(key),NumberStyles.Float,CultureInfo.InvariantCulture,out var value)&&double.IsFinite(value)?value:null;
        var mode=Index("Video AR Mode");var ratio=Number("Video AR Value");var zoom=Number("Video Zoom Percent");
        VideoAspect? aspect=mode switch
        {
            0=>new(VideoAspectKind.Default),1=>new(VideoAspectKind.Full),2=>new(VideoAspectKind.Wide),3=>new(VideoAspectKind.Cinematic),
            4 when ratio is >=.5 and <=5=>new(VideoAspectKind.Custom,ratio.Value),_=>null
        };
        return new(Index("Active Line"),Index("Scroll Position"),Index("Video Position"),
            zoom is >0?VideoViewState.ClampZoom(zoom.Value):null,aspect);
    }

    public static IReadOnlyDictionary<string,string> Capture(int activeLine,int scrollPosition,int? videoPosition,double? zoom,VideoAspect? aspect,double aspectRatio)
    {
        string Integer(int value)=>Math.Max(0,value).ToString(CultureInfo.InvariantCulture);
        string Decimal(double value)=>value.ToString("0.######",CultureInfo.InvariantCulture);
        var values=new Dictionary<string,string>
        {
            ["Active Line"]=Integer(activeLine),["Scroll Position"]=Integer(scrollPosition)
        };
        if(videoPosition is {} frame)values["Video Position"]=Integer(frame);
        if(zoom is >0 && double.IsFinite(zoom.Value))values["Video Zoom Percent"]=Decimal(zoom.Value);
        if(aspect is {} ar)
        {
            values["Video AR Mode"]=Integer((int)ar.Kind);
            if(double.IsFinite(aspectRatio)&&aspectRatio>0)values["Video AR Value"]=Decimal(aspectRatio);
        }
        return values;
    }
}
