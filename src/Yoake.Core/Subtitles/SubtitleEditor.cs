using System.Globalization;
using Yoake.Core.Undo;

namespace Yoake.Core.Subtitles;

// One editor and undo history per document. UI drafts and gestures call this boundary.
public sealed class SubtitleEditor(AssDocument document)
{
    public AssDocument Document { get; } = document;
    public UndoManager Undo { get; } = new();
    public bool IsDirty => Undo.IsDirty;
    public void MarkSaved() => Undo.MarkSaved();
    private void Require(AssRecord record)
    {
        if (!Document.Source.Any(l => ReferenceEquals(l.Record, record))) throw new InvalidOperationException("Record belongs to another document.");
    }
    public void SetField(AssRecord record, string field, string value, string name)
    {
        Require(record);
        var old = (string[])record.Fields.Clone();
        var probe = record is AssEvent e ? (AssRecord)e.Clone() : ((AssStyle)record).Clone();
        probe.Set(field, value); // Validate before executing, including missing columns.
        var next = (string[])probe.Fields.Clone();
        if (old.SequenceEqual(next)) return;
        Undo.Execute(new FieldEditOperation(name,record,old,next));
    }
    private sealed class FieldEditOperation(string name,AssRecord record,string[] before,string[] after) : IMergeableUndoOperation
    {
        private string[] _after=after;
        public string Name=>name;
        public void Undo()=>record.RestoreFields(before);
        public void Redo()=>record.RestoreFields(_after);
        public bool TryMerge(IUndoOperation next)
        {
            if(next is not FieldEditOperation edit||!ReferenceEquals(record,edit.Target))return false;
            _after=edit._after;return true;
        }
        private AssRecord Target=>record;
    }
    public void EditEvent(AssEvent line, IReadOnlyDictionary<string, string> values)
    {
        Require(line);
        var probe = line.Clone();
        foreach (var pair in values) probe.Set(pair.Key, pair.Value);
        if (!AssTime.TryParse(probe.Start, out var start) || !AssTime.TryParse(probe.End, out var end) || end < start) throw new ArgumentException("Use h:mm:ss.cc times, with end at or after start.");
        foreach (var field in new[] { "Layer", "MarginL", "MarginR", "MarginV" })
            if (probe.Index(field) >= 0 && (!int.TryParse(probe.Get(field), out var number) || field != "Layer" && number < 0)) throw new ArgumentException($"{field} must be an integer (margins must be non-negative).");
        var changed = values.Where(p => line.Get(p.Key) != probe.Get(p.Key)).ToArray();
        if (changed.Length == 0) return;
        var name = changed.Length == 1 ? changed[0].Key switch { "Text" => "Edit subtitle text", "Style" => "Change style", "Name" or "Actor" => "Change actor", "Start" or "End" => "Change line timing", "MarginL" or "MarginR" or "MarginV" => "Change margins", _ => "Edit event metadata" } : "Edit subtitle";
        using var transaction = Undo.BeginTransaction(name);
        foreach (var pair in changed) SetField(line, pair.Key, pair.Value, name);
        transaction.Commit();
    }
    public void SetTiming(AssEvent line, long start, long end)
    {
        if (start < 0 || end < start) throw new ArgumentException("Invalid timing range.");
        // Can be nested in a gesture transaction without opening another transaction.
        SetField(line, "Start", AssTime.Format(start), "Change line timing");
        SetField(line, "End", AssTime.Format(end), "Change line timing");
    }
    private void Structure(string name, Action action)
    {
        var before = Document.Source.ToArray(); AssDocument.SourceLine[]? after = null;
        Undo.Execute(new DelegateUndoOperation(name, () => Document.Restore(before), () =>
        {
            if (after is not null) { Document.Restore(after); return; }
            try { using(Document.BeginUpdate())action(); after = Document.Source.ToArray(); }
            catch { Document.Restore(before); throw; }
        }));
    }
    public AssEvent Insert(AssEvent? reference, bool after, long start = 0)
    {
        if (reference is not null) Require(reference);
        var line = reference?.Clone() ?? Document.NewEvent(); line.Text = "";
        line.Start = AssTime.Format(start); line.End = AssTime.Format(start + 2000);
        var index = reference is null ? Document.Events.Count : Document.Events.IndexOf(reference) + (after ? 1 : 0);
        var before = Document.Events.ElementAtOrDefault(index);
        Structure("Insert subtitle", () => Document.Insert(line, before)); return line;
    }
    private AssEvent[] Ordered(IEnumerable<AssEvent> selection)
    {
        var set = selection.ToHashSet(); foreach (var line in set) Require(line);
        return Document.Events.Where(set.Contains).ToArray();
    }
    public void Delete(IEnumerable<AssEvent> selection)
    {
        var lines = Ordered(selection); if (lines.Length == 0) return;
        Structure("Delete subtitles", () => { foreach (var line in lines) Document.Remove(line); });
    }
    public IReadOnlyList<AssEvent> Duplicate(IEnumerable<AssEvent> selection)
    {
        var lines = Ordered(selection); var copies = lines.Select(l => l.Clone()).ToArray(); if (lines.Length == 0) return copies;
        var before = Document.Events.ElementAtOrDefault(Document.Events.IndexOf(lines[^1]) + 1);
        Structure("Duplicate subtitles", () => { foreach (var line in copies) Document.Insert(line, before); }); return copies;
    }
    public void ToggleComment(IEnumerable<AssEvent> selection)
    {
        var lines = Ordered(selection); using var transaction = Undo.BeginTransaction("Toggle Dialogue / Comment");
        foreach (var line in lines) { var was = line.IsComment; var prefix=line.Prefix; Undo.Execute(new DelegateUndoOperation("Toggle comment", () => line.RestorePrefix(prefix), () => line.SetKind(!was))); }
        transaction.Commit();
    }
    public void Move(IEnumerable<AssEvent> selection, int direction)
    {
        var lines = Ordered(selection); if (lines.Length == 0) return; var set = lines.ToHashSet();
        var step=Math.Sign(direction);
        if(step==0||!lines.Any(line=>{var i=Document.Events.IndexOf(line)+step;return i>=0&&i<Document.Events.Count&&!set.Contains(Document.Events[i]);}))return;
        if (direction > 0) Array.Reverse(lines);
        Structure("Move subtitles", () =>
        {
            var order=Document.Events.ToList();
            foreach(var line in lines)
            {
                var from=order.IndexOf(line);var to=from+Math.Sign(direction);
                if(to<0||to>=order.Count||set.Contains(order[to]))continue;
                Document.Swap(line,order[to]);(order[from],order[to])=(order[to],order[from]);
            }
        });
    }
    public AssEvent Split(AssEvent line, int cursor)
    {
        Require(line); var text = line.Text; cursor = AssText.SafeSplitIndex(text, cursor);
        if (cursor <= 0 || cursor >= text.Length) throw new ArgumentException("Place the cursor between characters outside override blocks.");
        var second = line.Clone(); second.Text = AssText.LeadingOverrides(text[..cursor]) + text[cursor..];
        var midpoint = ((line.StartMilliseconds ?? 0) + (line.EndMilliseconds ?? 0)) / 2;
        second.Start = AssTime.Format(midpoint);
        using var transaction = Undo.BeginTransaction("Split subtitle");
        SetField(line, "Text", text[..cursor], "Split subtitle"); SetField(line, "End", AssTime.Format(midpoint), "Split subtitle");
        var before = Document.Events.ElementAtOrDefault(Document.Events.IndexOf(line) + 1);
        Structure("Insert split subtitle", () => Document.Insert(second, before)); transaction.Commit(); return second;
    }
    public void Join(IEnumerable<AssEvent> selection)
    {
        var lines = Ordered(selection); if (lines.Length < 2) return;
        using var transaction = Undo.BeginTransaction("Join subtitles");
        SetField(lines[0], "Text", string.Join("\\N", lines.Select(l => l.Text)), "Join subtitles");
        SetField(lines[0], "Start", AssTime.Format(lines.Min(l => l.StartMilliseconds ?? 0)), "Join subtitles");
        SetField(lines[0], "End", AssTime.Format(lines.Max(l => l.EndMilliseconds ?? 0)), "Join subtitles");
        Delete(lines.Skip(1)); transaction.Commit();
    }
    public string Copy(IEnumerable<AssEvent> selection)
    {
        var doc = AssDocument.CreateEmpty(); foreach (var line in Ordered(selection)) doc.Insert(line.Clone()); return doc.Serialize();
    }
    public IReadOnlyList<AssEvent> Paste(string text, AssEvent? before)
    {
        if (before is not null) Require(before);
        var parsed = AssDocument.Parse(text.StartsWith('[') ? text : "[Events]\nFormat: " + string.Join(",", AssDocument.EventFormat) + "\n" + text);
        var copies = parsed.Events.Select(l => l.Clone()).ToArray();
        if (copies.Length == 0) { var line = Document.NewEvent(); line.Text = text; copies = [line]; }
        Structure("Paste subtitles", () => { foreach (var line in copies) Document.Insert(line, before); }); return copies;
    }
    public AssStyle AddStyle(AssStyle? source = null)
    {
        var style = source?.Clone() ?? Document.NewStyle(); var stem = style.Name; var n = 1;
        while (Document.Styles.Any(s => s.Name == style.Name)) style.Name = stem + " " + n++;
        Structure("Create style", () => Document.Insert(style)); return style;
    }
    public void EditStyle(AssStyle style, IReadOnlyDictionary<string, string> values)
    {
        Require(style); var probe = style.Clone(); foreach (var pair in values) probe.Set(pair.Key, pair.Value);
        if (string.IsNullOrWhiteSpace(probe.Name) || Document.Styles.Any(s => !ReferenceEquals(s, style) && s.Name == probe.Name)) throw new ArgumentException("Style names must be nonempty and unique.");
        using var transaction = Undo.BeginTransaction("Edit style"); var oldName = style.Name;
        foreach (var pair in values) SetField(style, pair.Key, pair.Value, "Edit style");
        if (oldName != style.Name) ReplaceStyleReferences(oldName, style.Name);
        transaction.Commit();
    }
    public void DeleteStyle(AssStyle style, string? replacement = null)
    {
        Require(style);
        var references = Document.Events.Where(l => l.Style == style.Name || AssStyleReferences.Uses(l.Text, style.Name)).ToArray();
        if (references.Length > 0 && !Document.Styles.Any(s => s != style && s.Name == replacement))
            throw new ArgumentException("This style is used by subtitles. Choose a different replacement style before deleting.");
        using var transaction = Undo.BeginTransaction("Delete style");
        if (references.Length > 0) ReplaceStyleReferences(style.Name, replacement!);
        Structure("Delete style", () => Document.Remove(style)); transaction.Commit();
    }
    public void MoveStyle(AssStyle style, int direction) { Require(style); var i = Document.Styles.IndexOf(style) + Math.Sign(direction); if (i >= 0 && i < Document.Styles.Count) Structure("Reorder styles", () => Document.Swap(style, Document.Styles[i])); }
    public void DeleteStyles(IEnumerable<AssStyle> selection, string? replacement=null)
    {
        var set=selection.ToHashSet();foreach(var style in set)Require(style);if(set.Count==0)return;
        var names=set.Select(s=>s.Name).ToHashSet(StringComparer.Ordinal);
        var used=Document.Events.Any(e=>names.Contains(e.Style)||names.Any(n=>AssStyleReferences.Uses(e.Text,n)));
        if(used&&!Document.Styles.Any(s=>!set.Contains(s)&&s.Name==replacement))throw new ArgumentException("Choose a surviving replacement style.");
        using var transaction=Undo.BeginTransaction("Delete styles");
        if(used)foreach(var name in names)ReplaceStyleReferences(name,replacement!);
        Structure("Delete styles",()=>{foreach(var style in set)Document.Remove(style);});transaction.Commit();
    }
    private void ReplaceStyleReferences(string oldName, string newName)
    {
        foreach (var line in Document.Events)
        {
            if (line.Style == oldName) SetField(line, "Style", newName, "Replace style references");
            var text = AssStyleReferences.Replace(line.Text, oldName, newName);
            if (text != line.Text) SetField(line, "Text", text, "Replace style reset references");
        }
    }
    public IReadOnlyList<AssStyle> CopyStyles(IEnumerable<AssStyle> source)
    {
        var copies = source.Select(s => s.Clone()).ToArray();
        var names = Document.Styles.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var copy in copies)
        {
            var stem = copy.Name; var n = 1;
            if (string.IsNullOrWhiteSpace(stem)) throw new ArgumentException("Style names must be nonempty.");
            while (!names.Add(copy.Name)) copy.Name = stem + " " + n++;
        }
        if (copies.Length > 0) Structure("Copy styles", () => { foreach (var copy in copies) Document.Insert(copy); });
        return copies;
    }
    public string CopyStyleText(IEnumerable<AssStyle> source)
    {
        var document = AssDocument.Parse("[V4+ Styles]\n");
        foreach (var style in source) { Require(style); document.Insert(style.Clone()); }
        return document.Serialize();
    }
    public IReadOnlyList<AssStyle> PasteStyles(string text)
    {
        var parsed = AssDocument.Parse(text.TrimStart().StartsWith('[') ? text : "[V4+ Styles]\nFormat: " + string.Join(",", AssDocument.StyleFormat) + "\n" + text);
        if (parsed.Styles.Count == 0 || parsed.Styles.Any(s => s.Fields.Length != s.Format.Length || !s.FieldNames.Contains("Name", StringComparer.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(s.Name)))
            throw new ArgumentException("Clipboard contains no complete ASS styles. Nothing was pasted.");
        return CopyStyles(parsed.Styles);
    }
    public void ReorderStyles(IEnumerable<AssStyle> selection, StyleOrder action)
    {
        var set = selection.ToHashSet(); foreach (var style in set) Require(style);
        var order = Document.Styles.ToList();
        if (action == StyleOrder.Sort) order = order.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Name, StringComparer.Ordinal).ToList();
        else if (action is StyleOrder.Top or StyleOrder.Bottom)
            order = (action == StyleOrder.Top ? order.Where(set.Contains).Concat(order.Where(s => !set.Contains(s))) : order.Where(s => !set.Contains(s)).Concat(order.Where(set.Contains))).ToList();
        else
        {
            var indices = action == StyleOrder.Up ? Enumerable.Range(1, Math.Max(0, order.Count - 1)) : Enumerable.Range(0, Math.Max(0, order.Count - 1)).Reverse();
            foreach (var i in indices)
            {
                var j = i + (action == StyleOrder.Up ? -1 : 1);
                if (set.Contains(order[i]) && !set.Contains(order[j])) (order[i], order[j]) = (order[j], order[i]);
            }
        }
        if (order.SequenceEqual(Document.Styles)) return;
        Structure(action == StyleOrder.Sort ? "Sort styles" : "Reorder styles", () =>
        {
            var slots = Document.Source.Select((line, index) => (line, index)).Where(p => p.line.Record is AssStyle).ToArray();
            for (var i = 0; i < slots.Length; i++) Document.Source[slots[i].index] = slots[i].line with { Record = order[i] };
            Document.Restore(Document.Source.ToArray());
        });
    }
    internal void DeleteLibraryStyle(AssStyle style)
    {
        Require(style);if(Document.Events.Count!=0)throw new InvalidOperationException("Library deletion requires a style-only document.");
        Structure("Delete library preset",()=>Document.Remove(style));
    }
    public void SetScriptInfo(IReadOnlyDictionary<string, string> values)
    {
        var changed=values.Where(p=>Document.GetScriptInfo(p.Key)!=p.Value).ToArray();if(changed.Length==0)return;
        Structure("Edit Script Info", () => { foreach (var pair in changed) Document.SetScriptInfo(pair.Key, pair.Value); });
    }
    public void SetProjectProperties(IReadOnlyDictionary<string, string> values)
    {
        var changed = values.Where(p => Document.GetSectionValue("[Aegisub Project Garbage]", p.Key) != p.Value).ToArray();
        if (changed.Length == 0) return;
        Structure("Edit project properties", () => { foreach (var pair in changed) Document.SetSectionValue("[Aegisub Project Garbage]", pair.Key, pair.Value); });
    }
}

public static class AssText
{
    public static int SafeSplitIndex(string text, int cursor)
    {
        cursor = Math.Clamp(cursor, 0, text.Length);
        var boundaries = StringInfo.ParseCombiningCharacters(text);
        if (cursor < text.Length) cursor = boundaries.LastOrDefault(i => i <= cursor);
        var open = text.LastIndexOf('{', Math.Max(0, cursor - 1), cursor);
        var close = text.LastIndexOf('}', Math.Max(0, cursor - 1), cursor);
        if (open > close) return open;
        if (cursor > 0 && cursor < text.Length && text[cursor - 1] == '\\' && text[cursor] is 'N' or 'n' or 'h') cursor--;
        return cursor;
    }
    public static string LeadingOverrides(string text)
    {
        var end = 0; while (end < text.Length && text[end] == '{') { var close = text.IndexOf('}', end); if (close < 0) break; end = close + 1; } return text[..end];
    }
}
