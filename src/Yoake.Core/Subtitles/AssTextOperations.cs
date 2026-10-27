using System.Globalization;
using System.Text;

namespace Yoake.Core.Subtitles;

internal readonly record struct AssOverrideBlock(int Start,int End,bool Closed);

public static class AssTextOperations
{
    // Toshi-ban's stripped sort compares plain blocks, retaining escape and
    // furigana spelling while excluding closed override/comment and drawing blocks.
    public static string PlainTextForSorting(string source)
    {
        var result=new StringBuilder();var previous=0;var drawing=false;
        foreach(var block in Blocks(source))
        {
            if(!drawing)result.Append(source,previous,block.Start-previous);
            if(!block.Closed){if(!drawing)result.Append(source,block.Start,block.End-block.Start);return result.ToString();}
            var text=source[block.Start..block.End];
            foreach(var tag in AssSyntax.Tags(text))
                if(tag.Name=="p"&&int.TryParse(text.AsSpan(tag.ValueStart,tag.End-tag.ValueStart),NumberStyles.Integer,CultureInfo.InvariantCulture,out var scale))drawing=scale>0;
            previous=block.End;
        }
        if(!drawing)result.Append(source,previous,source.Length-previous);
        return result.ToString();
    }
    internal static IEnumerable<AssOverrideBlock> Blocks(string source)
    {
        var opening=-1;
        foreach(var token in AssSyntax.Tokenize(source))
        {
            if(token.Kind!=AssSyntaxKind.Brace)continue;
            for(var index=token.Start;index<token.Start+token.Length;index++)
                if(source[index]=='{')opening=index;
                else if(opening>=0){yield return new(opening,index+1,true);opening=-1;}
        }
        if(opening>=0)yield return new(opening,source.Length,false);
    }
    public static string ClearVisibleText(string source)
    {
        var result=new StringBuilder();var previous=0;var drawing=false;
        foreach(var block in Blocks(source))
        {
            if(drawing)result.Append(source,previous,block.Start-previous);
            result.Append(source,block.Start,block.End-block.Start);
            if(block.Closed)
                foreach(var tag in AssSyntax.Tags(source[block.Start..block.End]))
                {
                    if(tag.Name=="r")drawing=false;
                    else if(tag.Name=="p"&&int.TryParse(source.AsSpan(block.Start+tag.ValueStart,tag.End-tag.ValueStart),NumberStyles.Integer,CultureInfo.InvariantCulture,out var scale))drawing=scale>0;
                }
            previous=block.End;
        }
        if(drawing)result.Append(source,previous,source.Length-previous);
        return result.ToString();
    }
    public static string OverridesBefore(string source,int cursor)
    {
        var result=new StringBuilder();
        foreach(var block in Blocks(source))
        {
            if(!block.Closed||block.End>cursor)break;
            var text=source[block.Start..block.End];var previous=0;
            // Earlier syllable timing must not replay on the new text segment.
            // All other known, unknown and Mangetsu tags retain exact spelling.
            foreach(var tag in AssSyntax.Tags(text))
                if(tag.Name is "k" or "K" or "kf" or "ko" or "kO" or "kt")
                {result.Append(text,previous,tag.Start-previous);previous=tag.End;}
            result.Append(text,previous,text.Length-previous);
        }
        return result.ToString();
    }
    public static int VisibleGraphemeCount(string source)
    {
        var text=new StringBuilder();
        foreach(var token in AssSyntax.Tokenize(source))
        {
            if(token.Kind==AssSyntaxKind.Text)text.Append(source,token.Start,token.Length);
            else if(token.Kind==AssSyntaxKind.Escape)
                for(var index=token.Start;index<token.Start+token.Length;)
                {
                    var character=source[index+1];text.Append(character is 'N' or 'n'?'\n':character=='h'?' ':character);
                    index+=character=='|'&&index+2<source.Length&&source[index+2]=='\\'?3:2;
                }
        }
        return StringInfo.ParseCombiningCharacters(text.ToString()).Length;
    }
}
