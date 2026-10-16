using System.Globalization;
using System.Text;

namespace Yoake.Core.Subtitles;

internal sealed class SubtitleSearchProjection
{
    internal readonly record struct Unit(int Start,int End);
    internal readonly record struct Segment(int Start,int Length,int VisibleLength);
    private readonly Unit[] _units;
    private readonly bool[] _boundaries;
    private readonly string _source;
    public string Text {get;}
    public SubtitleSearchProjection(string source,bool visibleText)
    {
        _source=source;
        if(!visibleText){Text=source;_units=[];}
        else
        {
            var text=new StringBuilder();List<Unit> units=[];
            foreach(var token in AssSyntax.Tokenize(source))
            {
                if(token.Kind==AssSyntaxKind.Text)
                    for(var index=token.Start;index<token.Start+token.Length;index++){text.Append(source[index]);units.Add(new(index,index+1));}
                else if(token.Kind==AssSyntaxKind.Escape)
                    for(var index=token.Start;index<token.Start+token.Length;)
                    {
                        var character=source[index+1];var length=character=='|'&&index+2<source.Length&&source[index+2]=='\\'?3:2;
                        text.Append(character is 'N' or 'n'?'\n':character=='h'?' ':character);units.Add(new(index,index+length));index+=length;
                    }
            }
            Text=text.ToString();_units=units.ToArray();
        }
        Visible=visibleText;_boundaries=new bool[Text.Length+1];_boundaries[Text.Length]=true;
        foreach(var position in StringInfo.ParseCombiningCharacters(Text))_boundaries[position]=true;
    }
    private bool Visible {get;}
    public bool IsWholeGraphemeRange(int index,int length)=>_boundaries[index]&&_boundaries[index+length];
    public int SourceIndex(int index)=>!Visible?index:index<_units.Length?_units[index].Start:_units.Length==0?_source.Length:_units[^1].End;
    public int SourceLength(int index,int length)=>length==0?0:!Visible?length:_units[index+length-1].End-SourceIndex(index);
    public string Replace(string source,int index,int length,string replacement)
    {
        if(source!=_source)throw new InvalidOperationException("Search source changed.");
        return ReplaceMany([(index,length,replacement)]);
    }
    public string ReplaceMany(IReadOnlyList<(int Index,int Length,string Text)> patches,CancellationToken token=default)
    {
        var result=new StringBuilder(_source.Length);var previous=0;
        foreach(var patch in patches){token.ThrowIfCancellationRequested();AppendReplacement(result,ref previous,patch.Index,patch.Length,patch.Text);}
        return result.Append(_source,previous,_source.Length-previous).ToString();
    }
    private void AppendReplacement(StringBuilder result,ref int previous,int index,int length,string replacement)
    {
        var insertion=SourceIndex(index);
        if(Text.AsSpan(index,length).SequenceEqual(replacement.AsSpan()))
        {
            var end=insertion+SourceLength(index,length);result.Append(_source,previous,end-previous);previous=end;return;
        }
        ValidateUnicode(replacement);
        if(length==0||!Visible)
        {
            result.Append(_source,previous,insertion-previous);result.Append(replacement);previous=insertion+SourceLength(index,length);return;
        }
        List<Segment> segments=[];
        for(var cursor=index;cursor<index+length;cursor++)
        {
            var unit=_units[cursor];
            if(segments.Count>0&&segments[^1].Start+segments[^1].Length==unit.Start)
                segments[^1]=segments[^1] with{Length=unit.End-segments[^1].Start,VisibleLength=segments[^1].VisibleLength+1};
            else segments.Add(new(unit.Start,unit.End-unit.Start,1));
        }
        var used=0;var boundaries=segments.Count>1?StringInfo.ParseCombiningCharacters(replacement):[];
        for(var cursor=0;cursor<segments.Count;cursor++)
        {
            var segment=segments[cursor];result.Append(_source,previous,segment.Start-previous);
            var end=cursor==segments.Count-1?replacement.Length:Math.Min(replacement.Length,used+segment.VisibleLength);
            if(end<replacement.Length&&!boundaries.Contains(end))end=boundaries.LastOrDefault(boundary=>boundary<=end);
            result.Append(replacement,used,end-used);used=end;previous=segment.Start+segment.Length;
        }
    }
    private static void ValidateUnicode(string text)
    {
        for(var index=0;index<text.Length;index++)
            if(char.IsHighSurrogate(text[index]))
            {if(index+1>=text.Length||!char.IsLowSurrogate(text[++index]))throw new ArgumentException("Replacement would create an incomplete Unicode character.");}
            else if(char.IsLowSurrogate(text[index]))throw new ArgumentException("Replacement would create an incomplete Unicode character.");
    }
}
