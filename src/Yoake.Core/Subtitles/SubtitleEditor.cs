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
        Undo.Execute(new DelegateUndoOperation(name, () => record.RestoreFields(old), () => record.RestoreFields(next)));
    }
    public void EditEvent(AssEvent line, IReadOnlyDictionary<string, string> values)
    {
        Require(line);
        var probe = line.Clone();
        foreach (var pair in values) probe.Set(pair.Key, pair.Value);
        if (!AssTime.TryParse(probe.Start, out var start) || !AssTime.TryParse(probe.End, out var end) || end < start) throw new ArgumentException("Use h:mm:ss.cc times, with end at or after start.");
        foreach (var field in new[] { "Layer", "MarginL", "MarginR", "MarginV" })
            if (probe.Index(field) >= 0 && (!int.TryParse(probe.Get(field), out var number) || number < 0)) throw new ArgumentException($"{field} must be a non-negative integer.");
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
            try { action(); after = Document.Source.ToArray(); }
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
        if (direction > 0) Array.Reverse(lines);
        Structure("Move subtitles", () => { foreach (var line in lines) { var i = Document.Events.IndexOf(line) + Math.Sign(direction); if (i >= 0 && i < Document.Events.Count && !set.Contains(Document.Events[i])) Document.Swap(line, Document.Events[i]); } });
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
        if (oldName != style.Name) foreach (var line in Document.Events.Where(l => l.Style == oldName)) SetField(line, "Style", style.Name, "Rename style references");
        transaction.Commit();
    }
    public void DeleteStyle(AssStyle style, string replacement)
    {
        Require(style);
        if (!Document.Styles.Any(s => s != style && s.Name == replacement)) throw new ArgumentException("Choose a different replacement style before deleting.");
        using var transaction = Undo.BeginTransaction("Delete style");
        foreach (var line in Document.Events.Where(l => l.Style == style.Name)) SetField(line, "Style", replacement, "Replace style references");
        Structure("Delete style", () => Document.Remove(style)); transaction.Commit();
    }
    public void MoveStyle(AssStyle style, int direction) { Require(style); var i = Document.Styles.IndexOf(style) + Math.Sign(direction); if (i >= 0 && i < Document.Styles.Count) Structure("Reorder styles", () => Document.Swap(style, Document.Styles[i])); }
    public void SetScriptInfo(IReadOnlyDictionary<string, string> values) => Structure("Edit Script Info", () => { foreach (var pair in values) Document.SetScriptInfo(pair.Key, pair.Value); });
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
