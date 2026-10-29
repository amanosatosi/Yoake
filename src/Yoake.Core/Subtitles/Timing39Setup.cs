namespace Yoake.Core.Subtitles;

public sealed record Timing39Target(int Row,long Start,long End,string Style,string Text,bool Selected);
public sealed record Timing39Setup(long Start,long End,int? MarkerRow,bool ExplicitSelection,string ActiveStyle,IReadOnlyList<Timing39Target> Targets);

/// <summary>Captures source values and media bounds without mutating the document.</summary>
public static class Timing39SessionSetup
{
    public static Timing39Setup Create(IReadOnlyList<AssEvent> events,IReadOnlyCollection<AssEvent> selected,AssEvent? active,long mediaEnd)
    {
        ArgumentNullException.ThrowIfNull(events);ArgumentNullException.ThrowIfNull(selected);
        if(mediaEnd<=0)throw new ArgumentOutOfRangeException(nameof(mediaEnd));
        var eventSet=events.ToHashSet();
        if(selected.Any(line=>!eventSet.Contains(line)) || active is not null && !eventSet.Contains(active))
            throw new ArgumentException("The selection belongs to a different subtitle document.");
        var selectedLyrics=selected.Where(line=>!line.IsComment).ToHashSet();
        var explicitSelection=selectedLyrics.Count>1;
        long start=0;int? marker=null;
        for(var i=0;i<events.Count;i++)
        {
            var line=events[i];
            if(!line.IsComment||line.StartMilliseconds is not {} time||time<0)continue;
            var text=AssTextOperations.PlainTextForSorting(line.Text).Trim(' ','\t','\r','\n','\f','\v');
            if(!text.Equals("39 mode start here",StringComparison.OrdinalIgnoreCase))continue;
            if(marker is null||time<start){marker=i;start=time;}
        }
        if(start>=mediaEnd)throw new ArgumentException("39 Mode start marker is outside the media.");
        var activeStyle=active is {IsComment:false}?active.Style:events.FirstOrDefault(line=>!line.IsComment&&(selectedLyrics.Count==0||selectedLyrics.Contains(line)))?.Style??"";
        var targets=new List<Timing39Target>();
        for(var i=0;i<events.Count;i++)
        {
            var line=events[i];
            if(line.IsComment||explicitSelection&&!selectedLyrics.Contains(line)||line.StartMilliseconds is not {} from||line.EndMilliseconds is not {} to||to<=from)continue;
            if(!explicitSelection&&to<=start)continue;
            targets.Add(new(i,from,to,line.Style,line.Text,selectedLyrics.Contains(line)));
        }
        var end=explicitSelection&&targets.Count>0?targets.Max(target=>target.End):mediaEnd;
        if(end<=start)end=mediaEnd;
        end=Math.Min(end,mediaEnd);
        return new(start,end,marker,explicitSelection,activeStyle,targets.ToArray());
    }
}
