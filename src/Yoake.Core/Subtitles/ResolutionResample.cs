// Algorithms adapted from Aegisub 3.5 resolution_resampler.cpp.
// Copyright (c) 2013 Thomas Goyne. ISC permission:
// Permission to use, copy, modify, and distribute this software for any
// purpose with or without fee is hereby granted, provided that the above
// copyright notice and this permission notice appear in all copies.
// THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
// WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
// MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
// ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
// WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION
// OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN
// CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
using System.Globalization;
using System.Text;

namespace Yoake.Core.Subtitles;

public enum ResampleAspect { Stretch, AddBorders, RemoveBorders, Manual }
public sealed record ResampleOptions(int SourceWidth,int SourceHeight,int Width,int Height,ResampleAspect Aspect=ResampleAspect.Stretch,int Left=0,int Right=0,int Top=0,int Bottom=0);
public sealed record ResampleWarning(string Location,string Tag,string Reason)
{ public override string ToString()=>$"{Location} · {Tag}: {Reason}"; }
public sealed record ResampleGeometry(double X,double Y,double Stretch,int Left,int Right,int Top,int Bottom)
{
    public static ResampleGeometry Calculate(ResampleOptions options)
    {
        if(options.SourceWidth is <1 or >999999||options.SourceHeight is <1 or >999999||options.Width is <1 or >999999||options.Height is <1 or >999999||!Enum.IsDefined(options.Aspect)||
            new[]{options.Left,options.Right,options.Top,options.Bottom}.Any(v=>v is <-9999 or >9999))throw new ArgumentException("Dimensions must be 1–999999; manual margins must be −9999–9999.");
        var left=options.Left;var right=options.Right;var top=options.Top;var bottom=options.Bottom;var stretch=1d;
        var oldAspect=options.SourceWidth/(double)options.SourceHeight;var newAspect=options.Width/(double)options.Height;
        if(options.Aspect!=ResampleAspect.Manual)left=right=top=bottom=0;
        if(Math.Abs(oldAspect-newAspect)/newAspect>.01)
        {
            if(options.Aspect is ResampleAspect.AddBorders or ResampleAspect.RemoveBorders)
            {
                var horizontal=newAspect>oldAspect;if(options.Aspect==ResampleAspect.RemoveBorders)horizontal=!horizontal;
                if(horizontal)left=right=(int)((options.SourceHeight*newAspect-options.SourceWidth)/2);
                else top=bottom=(int)((options.SourceWidth/newAspect-options.SourceHeight)/2);
            }
            else if(options.Aspect==ResampleAspect.Stretch)stretch=newAspect/oldAspect;
            else
            {
                var w=options.SourceWidth+left+right;var h=options.SourceHeight+top+bottom;
                if(w<=0||h<=0)throw new ArgumentException("Margin offsets must leave positive source dimensions.");
                oldAspect=w/(double)h;if(Math.Abs(oldAspect-newAspect)/newAspect>.01)stretch=newAspect/oldAspect;
            }
        }
        var sourceX=options.SourceWidth+left+right;var sourceY=options.SourceHeight+top+bottom;
        if(sourceX<=0||sourceY<=0)throw new ArgumentException("Margin offsets must leave positive source dimensions.");
        return new(options.Width/(double)sourceX,options.Height/(double)sourceY,stretch,left,right,top,bottom);
    }
}

