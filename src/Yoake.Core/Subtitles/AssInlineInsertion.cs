using System.Text;

namespace Yoake.Core.Subtitles;

public sealed record JapaneseBracketPair(string Id,string Left,string Right);
public sealed record FuriganaReading(string Reading,string OppositeReading="");

public static class AssInlineInsertion
{
    // Pair inventory/behavior: Toshi-ban subs_edit_box.cpp, kBracketPairs.
    public static IReadOnlyList<JapaneseBracketPair> Brackets { get; } = Array.AsReadOnly(new[]
    {
        new JapaneseBracketPair("corner","「","」"),new("double-corner","『","』"),
        new("angle","〈","〉"),new("double-angle","《","》"),new("lenticular","【","】"),
        new("tortoise","〔","〕"),new("white-lenticular","〖","〗"),new("white-tortoise","〘","〙"),
        new("white-square","〚","〛"),new("round","（","）"),new("square","［","］"),
        new("curly","｛","｝"),new("fullwidth-angle","＜","＞"),new("much-less","≪","≫")
    });

    private static (int Start,int End) Range(string source,int start,int end)
    {
        var caret = start == end;
        var left = AssFormatting.Boundary(source,Math.Min(start,end),caret);
        var right = caret ? left : AssFormatting.Boundary(source,Math.Max(start,end),true);
        foreach (var block in AssTextOperations.Blocks(source))
        {
            if (!block.Closed && (left > block.Start || right > block.Start)) throw new ArgumentException("Close the ASS override/comment block before inserting text.");
            if (left > block.Start && left < block.End)
            {
                if (!block.Closed) throw new ArgumentException("Close the ASS override/comment block before inserting text.");
                left = caret ? block.End : block.Start;
            }
            if (!caret && right > block.Start && right < block.End) right = block.End;
        }
        foreach (var (offset,length) in Escapes(source))
        {
            if (left > offset && left < offset+length) left = caret ? offset+length : offset;
            if (!caret && right > offset && right < offset+length) right = offset+length;
        }
        return (left,caret ? left : right);
    }

    public static AssTextEdit Bracket(string source,int start,int end,string id)
    {
        var pair = Brackets.FirstOrDefault(pair=>pair.Id==id) ?? throw new ArgumentException("Choose a Japanese bracket pair.");
        var (left,right) = Range(source,start,end);
        var text = source[..left]+pair.Left+source[left..right]+pair.Right+source[right..];
        return new(text,left+pair.Left.Length,right+pair.Left.Length);
    }

    public static string FuriganaBase(string source,int start,int end)
    {
        var (left,right) = Range(source,start,end);
        if (left==right || string.IsNullOrWhiteSpace(source[left..right])) throw new ArgumentException("Select the base text for furigana first.");
        var tokens = AssSyntax.Tokenize(source).Where(t=>t.Start<right&&t.Start+t.Length>left).ToArray();
        if (tokens.Any(t=>t.Kind is not (AssSyntaxKind.Text or AssSyntaxKind.Escape)) ||
            source[left..right].IndexOfAny(['\r','\n'])>=0 || Escapes(source).Any(t=>t.Start>=left&&t.Start<right&&source[t.Start+1] is 'N' or 'n'))
            throw new ArgumentException("Select plain base text on one line, outside overrides, drawings and existing furigana.");
        return source[left..right];
    }

    public static AssTextEdit Furigana(string source,int start,int end,FuriganaReading reading)
    {
        var originalBase = FuriganaBase(source,start,end);
        if (string.IsNullOrWhiteSpace(reading.Reading)&&string.IsNullOrWhiteSpace(reading.OppositeReading))
            throw new ArgumentException("Enter a reading or an opposite-side reading.");
        if ((reading.Reading+reading.OppositeReading).IndexOfAny(['\r','\n'])>=0)
            throw new ArgumentException("Furigana cannot contain a line break.");
        var (left,right) = Range(source,start,end);
        var baseText = new StringBuilder();
        foreach (var token in AssSyntax.Tokenize(originalBase))
            baseText.Append(token.Kind==AssSyntaxKind.Escape ? originalBase.Substring(token.Start,token.Length) :
                Escape(originalBase.Substring(token.Start,token.Length),false));
        var markup = "<"+baseText+"|"+Escape(reading.Reading,true)+
            (reading.OppositeReading.Length==0 ? "" : "|"+Escape(reading.OppositeReading,true))+">";
        var text = source[..left]+markup+source[right..];
        if (!AssSyntax.Tokenize(text).Any(t=>t.Kind==AssSyntaxKind.Furigana&&t.Start<=left&&t.Start+t.Length>left))
            throw new ArgumentException("Furigana is disabled here, vertical text is active, or the selection is inside existing ruby.");
        return new(text,left+1,left+1+baseText.Length);
    }

    private static IEnumerable<(int Start,int Length)> Escapes(string source)
    {
        foreach (var token in AssSyntax.Tokenize(source).Where(t=>t.Kind==AssSyntaxKind.Escape))
            for (var i=token.Start;i<token.Start+token.Length;)
            {
                var length=source[i+1]=='|'&&i+2<source.Length&&source[i+2]=='\\'?3:2;
                yield return (i,length);i+=length;
            }
    }

    private static string Escape(string text,bool reading)
    {
        if (reading) text = text.Replace("\\","\\\\",StringComparison.Ordinal).Replace("{",@"\{",StringComparison.Ordinal).Replace("}",@"\}",StringComparison.Ordinal);
        return text.Replace("<",@"\<",StringComparison.Ordinal).Replace(">",@"\>",StringComparison.Ordinal).Replace("|",@"\|\",StringComparison.Ordinal);
    }
}
