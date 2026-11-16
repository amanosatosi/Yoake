using System.Text;

namespace Yoake.Core.Subtitles;

public enum MangetsuMetadataKind { None, Style, Actor }

// Matches libassmod/mangetsu ass.c's initial metadata block. These are normal
// ASS comments: payloads, unknown tags and future columns remain opaque.
public static class MangetsuMetadata
{
    public static MangetsuMetadataKind Kind(AssEvent line)
    {
        if(!line.IsComment)return MangetsuMetadataKind.None;
        if(line.Effect=="mangetsu-style")return MangetsuMetadataKind.Style;
        var effect=line.Effect.Trim();
        if((effect=="mangetsu-colorcoding"||effect=="{mangetsu-colorcoding}")&&line.Actor.Length>0)return MangetsuMetadataKind.Actor;
        return MangetsuMetadataKind.None;
    }
    public static bool IsInherited(AssEvent line)=>line.Text.Contains("{uketsugu}",StringComparison.Ordinal);
    public static AssEvent[] Initial(AssDocument document)=>document.Events.TakeWhile(e=>Kind(e)!=MangetsuMetadataKind.None).ToArray();
    internal static string RenameResets(string text,IReadOnlyDictionary<string,string> names)
    {
        var result=new StringBuilder(text);
        foreach(var tag in AssSyntax.Tags(text).Reverse())
        {
            if(tag.Name!="r"||tag.BlockEnd>=text.Length)continue;
            var value=text.AsSpan(tag.ValueStart,tag.End-tag.ValueStart);var trimmed=value.Trim();
            if(!names.TryGetValue(trimmed.ToString(),out var replacement))continue;
            var start=tag.ValueStart+value.Length-value.TrimStart().Length;
            result.Remove(start,trimmed.Length).Insert(start,replacement);
        }
        return result.ToString();
    }
    internal static string RenameWhitelist(string text,IReadOnlyDictionary<string,string> names)
    {
        var result=new StringBuilder();var copied=0;
        for(var start=text.IndexOf('{');start>=0;start=text.IndexOf('{',copied))
        {
            var end=text.IndexOf('}',start+1);if(end<0)break;
            var name=text[(start+1)..end];
            result.Append(text,copied,start-copied+1).Append(names.GetValueOrDefault(name,name)).Append('}');copied=end+1;
        }
        return result.Append(text,copied,text.Length-copied).ToString();
    }
}

