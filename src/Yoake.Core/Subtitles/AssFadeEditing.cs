using System.Globalization;

namespace Yoake.Core.Subtitles;

// Static fades have first-valid-tag precedence. Preserve the other side and
// optional Mangetsu color/alpha extension; never edit tags inside transforms.
public sealed record AssFadeEdit(string Text,int Start=0,int End=0,int ReplacementLength=0,int RemovalStart=-1,int RemovalEnd=-1)
{
    public int Map(int offset)
    {
        if(RemovalStart>=0)offset=offset<=RemovalStart?offset:offset>=RemovalEnd?offset-(RemovalEnd-RemovalStart):RemovalStart;
        return Math.Clamp(offset<Start?offset:offset>=End?offset+ReplacementLength-(End-Start):Start+ReplacementLength,0,Text.Length);
    }
}
public static class AssFadeEditing
{
    public static long Duration(bool fromStart,long time,long start,long end)
    {
        if(end<=start)return -1;
        var duration=fromStart?time-start:end-time;
        return duration is >0 and <=int.MaxValue?duration:-1;
    }
    public static AssFadeEdit Set(string text,bool fromStart,long time,long start,long end,string? color=null)
    {
        var duration=Duration(fromStart,time,start,end);if(duration<0)return new(text);
        var tags=AssVisualTags.Scan(text).Where(t=>t.Name is "fad" or "fade").ToArray();
        var existing=tags.FirstOrDefault(t=>t.Name=="fad"&&TryRead(t.Arguments,out _,out _,out _,out _));
        var fadeIn=0L;var fadeOut=0L;var inColor="";var outColor="";
        if(existing.Length>0)TryRead(existing.Arguments,out fadeIn,out fadeOut,out inColor,out outColor);
        var old=fromStart?inColor:outColor;var next=color??"";
        if(next.Length>0&&old.EndsWith("+a",StringComparison.OrdinalIgnoreCase))next+=old[^2..];
        if(fromStart){fadeIn=duration;inColor=next;}else{fadeOut=duration;outColor=next;}
        var tag="\\fad("+fadeIn.ToString(CultureInfo.InvariantCulture)+","+fadeOut.ToString(CultureInfo.InvariantCulture)+(inColor.Length+outColor.Length==0?")":$",{inColor},{outColor})");
        var first=tags.FirstOrDefault(t=>t.Name=="fad"?TryRead(t.Arguments,out _,out _,out _,out _):t.Arguments.Split(',').Length==7);
        if(existing.Length>0&&first.Start==existing.Start)
            return new(text[..existing.Start]+tag+text[(existing.Start+existing.Length)..],existing.Start,existing.Start+existing.Length,tag.Length);
        if(first.Length>0)
        {
            if(existing.Length>0)
            {
                // Move the later short fade before the winning long fade;
                // do not create a second short fade or remove unrelated tags.
                var removeStart=existing.Start;var removeEnd=existing.Start+existing.Length;
                if(removeStart>0&&removeEnd<text.Length&&text[removeStart-1]=='{'&&text[removeEnd]=='}'){removeStart--;removeEnd++;}
                var removed=text.Remove(removeStart,removeEnd-removeStart);
                var insert=first.Start-(removeStart<first.Start?removeEnd-removeStart:0);
                return new(removed.Insert(insert,tag),insert,insert,tag.Length,removeStart,removeEnd);
            }
            return new(text.Insert(first.Start,tag),first.Start,first.Start,tag.Length);
        }
        var index=text.StartsWith('{')?1:0;var insertion=index==1?tag:"{"+tag+"}";
        return new(text[..index]+insertion+text[index..],index,index,insertion.Length);
    }
    private static bool TryRead(string args,out long fadeIn,out long fadeOut,out string inColor,out string outColor)
    {
        fadeIn=fadeOut=0;inColor=outColor="";var fields=args.Split(',');
        if(fields.Length is not (2 or 4)||!long.TryParse(fields[0],NumberStyles.Integer,CultureInfo.InvariantCulture,out fadeIn)||!long.TryParse(fields[1],NumberStyles.Integer,CultureInfo.InvariantCulture,out fadeOut))return false;
        fadeIn=Math.Max(0,fadeIn);fadeOut=Math.Max(0,fadeOut);
        if(fields.Length==4){inColor=fields[2].Trim();outColor=fields[3].Trim();}return true;
    }
}
