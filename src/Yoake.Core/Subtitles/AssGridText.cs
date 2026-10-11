using System.Text;

namespace Yoake.Core.Subtitles;

public enum GridTagDisplayMode { Full, Simplified, Hidden }

public static class AssGridText
{
    // A projection only. The exact event text, escapes, drawing and Mangetsu
    // syntax remain untouched. Incomplete blocks remain visible for repair.
    public static string Project(string source,GridTagDisplayMode mode)
    {
        ArgumentNullException.ThrowIfNull(source);
        if(!Enum.IsDefined(mode))throw new ArgumentException("Unknown tag display mode.",nameof(mode));
        if(mode==GridTagDisplayMode.Full)return source;
        StringBuilder? result=null;var previous=0;var opening=-1;
        foreach(var token in AssSyntax.Tokenize(source))
        {
            if(token.Kind!=AssSyntaxKind.Brace)continue;
            // The syntax cache coalesces adjacent braces into one token.
            for(var index=token.Start;index<token.Start+token.Length;index++)
            {
                if(source[index]=='{'){opening=index;continue;}
                if(opening<0)continue;
                result??=new StringBuilder(source.Length);
                result.Append(source,previous,opening-previous);
                if(mode==GridTagDisplayMode.Simplified)result.Append('☀');
                previous=index+1;opening=-1;
            }
        }
        return result is null?source:result.Append(source,previous,source.Length-previous).ToString();
    }
}
