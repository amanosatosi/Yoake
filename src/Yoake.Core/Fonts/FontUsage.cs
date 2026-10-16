// Behavioral algorithms adapted from the supplied Aegisub source.
// Copyright (c) 2012 Thomas Goyne. ISC permission:
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
using Yoake.Core.Subtitles;

namespace Yoake.Core.Fonts;

public sealed record FontRequest(string Family,int Bold,bool Italic)
{
    public int Weight=>Bold==0?400:Bold==1?700:Math.Clamp(Bold,100,1000);
    public override string ToString()=>$"{Family} · weight {Weight}"+(Italic?" · italic":"");
}
public sealed record FontUsage(FontRequest Request,int[] Characters,string[] Styles,int[] Lines);
public sealed record FontRequirements(FontUsage[] Fonts,string[] Warnings);

/// <summary>Immutable font-relevant projection, never a second ASS document.</summary>
public sealed class FontUsageSnapshot
{
    private sealed record Style(string Name,FontRequest Font);
    private sealed record Line(string Style,string Text,bool Comment,int Number);
    private readonly Style[] _styles;
    private readonly Line[] _lines;
    public long Revision {get;}
    private FontUsageSnapshot(Style[] styles,Line[] lines,long revision)=>(_styles,_lines,Revision)=(styles,lines,revision);
    public static FontUsageSnapshot Capture(AssDocument document,AssEvent? draftLine=null,IReadOnlyDictionary<string,string>? draft=null)
    {
        if(draftLine is not null&&!document.Events.Contains(draftLine))throw new InvalidOperationException("The font draft belongs to another document.");
        var styles=document.Styles.Select(s=>new Style(s.Name,new(s.Get("Fontname"),Integer(s.Get("Bold")) is {} bold&&bold!=0?1:0,Integer(s.Get("Italic")) is {} italic&&italic!=0))).ToArray();
        var lines=document.Events.Select((e,i)=>new Line(ReferenceEquals(e,draftLine)&&draft is not null?draft.GetValueOrDefault("Style",e.Style):e.Style,
            ReferenceEquals(e,draftLine)&&draft is not null?NormalizeNewlines(draft.GetValueOrDefault("Text",e.Text)):e.Text,e.IsComment,i+1)).ToArray();
        return new(styles,lines,document.Revision);
    }
    private static string NormalizeNewlines(string text)=>text.Replace("\r\n","\\N",StringComparison.Ordinal).Replace("\r","\\N",StringComparison.Ordinal).Replace("\n","\\N",StringComparison.Ordinal);
    private static int? Integer(string text)
    {
        var value=text.AsSpan().Trim();var length=0;if(value.Length>0&&value[0] is '+' or '-')length++;
        while(length<value.Length&&char.IsAsciiDigit(value[length]))length++;
        return int.TryParse(value[..length],NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var n)?n:null;
    }
    public FontRequirements Scan(bool includeUnusedStyles=false,CancellationToken token=default)
    {
        var styles=new Dictionary<string,FontRequest>(StringComparer.Ordinal);foreach(var style in _styles)styles[style.Name]=style.Font;
        var usage=new Dictionary<(string Family,int Bold,bool Italic),(FontRequest Request,HashSet<int> Characters,HashSet<string> Styles,HashSet<int> Lines)>();var warnings=new List<string>();
        void Add(FontRequest request,string style,int line,IEnumerable<int> characters)
        {
            var family=request.Family.Trim();var key=(family.TrimStart('@').ToUpperInvariant(),request.Bold,request.Italic);
            if(!usage.TryGetValue(key,out var item))item=(request with{Family=family},[],[],[]);
            item.Styles.Add(style);if(line>0)item.Lines.Add(line);foreach(var character in characters)item.Characters.Add(character);usage[key]=item;
        }
        if(includeUnusedStyles)foreach(var style in styles){token.ThrowIfCancellationRequested();Add(style.Value,style.Key,0,[]);}
        foreach(var line in _lines)
        {
            token.ThrowIfCancellationRequested();if(line.Comment)continue;
            if(!styles.TryGetValue(line.Style,out var initial)){warnings.Add($"Line {line.Number}: style '{line.Style}' does not exist.");continue;}
            var current=initial;var reset=initial;var styleName=line.Style;var drawing=false;var tags=AssSyntax.Tags(line.Text).ToArray();var next=0;
            foreach(var syntax in AssSyntax.Tokenize(line.Text))
            {
                token.ThrowIfCancellationRequested();
                while(next<tags.Length&&tags[next].Start<=syntax.Start)
                {
                    var tag=tags[next++];if(tag.BlockEnd>=line.Text.Length)continue;var argument=line.Text[tag.ValueStart..tag.End].Trim();
                    switch(tag.Name)
                    {
                        case "r":
                            styleName=argument.Length==0?line.Style:argument;
                            if(!styles.TryGetValue(styleName,out var selected)){warnings.Add($"Line {line.Number}: reset style '{styleName}' does not exist; original style retained for analysis.");selected=initial;styleName=line.Style;}
                            current=reset=selected;drawing=false;break;
                        case "fn":current=current with{Family=argument.Length==0||argument=="0"?reset.Family:argument};break;
                        case "b":var bold=Integer(argument);current=current with{Bold=bold is {} b&&(b is 0 or 1||b>=100)?b:reset.Bold};break;
                        case "i":var italic=Integer(argument);current=current with{Italic=italic is 0 or 1?italic==1:reset.Italic};break;
                        case "p":drawing=Integer(argument)>0;break;
                    }
                }
                if(drawing)continue;
                var text=line.Text.AsSpan(syntax.Start,syntax.Length);
                if(syntax.Kind is AssSyntaxKind.Text or AssSyntaxKind.Furigana)
                {
                    var chars=new List<int>();foreach(var rune in text.EnumerateRunes())
                        if(syntax.Kind!=AssSyntaxKind.Furigana||rune.Value is not ('<' or '>' or '|'))chars.Add(rune.Value);
                    if(chars.Count>0)Add(current,styleName,line.Number,chars);
                }
                else if(syntax.Kind==AssSyntaxKind.Escape&&text.Length>=2)
                {
                    // The shared tokenizer coalesces adjacent escapes.
                    for(var i=0;i+1<text.Length;)
                    {
                        var escaped=text[i+1];if(escaped is not ('N' or 'n'))Add(current,styleName,line.Number,[escaped=='h'?0xA0:escaped]);
                        i+=escaped=='|'&&i+2<text.Length&&text[i+2]=='\\'?3:2;
                    }
                }
            }
        }
        return new(usage.Values.Select(u=>new FontUsage(u.Request,u.Characters.Order().ToArray(),u.Styles.Order(StringComparer.Ordinal).ToArray(),u.Lines.Order().ToArray())).OrderBy(u=>u.Request.Family,StringComparer.OrdinalIgnoreCase).ThenBy(u=>u.Request.Weight).ThenBy(u=>u.Request.Italic).ToArray(),warnings.Distinct().ToArray());
    }
}
