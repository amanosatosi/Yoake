using System.Globalization;

namespace Yoake.Core.Subtitles;

public sealed partial class SubtitleEditor
{
    public IReadOnlyList<AssEvent> Recombine(IEnumerable<AssEvent> selection)
    {
        var selected=Ordered(selection);
        var originals=selected.Where(e=>MangetsuMetadata.Kind(e)==MangetsuMetadataKind.None&&!MangetsuMetadata.IsInherited(e)).OrderBy(e=>e.StartMilliseconds).ToArray();
        if(originals.Length<2)return selected;
        var copies=originals.Select(e=>e.Clone()).ToArray();var removed=new bool[copies.Length];
        foreach(var line in copies)
        {
            if(line.StartMilliseconds is not {} start||line.EndMilliseconds is not {} end||end<start)throw new ArgumentException("Correct invalid line timing before recombining.");
            line.Text=TrimRecombine(line.Text);
        }
        static void Expand(AssEvent source,AssEvent destination)
        {
            if(source.StartMilliseconds<destination.StartMilliseconds)destination.Start=source.Start;
            if(source.EndMilliseconds>destination.EndMilliseconds)destination.End=source.End;
        }
        static bool Prefix(AssEvent text,AssEvent part)
        {
            if(!text.Text.StartsWith(part.Text,StringComparison.Ordinal))return false;
            RecombineBoundary(text.Text,part.Text.Length);text.Text=TrimRecombine(text.Text[part.Text.Length..]);Expand(text,part);return true;
        }
        static bool Suffix(AssEvent text,AssEvent part)
        {
            if(!text.Text.EndsWith(part.Text,StringComparison.Ordinal))return false;
            var cut=text.Text.Length-part.Text.Length;RecombineBoundary(text.Text,cut);text.Text=TrimRecombine(text.Text[..cut]);Expand(text,part);return true;
        }
        // The four prefix/suffix cases follow Aegisub's raw-text comparison.
        // All work is on clones so a later unsafe boundary remains atomic.
        for(var i=0;i<copies.Length-1;i++)
        {
            var first=copies[i];var next=i+1;
            if(first.Text==copies[next].Text) { Expand(first,copies[next]);removed[i]=true;continue; }
            if(first.Text.Length==0) { removed[i]=true;continue; }
            if(next==copies.Length-1&&copies[next].Text.Length==0) { removed[next]=true;continue; }
            while(next<copies.Length&&Prefix(copies[next],first))next++;
            while(next<copies.Length&&Suffix(copies[next],first))next++;
            while(next<copies.Length&&Suffix(first,copies[next]))next++;
            while(next<copies.Length&&Prefix(first,copies[next]))next++;
        }
        // Remove fragments emptied by the last prefix/suffix pass too. The
        // reference only revisits later rows and can leave an empty first row.
        for(var i=0;i<copies.Length;i++)if(copies[i].Text.Length==0)removed[i]=true;
        if(removed.All(value=>value))removed[^1]=false;
        var deleted=originals.Where((_,i)=>removed[i]).ToHashSet();
        using var update=Document.BeginUpdate();
        using var transaction=Undo.BeginTransaction("Recombine subtitles");
        for(var i=0;i<copies.Length;i++)
        {
            if(removed[i])continue;
            SetField(originals[i],"Text",copies[i].Text,"Recombine subtitles");
            SetField(originals[i],"Start",copies[i].Start,"Recombine subtitles");
            SetField(originals[i],"End",copies[i].End,"Recombine subtitles");
        }
        if(deleted.Count>0)Structure("Recombine subtitles",()=> { foreach(var line in deleted)Document.Remove(line); });
        transaction.Commit();return selected.Where(e=>!deleted.Contains(e)).ToArray();
    }
    private static string TrimRecombine(string text)
    {
        var start=0;var end=text.Length;
        while(start<end)
        {
            if(text[start] is ' ' or '\t') { start++;continue; }
            if(start+1<end&&text[start]=='\\'&&text[start+1] is 'N' or 'n' or 'h') { start+=2;continue; }
            break;
        }
        while(end>start)
        {
            if(text[end-1] is ' ' or '\t') { end--;continue; }
            if(end-2>=start&&text[end-2]=='\\'&&text[end-1] is 'N' or 'n' or 'h') { end-=2;continue; }
            break;
        }
        RecombineBoundary(text,start);RecombineBoundary(text,end);return text[start..end];
    }
    private static void RecombineBoundary(string text,int cut)
    {
        if(cut==0||cut==text.Length)return;
        if(!StringInfo.ParseCombiningCharacters(text).Contains(cut)||AssTextOperations.Blocks(text).Any(b=>b.Start<cut&&cut<b.End))
            throw new ArgumentException("Recombining would cut a grapheme or override block; the source was preserved.");
        var group=-1;
        foreach(var token in AssSyntax.Tokenize(text).Where(t=>t.Kind==AssSyntaxKind.Furigana))
            for(var i=token.Start;i<token.Start+token.Length;i++)
                if(text[i]=='<')group=i;
                else if(text[i]=='>'&&group>=0) { if(cut>group&&cut<=i)throw new ArgumentException("Recombining would cut a ruby group; the source was preserved.");group=-1; }
    }
}
