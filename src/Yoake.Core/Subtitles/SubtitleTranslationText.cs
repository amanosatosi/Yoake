namespace Yoake.Core.Subtitles;

public readonly record struct SubtitleTextSpan(int Start, int Length);

// Source offsets, never a stripped/re-serialized ASS line. Drawing payload,
// override/comment blocks and ruby delimiters/readings remain outside edits.
public static class SubtitleTranslationText
{
    public static SubtitleTextSpan[] Blocks(string source)
    {
        var spans=new List<SubtitleTextSpan>();
        var excluded=new bool[source.Length];var drawing=false;var previous=0;
        foreach(var block in AssTextOperations.Blocks(source))
        {
            if(drawing)Array.Fill(excluded,true,previous,block.Start-previous);
            if(block.Closed)
                foreach(var tag in AssSyntax.Tags(source[block.Start..block.End]))
                {
                    if(tag.Name=="r")drawing=false;
                    else if(tag.Name=="p"&&int.TryParse(source.AsSpan(block.Start+tag.ValueStart,tag.End-tag.ValueStart),out var scale))drawing=scale>0;
                }
            previous=block.End;
        }
        if(drawing)Array.Fill(excluded,true,previous,source.Length-previous);
        foreach(var token in AssSyntax.Tokenize(source))
        {
            if(token.Kind is not (AssSyntaxKind.Text or AssSyntaxKind.Escape)||excluded[token.Start])continue;
            if(spans.Count>0&&spans[^1].Start+spans[^1].Length==token.Start)
                spans[^1]=spans[^1] with { Length=spans[^1].Length+token.Length };
            else spans.Add(new(token.Start,token.Length));
        }
        return spans.Where(s=>!string.IsNullOrWhiteSpace(source.Substring(s.Start,s.Length))).ToArray();
    }
    public static string Replace(string source,SubtitleTextSpan span,string text)
    {
        if(!Blocks(source).Contains(span))throw new ArgumentException("The translation block is no longer current.");
        text=text.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n').Replace("\n",@"\N",StringComparison.Ordinal);
        return source[..span.Start]+text+source[(span.Start+span.Length)..];
    }
}
