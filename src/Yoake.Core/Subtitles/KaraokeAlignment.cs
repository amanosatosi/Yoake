using System.Globalization;

namespace Yoake.Core.Subtitles;

public sealed record KaraokeMatchGroup(int SourceCount,int DestinationCount,string SourceText,string DestinationText,long Duration,int DestinationStart);
public sealed record KaraokeMatchSyllable(string Text,long Duration);

/// <summary>Manual many-to-many syllable alignment; no document mutations until accepted.</summary>
public sealed class KaraokeAlignment
{
    private readonly KaraokeSource _destination;
    private readonly KaraokeMatchSyllable[] _source;
    private readonly int[] _boundaries;
    private readonly List<KaraokeMatchGroup> _groups=[];
    private int _sourceCursor,_destinationCursor;
    public IReadOnlyList<KaraokeMatchGroup> Groups=>_groups;
    public KaraokeMatchSyllable[] RemainingSource=>_source[_sourceCursor..];
    public string[] RemainingDestination=>Enumerable.Range(_destinationCursor,DestinationRemaining).Select(i=>_destination.Text[_boundaries[i].._boundaries[i+1]]).ToArray();
    public int SourceRemaining=>_source.Length-_sourceCursor;
    public int DestinationRemaining=>_boundaries.Length-1-_destinationCursor;
    public bool Complete=>SourceRemaining==0&&DestinationRemaining==0;
    public KaraokeAlignment(string source,string destination,long? sourceDuration=null)
    {
        // Ruby needs separate base/reading matching. Never split its syntax or
        // pretend its reading is the user's kanji destination.
        if(AssSyntax.Tokenize(destination).Any(t=>t.Kind is AssSyntaxKind.Furigana or AssSyntaxKind.Drawing))
            throw new NotSupportedException("Ruby and drawing destinations require separate matching and are preserved unchanged.");
        var input=KaraokeSource.Parse(source);_destination=KaraokeSource.Parse(destination);
        if(!input.Existing&&sourceDuration is null)throw new ArgumentException("The source line has no karaoke syllables.");
        _source=!input.Existing?[new(input.Text,0)]:Enumerable.Range(0,input.Tags.Length).Select(i=>new KaraokeMatchSyllable(input.Text[input.Cuts[i]..(i+1<input.Cuts.Length?input.Cuts[i+1]:input.Text.Length)],input.Durations[i])).ToArray();
        if(sourceDuration is {} remaining)
        {
            if(remaining<0)throw new ArgumentException("Source end time must not precede start time.");
            // Aegisub 3.2.2 AssKaraoke normalizes to the source line length:
            // clamp overflow and extend the last syllable to the line end.
            for(var i=0;i<_source.Length;i++)
            {
                var duration=i==_source.Length-1?remaining:Math.Min(_source[i].Duration,remaining);
                _source[i]=_source[i] with { Duration=duration };remaining-=duration;
            }
        }
        var boundaries=StringInfo.ParseCombiningCharacters(_destination.Text).ToList();
        // ASS escapes are indivisible source tokens, alongside Unicode clusters.
        for(var i=0;i+1<_destination.Text.Length;i++)
            if(_destination.Text[i]=='\\'&&_destination.Text[i+1] is 'N' or 'n' or 'h' or '{' or '}' or '\\') { boundaries.Remove(i+1);i++; }
        boundaries.Add(_destination.Text.Length);_boundaries=boundaries.ToArray();
    }
    public KaraokeMatchSuggestion Suggest()=>KaraokeJapaneseMatcher.Suggest(RemainingSource.Select(s=>s.Text).ToArray(),RemainingDestination);
    public void Link(int sourceCount,int destinationCount)
    {
        if(sourceCount<0||sourceCount>SourceRemaining||destinationCount<0||destinationCount>DestinationRemaining||sourceCount+destinationCount==0)throw new ArgumentException("Choose a nonempty group within the remaining source and destination.");
        var source=_source.AsSpan(_sourceCursor,sourceCount);long duration=0;foreach(var syllable in source)duration=checked(duration+syllable.Duration);
        var begin=_boundaries[_destinationCursor];var end=_boundaries[_destinationCursor+destinationCount];
        _groups.Add(new(sourceCount,destinationCount,string.Concat(source.ToArray().Select(s=>s.Text)),_destination.Text[begin..end],duration,begin));
        _sourceCursor+=sourceCount;_destinationCursor+=destinationCount;
    }
    public void Unlink()
    {
        if(_groups.Count==0)return;var last=_groups[^1];_groups.RemoveAt(_groups.Count-1);_sourceCursor-=last.SourceCount;_destinationCursor-=last.DestinationCount;
    }
    public string Render()
    {
        if(!Complete)throw new InvalidOperationException("Link all source syllables and destination characters before accepting the line.");
        return _destination.Render(_groups.Select(g=>g.DestinationStart).ToArray(),_groups.Select(_=>"k").ToArray(),_groups.Select(g=>g.Duration/10*10).ToArray());
    }
}