public sealed class ResampleSnapshot
{
    internal sealed record Row(AssRecord Target,string[] Names,string[] Fields,int Index,bool Style,bool Comment,string Effect);
    internal SubtitleEditor Editor {get;}
    internal long Revision {get;}
    internal Row[] Rows {get;}
    internal AssEvent? DraftLine {get;}
    internal string[]? DraftFields {get;}
    private readonly Dictionary<string,string> _info;
    internal ResampleSnapshot(SubtitleEditor editor,Row[] rows,Dictionary<string,string> info,AssEvent? draftLine,string[]? draftFields)
    {Editor=editor;Revision=editor.Document.Revision;Rows=rows;_info=info;DraftLine=draftLine;DraftFields=draftFields;}
    public (int Width,int Height) Resolution
    {
        get
        {
            int Read(string key)=>int.TryParse(_info[key],out var n)&&n>0?n:0;
            var x=Read("PlayResX");var y=Read("PlayResY");
            if(x==0&&y==0)return(384,288);if(x==0)x=y==1024?1280:y*4/3;if(y==0)y=x==1280?1024:x*3/4;return(x,y);
        }
    }
    public ResamplePlan Calculate(ResampleOptions options,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();var geometry=ResampleGeometry.Calculate(options);var edits=new List<ResamplePlan.Edit>();var warnings=new List<ResampleWarning>();
        var identity=geometry.X==1&&geometry.Y==1&&geometry.Stretch==1&&geometry.Left==0&&geometry.Right==0&&geometry.Top==0&&geometry.Bottom==0;
        foreach(var row in Rows)
        {
            token.ThrowIfCancellationRequested();if(identity)continue;
            if(row.Comment&&(row.Effect.StartsWith("template",StringComparison.Ordinal)||row.Effect.StartsWith("code",StringComparison.Ordinal)))continue;
            var values=(string[])row.Fields.Clone();
            var location=row.Style?"Style "+Read(row,"Name"):$"Line {row.Index+1}";
            void Scale(string field,double factor,double shift=0,bool integer=false,bool inherited=false)
            {
                var index=Array.FindIndex(row.Names,n=>n.Equals(field,StringComparison.OrdinalIgnoreCase));if(index<0)return;
                var source=values.ElementAtOrDefault(index)??"";
                if(!double.TryParse(source,NumberStyles.Float,CultureInfo.InvariantCulture,out var number)||!double.IsFinite(number))
                {warnings.Add(new(location,field,"Invalid numeric field preserved."));return;}
                if(inherited&&number==0)return;
                var changed=(number+shift)*factor;if(integer)changed=Math.Truncate(changed+.5);
                if(!double.IsFinite(changed)||Math.Abs(changed)>int.MaxValue){warnings.Add(new(location,field,"Result is outside the supported numeric range; preserved."));return;}
                if(changed!=number)values[index]=changed.ToString("0.#########",CultureInfo.InvariantCulture);
            }
            Scale("MarginL",geometry.X,geometry.Left,true,!row.Style);Scale("MarginR",geometry.X,geometry.Right,true,!row.Style);Scale("MarginV",geometry.Y,geometry.Top,true,!row.Style);
            if(row.Style)
            {Scale("Fontsize",geometry.Y,integer:true);Scale("Outline",geometry.Y);Scale("Shadow",geometry.Y);Scale("Spacing",geometry.Y);Scale("ScaleX",geometry.Stretch);}
            else
            {
                var index=Array.FindIndex(row.Names,n=>n.Equals("Text",StringComparison.OrdinalIgnoreCase));
                if(index>=0)values[index]=ResampleTags.Transform(values[index],geometry,location,warnings,token);
            }
            if(!values.SequenceEqual(row.Fields))edits.Add(new(row,values));
        }
        var info=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        if(!identity||Resolution!=(options.Width,options.Height))
        {
            info["PlayResX"]=options.Width.ToString(CultureInfo.InvariantCulture);info["PlayResY"]=options.Height.ToString(CultureInfo.InvariantCulture);
            if(!identity&&int.TryParse(_info["LayoutResX"],out var lx)&&int.TryParse(_info["LayoutResY"],out var ly)&&lx>0&&ly>0)
            {
                // The supplied 3.5 algorithm uses the original source width here.
                var newY=ly+Math.Round(ly*(geometry.Top+geometry.Bottom)/(double)options.SourceWidth,MidpointRounding.AwayFromZero);
                var newX=Math.Round(lx*(newY/ly)*(options.Width/(double)options.Height)/(options.SourceWidth/(double)options.SourceHeight),MidpointRounding.AwayFromZero);
                if(newX<=0||newY<=0||newX>int.MaxValue||newY>int.MaxValue)throw new ArgumentException("The border policy would create an invalid LayoutRes.");
                info["LayoutResX"]=newX.ToString(CultureInfo.InvariantCulture);info["LayoutResY"]=newY.ToString(CultureInfo.InvariantCulture);
            }
        }
        foreach(var pair in info.ToArray())if(_info[pair.Key]==pair.Value)info.Remove(pair.Key);
        return new(this,options,geometry,edits.ToArray(),info,warnings.ToArray());
    }
    private static string Read(Row row,string field)=>row.Fields.ElementAtOrDefault(Array.FindIndex(row.Names,n=>n.Equals(field,StringComparison.OrdinalIgnoreCase)))??"";
}
public sealed class ResamplePlan
{
    internal sealed record Edit(ResampleSnapshot.Row Row,string[] Fields);
    internal ResampleSnapshot Snapshot {get;}
    internal Edit[] Edits {get;}
    internal Dictionary<string,string> Info {get;}
    public ResampleOptions Options {get;}
    public ResampleGeometry Geometry {get;}
    public IReadOnlyList<ResampleWarning> Warnings {get;}
    public int ChangedEvents=>Edits.Count(e=>!e.Row.Style);
    public int ChangedStyles=>Edits.Count(e=>e.Row.Style);
    public bool HasChanges=>Edits.Length>0||Info.Count>0;
    public string Summary=>$"{Options.SourceWidth}×{Options.SourceHeight} → {Options.Width}×{Options.Height}; X ×{Geometry.X:0.####}, Y ×{Geometry.Y:0.####}, text stretch ×{Geometry.Stretch:0.####}. {ChangedEvents} lines, {ChangedStyles} styles; {Warnings.Count} preserved-field/tag warnings. Colors, times, karaoke and unrelated metadata are retained.";
    internal ResamplePlan(ResampleSnapshot snapshot,ResampleOptions options,ResampleGeometry geometry,Edit[] edits,Dictionary<string,string> info,ResampleWarning[] warnings)
    {(Snapshot,Options,Geometry,Edits,Info,Warnings)=(snapshot,options,geometry,edits,info,warnings);}
}