public sealed partial class SubtitleEditor
{
    private AssEvent[] PrepareStyleMetadata(AssDocument source,IReadOnlyDictionary<string,string> names)
    {
        string? Resolve(string name)
        {
            var plain=name.TrimStart('*');
            if(names.TryGetValue(plain,out var mapped))return mapped;
            return plain.Equals("Default",StringComparison.OrdinalIgnoreCase)&&names.TryGetValue("Default",out mapped)?mapped:null;
        }
        var metadata=MangetsuMetadata.Initial(source);
        var records=new List<AssEvent>();
        foreach(var line in metadata.Concat(source.Events.Where(MangetsuMetadata.IsInherited)).Distinct())
        {
            var kind=MangetsuMetadata.Kind(line);var target=Resolve(line.Style);
            if(kind==MangetsuMetadataKind.Style&&target is null)continue;
            var copy=line.Clone();if(target is not null)copy.Style=target;
            copy.Text=MangetsuMetadata.RenameResets(copy.Text,names);
            if(kind==MangetsuMetadataKind.Actor&&copy.Actor.Trim('{','}')=="mangetsu-colorcode-applied-styles")copy.Text=MangetsuMetadata.RenameWhitelist(copy.Text,names);
            records.Add(copy);
        }
        return records.OrderBy(e=>MangetsuMetadata.Kind(e) switch { MangetsuMetadataKind.Style=>0,MangetsuMetadataKind.Actor=>1,_=>2 }).ToArray();
    }
    private void InsertStyleMetadata(AssEvent[] imported)
    {
        if(imported.Length==0)return;
        var existing=SourceEvents().TakeWhile(e=>MangetsuMetadata.Kind(e)!=MangetsuMetadataKind.None).ToArray();
        var anchor=SourceEvents().Skip(existing.Length).FirstOrDefault();
        foreach(var line in imported.Where(e=>MangetsuMetadata.Kind(e)!=MangetsuMetadataKind.None))Document.Insert(line,anchor);
        NormalizeMetadataOrder();
        anchor=SourceEvents().SkipWhile(e=>MangetsuMetadata.Kind(e)!=MangetsuMetadataKind.None||MangetsuMetadata.IsInherited(e)).FirstOrDefault();
        foreach(var line in imported.Where(e=>MangetsuMetadata.Kind(e)==MangetsuMetadataKind.None))Document.Insert(line,anchor);
    }
    private AssEvent[] SourceEvents()=>Document.Source.Select(line=>line.Record).OfType<AssEvent>().ToArray();
    private void NormalizeMetadataOrder()
    {
        var initial=SourceEvents().TakeWhile(e=>MangetsuMetadata.Kind(e)!=MangetsuMetadataKind.None).ToHashSet();
        var positions=Document.Source.Select((line,index)=>(line,index)).Where(p=>p.line.Record is AssEvent e&&initial.Contains(e)).ToArray();
        var sorted=positions.Select(p=>p.line).OrderBy(line=>MangetsuMetadata.Kind((AssEvent)line.Record!)==MangetsuMetadataKind.Style?0:1).ToArray();
        for(var i=0;i<positions.Length;i++)Document.Source[positions[i].index]=sorted[i];
        Document.Restore(Document.Source.ToArray());
    }

    public void SetMangetsuMetadata(MangetsuMetadataKind kind,string target,string text)
    {
        if(kind is not (MangetsuMetadataKind.Style or MangetsuMetadataKind.Actor))throw new ArgumentException("Choose style or actor metadata.");
        if(string.IsNullOrWhiteSpace(target))throw new ArgumentException("Choose a style or enter an actor name.");
        if(kind==MangetsuMetadataKind.Style&&!Document.Styles.Any(s=>s.Name==target))throw new ArgumentException("Choose an existing style.");
        var existing=MangetsuMetadata.Initial(Document).LastOrDefault(e=>MangetsuMetadata.Kind(e)==kind&&(kind==MangetsuMetadataKind.Style?e.Style:e.Actor)==target);
        if(existing is not null&&existing.Text==text)return;
        // Clone and validate before opening the structural transaction.
        var copy=existing?.Clone()??Document.NewEvent();copy.Prefix="Comment: ";
        copy.Style=kind==MangetsuMetadataKind.Style?target:Document.Styles.FirstOrDefault()?.Name??"Default";
        copy.Actor=kind==MangetsuMetadataKind.Actor?target:"";copy.Effect=kind==MangetsuMetadataKind.Style?"mangetsu-style":"mangetsu-colorcoding";copy.Text=text;
        if(existing is not null)
        {
            // Existing record identity is retained for active grid selection.
            using var transaction=Undo.BeginTransaction("Edit Mangetsu metadata");
            SetField(existing,"Text",copy.Text,"Edit Mangetsu metadata");transaction.Commit();return;
        }
        Structure("Add Mangetsu metadata",()=> { Document.Insert(copy,Document.Events.FirstOrDefault());NormalizeMetadataOrder(); });
    }
    public void RemoveMangetsuMetadata(MangetsuMetadataKind kind,string target)
    {
        var entries=MangetsuMetadata.Initial(Document).Where(e=>MangetsuMetadata.Kind(e)==kind&&(kind==MangetsuMetadataKind.Style?e.Style:e.Actor)==target).ToArray();
        if(entries.Length==0)return;
        Structure("Remove Mangetsu metadata",()=> { foreach(var line in entries)Document.Remove(line); });
    }
}
