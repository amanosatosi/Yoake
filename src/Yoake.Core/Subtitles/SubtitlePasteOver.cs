using Yoake.Core.Undo;

namespace Yoake.Core.Subtitles;

[Flags]
public enum PasteOverFields
{
    None=0, Comment=1, Layer=2, Start=4, End=8, Style=16, Actor=32,
    MarginL=64, MarginR=128, MarginV=256, Effect=512, Text=1024,
    Times=Start|End, All=2047,
}

public static class SubtitlePasteClipboard
{
    // Structured input uses the same lossless Format-aware parser as files.
    // Malformed ASS must never silently become dialogue text and overwrite rows.
    public static IReadOnlyList<AssEvent> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if(text.Length>8*1024*1024)throw new ArgumentException("Clipboard subtitles exceed the 8 MiB limit.");
        var rows=text.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries);
        if(rows.Length>100000)throw new ArgumentException("Clipboard contains too many lines.");
        if(rows.Length==0)return [];
        var structured=rows.Any(r=>r.TrimStart().StartsWith("Dialogue:",StringComparison.OrdinalIgnoreCase)||r.TrimStart().StartsWith("Comment:",StringComparison.OrdinalIgnoreCase)||r.TrimStart().StartsWith("Format:",StringComparison.OrdinalIgnoreCase)||r.TrimStart().StartsWith('['));
        if(!structured)
        {
            var defaults=AssDocument.CreateEmpty();
            return rows.Select(r=>{var line=defaults.NewEvent();line.Start=line.End="0:00:00.00";line.Text=r.Trim();return line;}).ToArray();
        }
        var source=rows.Any(r=>r.TrimStart().StartsWith('['))?text:"[Events]\nFormat: "+string.Join(',',AssDocument.EventFormat)+"\n"+text;
        var document=AssDocument.Parse(source);
        if(document.Events.Count==0)throw new ArgumentException("Clipboard contains no subtitle dialogue records.");
        foreach(var line in document.Events)
        {
            if(line.Fields.Length!=line.FieldNames.Count||line.FieldNames.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=line.FieldNames.Count||line.Index("Text")<0)
                throw new ArgumentException("Malformed clipboard dialogue or Format record.");
            // Validate supplied timing/metadata without requiring optional columns.
            foreach(var name in new[]{"Start","End"})
                if(line.Index(name)>=0&&!AssTime.TryParse(line.Get(name),out _))throw new ArgumentException("Invalid clipboard subtitle time.");
            foreach(var name in new[]{"Layer","MarginL","MarginR","MarginV"})
                if(line.Index(name)>=0&&(!int.TryParse(line.Get(name),out var n)||name!="Layer"&&n<0))throw new ArgumentException("Invalid clipboard subtitle "+name+".");
        }
        return document.Events.Select(l=>l.Clone()).ToArray();
    }
}

public sealed partial class SubtitleEditor
{
    public int PasteOver(IReadOnlyList<AssEvent> clipboard,IEnumerable<AssEvent> selection,AssEvent? active,
        PasteOverFields fields,IReadOnlyDictionary<string,string>? activeDraft=null)
    {
        if((fields&~PasteOverFields.All)!=0)throw new ArgumentException("Invalid paste fields.");
        var targets=Ordered(selection);
        if(targets.Length==0||clipboard.Count==0||fields==PasteOverFields.None)return 0;
        if(active is not null)Require(active);
        if(targets.Length==1)
        {
            if(active is null)return 0;
            targets=Document.Events.Skip(Document.Events.IndexOf(active)).ToArray();
        }
        var changes=new List<(AssEvent Line,Dictionary<string,string> Values,bool? Comment)>();
        for(var i=0;i<Math.Min(targets.Length,clipboard.Count);i++)
        {
            var line=targets[i];var incoming=clipboard[i];
            var values=ReferenceEquals(line,active)&&activeDraft is not null?new Dictionary<string,string>(activeDraft,StringComparer.OrdinalIgnoreCase):new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(var (flag,name) in new[]{(PasteOverFields.Layer,"Layer"),(PasteOverFields.Start,"Start"),(PasteOverFields.End,"End"),(PasteOverFields.Style,"Style"),(PasteOverFields.Actor,"Name"),(PasteOverFields.MarginL,"MarginL"),(PasteOverFields.MarginR,"MarginR"),(PasteOverFields.MarginV,"MarginV"),(PasteOverFields.Effect,"Effect"),(PasteOverFields.Text,"Text")})
            {
                var targetName=name=="Name"&&line.Index("Name")<0?"Actor":name;
                var sourceName=name=="Name"&&incoming.Index("Name")<0?"Actor":name;
                if(fields.HasFlag(flag)&&line.Index(targetName)>=0&&incoming.Index(sourceName)>=0)values[targetName]=incoming.Get(sourceName);
            }
            ValidateEventValues(line,values); // Validate every result before the first mutation.
            changes.Add((line,values,fields.HasFlag(PasteOverFields.Comment)?incoming.IsComment:null));
        }
        using var transaction=Undo.BeginTransaction("Paste lines over");
        using var update=Document.BeginUpdate();
        foreach(var (line,values,comment) in changes)
        {
            foreach(var pair in values)SetField(line,pair.Key,pair.Value,"Paste lines over");
            if(comment is {} desired&&line.IsComment!=desired)
            {
                var prefix=line.Prefix;
                Undo.Execute(new DelegateUndoOperation("Paste comment flag",()=>line.RestorePrefix(prefix),()=>line.SetKind(desired)));
            }
        }
        transaction.Commit();return changes.Count;
    }
}