internal static class ResampleTags
{
    private static readonly HashSet<string> SizeX=["xbord","xshad","ctx","boxpx"];
    private static readonly HashSet<string> SizeY=["fs","fsp","bord","shad","ybord","yshad","pbo","cty","boxpy"];
    private static readonly HashSet<string> UnresolvedGeometry=["mover","z","furipos","furifsp","vsp","vcolsp","colsp","rndx","rndy","rndz","jitter0","movevc","moves3","moves4","perspective"];
    public static string Transform(string source,ResampleGeometry geometry,string location,List<ResampleWarning> warnings,CancellationToken token,int depth=0)
    {
        if(depth>32){warnings.Add(new(location,"t","Excessive nesting preserved."));return source;}
        var replacements=new List<(int Start,int Length,string Value)>();var tags=AssSyntax.Tags(source).ToArray();
        void Warn(string tag,string reason)=>warnings.Add(new(location,"\\"+tag,reason));
        foreach(var tag in tags)
        {
            token.ThrowIfCancellationRequested();var value=source[tag.ValueStart..tag.End];var first=0;var last=value.Length;
            while(first<last&&char.IsWhiteSpace(value[first]))first++;while(last>first&&char.IsWhiteSpace(value[last-1]))last--;
            var parenthesized=first<last&&value[first]=='('&&value[last-1]==')';
            var args=parenthesized?value[(first+1)..(last-1)]:value[first..last];string? changed=null;
            string? Scalar(string raw,double factor,double shift=0,bool relativeSize=false,bool integer=false)
            {
                var trimmed=raw.Trim();var relative=trimmed.StartsWith('~');var numeric=relative?trimmed[1..]:trimmed;
                if(!double.TryParse(numeric,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)||!double.IsFinite(n))return null;
                if(relative&&!(numeric.StartsWith('+')||numeric.StartsWith('-')))return null;
                var result=(n+(relative?0:shift))*factor;if(integer)result=Math.Truncate(result+.5);
                if(!double.IsFinite(result)||Math.Abs(result)>int.MaxValue)return null;if(result==n)return raw;
                var formatted=result.ToString("0.#########",CultureInfo.InvariantCulture);
                if(relative)formatted="~"+(result>=0?"+":"")+formatted;
                else if((relativeSize||shift==0)&&numeric.StartsWith('+')&&result>=0)formatted="+"+formatted;
                var leading=raw.Length-raw.TrimStart().Length;var trailing=raw.Length-raw.TrimEnd().Length;
                return raw[..leading]+formatted+raw[(raw.Length-trailing)..];
            }
            string? Tuple(int pairs,int[] allowed,bool position)
            {
                var parts=Split(args);if(!parenthesized||!allowed.Contains(parts.Length))return null;
                for(var i=0;i<pairs*2;i++)
                {
                    var rewritten=Scalar(parts[i],i%2==0?geometry.X:geometry.Y,position?(i%2==0?geometry.Left:geometry.Top):0);
                    if(rewritten is null)return null;parts[i]=rewritten;
                }
                return string.Join(',',parts);
            }
            if(tag.Name=="t")
            {
                var slash=args.IndexOf('\\');
                if(parenthesized&&slash>=0){var inner=Transform("{"+args[slash..]+"}",geometry,location,warnings,token,depth+1);changed=args[..slash]+inner[1..^1];}
                else Warn(tag.Name,"Unrecognized transform preserved.");
            }
            else if(tag.Name is "pos" or "org")changed=Tuple(1,[2],true);
            else if(tag.Name=="move")changed=Tuple(2,[4,6],true);
            else if(tag.Name=="clippos")changed=Tuple(1,[2],false);
            else if(tag.Name is "clip" or "iclip")
            {
                var parts=Split(args);
                if(parenthesized&&parts.Length==4)
                {
                    for(var i=0;i<4;i++){var n=Scalar(parts[i],i%2==0?geometry.X:geometry.Y,i%2==0?geometry.Left:geometry.Top,integer:true);if(n is null){parts=[];break;}parts[i]=n;}
                    if(parts.Length==4)changed=string.Join(',',parts);
                }
                else if(parenthesized&&parts.Length is 1 or 2)
                {
                    var scale=1;if(parts.Length==1||int.TryParse(parts[0],out scale)&&scale is >=1 and <=30)
                    {
                        var drawing=Drawing(parts[^1],geometry.X,geometry.Y,geometry.Left,geometry.Top,scale);
                        if(drawing is not null)changed=parts.Length==1?drawing:parts[0]+","+drawing;
                    }
                }
            }
            else if(tag.Name is "pgrd" or "1pgrd")
            {var parts=Split(args);if(parts.Length>=7)changed=Tuple(2,[parts.Length],true);}
            else if(tag.Name=="ct")changed=Drawing(args,geometry.X,geometry.Y,0,0,1);
            else if(tag.Name=="distort")
            {
                var parts=Split(args);if(!parenthesized||parts.Length is not (6 or 8)||parts.Any(p=>!double.TryParse(p,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)||!double.IsFinite(n)))Warn(tag.Name,"Ambiguous corner pins preserved.");
                continue; // normalized local pins, deliberately no coordinate scaling
            }
            else if(tag.Name=="boxp"&&geometry.X!=geometry.Y)
            {
                var x=Scalar(args,geometry.X,relativeSize:true);var y=Scalar(args,geometry.Y,relativeSize:true);
                if(!parenthesized&&x is not null&&y is not null){replacements.Add((tag.Start,tag.End-tag.Start,"\\boxpx"+x+"\\boxpy"+y));continue;}
            }
            else if(tag.Name=="boxp")changed=Scalar(args,geometry.Y,relativeSize:true);
            else if(tag.Name=="boxr")
            {if(geometry.X==geometry.Y)changed=Scalar(args,geometry.Y,relativeSize:true);else{Warn(tag.Name,"A circular radius cannot represent anisotropic geometry; preserved.");continue;}}
            else if(SizeX.Contains(tag.Name))changed=Scalar(args,geometry.X,relativeSize:true);
            else if(SizeY.Contains(tag.Name))changed=Scalar(args,geometry.Y,relativeSize:true,integer:tag.Name=="pbo");
            else if(tag.Name=="fscx")changed=Scalar(args,geometry.Stretch,relativeSize:true);
            else
            {
                if(UnresolvedGeometry.Contains(tag.Name)||AssTagCatalog.Match((tag.Name+" ").AsSpan()).Kind==AssSyntaxKind.Unknown)Warn(tag.Name,"Unsupported extension geometry is preserved; review this tag.");
                continue;
            }
            if(changed is null){Warn(tag.Name,"Unsupported argument or expression preserved.");continue;}
            if(changed==args)continue;
            var replacement=parenthesized?value[..(first+1)]+changed+value[(last-1)..]:value[..first]+changed+value[last..];
            replacements.Add((tag.ValueStart,tag.End-tag.ValueStart,replacement));
        }
        // Drawing bodies are separate from override parameters. Reuse the
        // shared syntax scanner for block boundaries and drawing-level state.
        var blocks=new List<(int Start,int End)>();var opening=-1;
        foreach(var syntax in AssSyntax.Tokenize(source).Where(t=>t.Kind==AssSyntaxKind.Brace))
        {if(source[syntax.Start]=='{')opening=syntax.Start;else if(opening>=0){blocks.Add((opening,syntax.Start+1));opening=-1;}}
        if(opening>=0)blocks.Add((opening,source.Length));
        var level=0;var cursor=0;
        foreach(var block in blocks.Append((source.Length,source.Length)))
        {
            token.ThrowIfCancellationRequested();
            if(level>0&&block.Start>cursor)
            {
                var drawing=source[cursor..block.Start];var changed=Drawing(drawing,geometry.X/geometry.Stretch,geometry.Y,0,0,level);
                if(changed is null)Warn("p","Unsupported drawing body preserved.");else if(changed!=drawing)replacements.Add((cursor,drawing.Length,changed));
            }
            foreach(var tag in tags.Where(t=>t.BlockStart==block.Start))
            {if(tag.Name=="p"&&int.TryParse(source[tag.ValueStart..tag.End].Trim(),out var n))level=n;if(tag.Name=="r")level=0;}
            cursor=block.End;
        }
        var result=new StringBuilder(source);foreach(var replacement in replacements.OrderByDescending(r=>r.Start))result.Remove(replacement.Start,replacement.Length).Insert(replacement.Start,replacement.Value);return result.ToString();
    }
    private static string[] Split(string source)
    {
        var parts=new List<string>();var begin=0;var depth=0;
        for(var i=0;i<source.Length;i++){if(source[i]=='(')depth++;else if(source[i]==')')depth--;else if(source[i]==','&&depth==0){parts.Add(source[begin..i]);begin=i+1;}}
        parts.Add(source[begin..]);return parts.ToArray();
    }
    private static string? Drawing(string drawing,double x,double y,double left,double top,int scale)
    {
        if(AssVectorPath.Parse(drawing.ToLowerInvariant(),scale) is null)return null;
        var factor=Math.Pow(2,scale-1);var changes=new List<(int Start,int Length,string Value)>();var number=0;
        for(var i=0;i<drawing.Length;)
        {
            if(!(char.IsAsciiDigit(drawing[i])||drawing[i] is '+' or '-' or '.')){i++;continue;}
            var start=i++;while(i<drawing.Length&&(char.IsAsciiDigit(drawing[i])||drawing[i]=='.'))i++;
            var old=drawing[start..i];if(!double.TryParse(old,NumberStyles.Float,CultureInfo.InvariantCulture,out var n))return null;
            var value=(n+(number%2==0?left:top)*factor)*(number++%2==0?x:y);
            if(!double.IsFinite(value)||Math.Abs(value)>int.MaxValue)return null;
            if(value!=n)changes.Add((start,i-start,value.ToString("0.#########",CultureInfo.InvariantCulture)));
        }
        var output=new StringBuilder(drawing);foreach(var change in changes.AsEnumerable().Reverse())output.Remove(change.Start,change.Length).Insert(change.Start,change.Value);return output.ToString();
    }
}

