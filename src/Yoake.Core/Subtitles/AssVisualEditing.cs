using System.Globalization;

namespace Yoake.Core.Subtitles;

public static partial class AssVisualTags
{
    public static AssMove? Move(string text)
    {
        var tag=Scan(text).FirstOrDefault(t=>t.Name=="move");var n=Numbers(tag.Arguments);
        return n is {Length:4 or 6}?new(new(n[0],n[1]),new(n[2],n[3]),n.Length==6?n[4]:null,n.Length==6?n[5]:null):null;
    }
    private static double[]? Numbers(string? value)
    {
        if(value is null)return null;var parts=value.Split(',');var result=new double[parts.Length];
        for(var i=0;i<parts.Length;i++)if(!TryNumber(parts[i],out result[i]))return null;
        return result;
    }
    public static AssPoint? Origin(string text)
    {
        var n=Numbers(Scan(text).FirstOrDefault(t=>t.Name=="org").Arguments);
        return n is {Length:2}?new(n[0],n[1]):null;
    }
    public static AssPoint DefaultPosition(AssEvent line,AssStyle? style,double width,double height,string? text=null)
    {
        double Margin(string name)=>TryNumber(line.Get(name),out var m)&&m>0?m:TryNumber(style?.Get(name)??"",out m)?m:20;
        var alignment=Alignment(text??line.Text,int.TryParse(style?.Get("Alignment"),out var a)&&a is >=1 and <=9?a:2);
        var col=(alignment-1)%3;var row=(alignment-1)/3;
        return new(col==0?Margin("MarginL"):col==2?width-Margin("MarginR"):(width+Margin("MarginL")-Margin("MarginR"))/2,
            row==0?height-Margin("MarginV"):row==2?Margin("MarginV"):height/2);
    }
    public static string SetOrigin(string text,AssPoint point)=>SetTuple(text,"org",[point.X,point.Y],true);
    public static string SetMove(string text,AssMove move)
    {
        var values=new List<double>{move.Start.X,move.Start.Y,move.End.X,move.End.Y};
        if(move.StartTime is {} a&&move.EndTime is {} b){values.Add(a);values.Add(b);}
        return SetTuple(text,"move",values,true);
    }
    public static string ShiftPosition(string text,AssPoint fallback,AssPoint delta)
    {
        string result;
        if(Move(text) is {} move)result=SetMove(text,move with{Start=VisualGeometry.Add(move.Start,delta),End=VisualGeometry.Add(move.End,delta)});
        else result=SetPosition(text,VisualGeometry.Add(Position(text)??fallback,delta));
        if(Origin(text) is {} origin)result=SetOrigin(result,VisualGeometry.Add(origin,delta));
        return result;
    }
    public static string MoveEndpoint(string text,int endpoint,AssPoint delta,double time,double duration)
    {
        var move=Move(text)??throw new InvalidOperationException("Unsupported movement coordinates.");
        var start=move.StartTime??0;var end=move.EndTime??duration;
        return SetMove(text,endpoint==0?move with{Start=VisualGeometry.Add(move.Start,delta),StartTime=Math.Min(time,end),EndTime=end}:
            move with{End=VisualGeometry.Add(move.End,delta),StartTime=start,EndTime=Math.Max(start,time)});
    }
    public static string ToggleMove(string text,AssPoint fallback,double frameStart,double frameEnd)
    {
        var tags=Scan(text);
        if(Move(text) is {} move)return Splice(text,tags.First(t=>t.Name=="move"),"\\pos("+Number(move.Start.X)+","+Number(move.Start.Y)+")");
        if(tags.Any(t=>t.Name=="move"))throw new InvalidOperationException("Unsupported movement coordinates.");
        var p=Position(text)??fallback;
        var tag=tags.FirstOrDefault(t=>t.Name=="pos");
        return Splice(text,tag,"\\move("+string.Join(',',new[]{p.X,p.Y,p.X,p.Y,frameStart,frameEnd}.Select(Number))+")");
    }
    private static string SetTuple(string text,string name,IEnumerable<double> values,bool first=false)
    {
        var tags=Scan(text).Where(t=>t.Name==name);var tag=first?tags.FirstOrDefault():tags.LastOrDefault();
        if(tag.Length>0&&Numbers(tag.Arguments) is null)throw new InvalidOperationException("This tag uses expressions or relative coordinates; preserve it in the text editor.");
        return Splice(text,tag,"\\"+name+"("+string.Join(',',values.Select(Number))+")");
    }
    // Initial run state. Later text runs and transforms are independent authoring
    // scopes; editing the line's primary transform never rewrites their contents.
    private static IEnumerable<AssSyntaxTagSpan> InitialTags(string text)
    {
        var end=0;while(end<text.Length&&text[end]=='{'){var close=text.IndexOf('}',end);if(close<0)break;end=close+1;}
        return AssSyntax.Tags(text).TakeWhile(t=>t.Start<end);
    }
    public static double Scalar(string text,string name,double fallback)
    {
        var value=fallback;
        foreach(var tag in InitialTags(text))
        {
            if(tag.Name=="r")value=fallback;
            if(tag.Name==name||name=="frz"&&tag.Name=="fr")
            {
                var raw=text[tag.ValueStart..tag.End].Trim();var explicitRelative=raw.StartsWith('~');var relative=explicitRelative||((name is "fscx" or "fscy")&&raw.Length>0&&(raw[0] is '+' or '-'));
                if(TryNumber(explicitRelative?raw[1..]:raw,out var n))value=relative?value+n:n;
                if(name is "fscx" or "fscy")value=Math.Max(0,value);
            }
        }
        return value;
    }
    public static AssTransform Transform(string text,AssStyle? style)
    {
        double Style(string field,double fallback)=>TryNumber(style?.Get(field)??"",out var n)?n:fallback;
        return new(Scalar(text,"frx",0),Scalar(text,"fry",0),Scalar(text,"frz",Style("Angle",0)),
            Scalar(text,"fscx",Style("ScaleX",100)),Scalar(text,"fscy",Style("ScaleY",100)),Scalar(text,"fax",0),Scalar(text,"fay",0));
    }
    public static string SetScalar(string text,string name,double value)
    {
        if(!double.IsFinite(value))throw new ArgumentOutOfRangeException(nameof(value));
        var initial=InitialTags(text).ToArray();var reset=initial.LastOrDefault(t=>t.Name=="r");
        var tag=initial.LastOrDefault(t=>t.Start>=reset.End&&(t.Name==name||name=="frz"&&t.Name=="fr"));
        if(tag.Name is not null)return text[..tag.Start]+"\\"+name+Number(value)+text[tag.End..];
        var addition="\\"+name+Number(value);
        if(reset.Name is not null)return text[..reset.End]+addition+text[reset.End..];
        return Splice(text,default,addition);
    }
    public static AssPoint ClipOffset(string text,double time=0,double duration=0)
    {
        var current=new AssPoint(0,0);
        void Apply(string source,double weight)
        {
            foreach(var tag in Scan(source))
            {
                if(tag.Name=="r")current=VisualGeometry.Lerp(current,new(0,0),weight);
                if(tag.Name=="clippos")
                {
                    var parts=tag.Arguments.Split(',');if(parts.Length!=2)continue;
                    double Operand(string raw,double baseline,bool y){raw=raw.Trim();return raw.StartsWith('~')&&TryNumber(raw[1..],out var d)?baseline+(y?-d:d):TryNumber(raw,out d)?d:double.NaN;}
                    var target=new AssPoint(Operand(parts[0],current.X,false),Operand(parts[1],current.Y,true));
                    if(double.IsFinite(target.X)&&double.IsFinite(target.Y))current=VisualGeometry.Lerp(current,target,weight);
                }
                if(tag.Name=="t")
                {
                    var start=tag.Arguments.IndexOf('\\');if(start<0)continue;
                    var prefix=tag.Arguments[..start].Trim().TrimEnd(',');var n=prefix.Length==0?Array.Empty<double>():Numbers(prefix);
                    if(n is null||n.Length>3)continue;var a=n.Length>=2?n[0]:0;var b=n.Length>=2?n[1]:duration;var accel=n.Length==1?n[0]:n.Length==3?n[2]:1;
                    var p=time<=a?0:time>=b||b<=a?1:Math.Pow((time-a)/(b-a),accel);
                    Apply("{"+tag.Arguments[start..]+"}",weight*p);
                }
            }
        }
        Apply(text,1);return current;
    }
    private static string TranslateClipOffset(string text,AssPoint delta)
    {
        // Append an explicit relative offset after the existing static and animated state.
        // Transform payloads remain byte-identical; geometry is never baked.
        var tag=Scan(text).LastOrDefault(t=>t.Name is "clippos" or "clips" or "t" or "r");
        var addition="\\clippos(~"+(delta.X>=0?"+":"")+Number(delta.X)+",~"+(-delta.Y>=0?"+":"")+Number(-delta.Y)+")";
        return tag.Length==0?Splice(text,default,addition):text[..(tag.Start+tag.Length)]+addition+text[(tag.Start+tag.Length)..];
    }
    public static string InvertClip(string text)
    {
        var tag=Scan(text).LastOrDefault(t=>t.Name is "clip" or "iclip");
        if(tag.Length==0)return text;return Splice(text,tag,"\\"+(tag.Name=="clip"?"iclip":"clip")+"("+tag.Arguments+")");
    }
    public static IReadOnlyList<AssPoint>? Distort(string text)
    {
        var tag=Scan(text).LastOrDefault(t=>t.Name=="distort");var n=Numbers(tag.Arguments);
        return n is {Length:6 or 8}?[n.Length==8?new(n[6],n[7]):new(0,0),new(n[0],n[1]),new(n[2],n[3]),new(n[4],n[5])]:null;
    }
    public static string SetDistort(string text,IReadOnlyList<AssPoint> points)
    {
        if(points.Count!=4)throw new ArgumentException("Four corners required.",nameof(points));
        return SetTuple(text,"distort",new[]{1,2,3,0}.SelectMany(i=>new[]{points[i].X,points[i].Y}));
    }
    public static string SetVector(string text,bool inverse,int scale,string drawing)
    {
        if(scale is <1 or >30)throw new ArgumentOutOfRangeException(nameof(scale));
        var tag=Scan(text).LastOrDefault(t=>t.Name is "clip" or "iclip");
        var explicitScale=scale!=1||tag.Arguments?.Contains(',')==true&&Clip(text)?.Rectangular==false;
        return Splice(text,tag,"\\"+(inverse?"iclip":"clip")+"("+(explicitScale?scale.ToString(CultureInfo.InvariantCulture)+",":"")+drawing+")");
    }
}