public sealed partial class SubtitleEditor
{
    public ResampleSnapshot CaptureResample(AssEvent? draftLine=null,IReadOnlyDictionary<string,string>? draft=null)
    {
        AssEvent? probe=null;string[]? baseline=null;
        if(draft is not null){if(draftLine is null)throw new ArgumentException("The edit draft needs its owning row.");Require(draftLine);probe=ValidateEventValues(draftLine,draft);baseline=(string[])draftLine.Fields.Clone();}
        var rows=Document.Styles.Select((s,i)=>new ResampleSnapshot.Row(s,(string[])s.Format.Clone(),(string[])s.Fields.Clone(),i,true,false,""))
            .Concat(Document.Events.Select((e,i)=>new ResampleSnapshot.Row(e,(string[])e.Format.Clone(),ReferenceEquals(e,draftLine)&&probe is not null?(string[])probe.Fields.Clone():(string[])e.Fields.Clone(),i,false,e.IsComment,e.Effect))).ToArray();
        var info=new[]{"PlayResX","PlayResY","LayoutResX","LayoutResY"}.ToDictionary(k=>k,Document.GetScriptInfo,StringComparer.OrdinalIgnoreCase);
        return new(this,rows,info,draftLine,baseline);
    }
    public void ApplyResample(ResamplePlan plan,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();var snapshot=plan.Snapshot;
        var owned=Document.Styles.Cast<AssRecord>().Concat(Document.Events).ToHashSet();
        if(!ReferenceEquals(snapshot.Editor,this)||snapshot.Revision!=Document.Revision||snapshot.Rows.Any(r=>!owned.Contains(r.Target)))throw new InvalidOperationException("The subtitle source changed while resampling. Nothing was changed.");
        if(snapshot.DraftLine is {} line&&snapshot.DraftFields is {} fields&&!line.Fields.SequenceEqual(fields))throw new InvalidOperationException("The draft's stored row changed. Nothing was changed.");
        if(!plan.HasChanges)return;
        var edits=plan.Edits.ToDictionary(e=>e.Row.Target);
        using var update=Document.BeginUpdate();using var transaction=Undo.BeginTransaction("Resample resolution");
        foreach(var row in snapshot.Rows)
        {
            token.ThrowIfCancellationRequested();var values=edits.GetValueOrDefault(row.Target)?.Fields??(ReferenceEquals(row.Target,snapshot.DraftLine)?row.Fields:null);if(values is null||values.SequenceEqual(row.Target.Fields))continue;
            Undo.Execute(new FieldEditOperation("Resample resolution",row.Target,(string[])row.Target.Fields.Clone(),(string[])values.Clone()));
        }
        if(plan.Info.Count>0)SetScriptInfo(plan.Info);token.ThrowIfCancellationRequested();transaction.Commit();
    }
}
