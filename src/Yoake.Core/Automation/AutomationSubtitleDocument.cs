using System.Globalization;
using Yoake.Core.Subtitles;
using Yoake.Core.Undo;

namespace Yoake.Core.Automation;

// Aegisub 3.2.2's file-indexed view, independent of the live document.
// An entry is replaced, never mutated; checkpoint arrays share unchanged entries.
public sealed class AutomationSubtitleDocument : IDisposable
{
    private enum Group { Info, Style, Dialogue }
    private sealed record Entry(Guid Id, Group Group, AssDocument.SourceLine Source);
    private sealed record Checkpoint(string Name, Entry[] Entries);
    private readonly AssDocument _document;
    private readonly long _revision;
    private readonly bool _writable;
    private readonly AssDocument.SourceLine[] _source;
    private readonly Dictionary<int, Group> _slots = [];
    private readonly Dictionary<Guid, AssRecord> _records = [];
    private readonly Dictionary<(Guid Id, string Schema), AssRecord> _recordVariants = [];
    private readonly AssEvent _eventTemplate;
    private readonly AssStyle _styleTemplate;
    private readonly Dictionary<Group, int> _last = [];
    private readonly List<Entry> _entries = [];
    private readonly List<Checkpoint> _checkpoints = [];
    private readonly Entry[] _initial;
    private bool _changed, _expired;

    // 3.2.2's parser removes project properties from the ordinary info view.
    private static readonly HashSet<string> ProjectKeys = new(StringComparer.Ordinal)
    {
        "Automation Scripts", "Export Filters", "Export Encoding", "Last Style Storage",
        "Audio URI", "Audio File", "Video File", "Timecodes File", "Keyframes File",
        "Video Zoom Percent", "Scroll Position", "Active Line", "Video Position",
        "Video AR Mode", "Video AR Value", "Aegisub Video Zoom Percent",
        "Aegisub Scroll Position", "Aegisub Active Line", "Aegisub Video Position"
    };

    public AutomationSubtitleDocument(AssDocument document, bool writable = true)
    {
        _document = document;
        _revision = document.Revision;
        _source = document.Source.ToArray();
        _eventTemplate = document.NewEvent(); _styleTemplate = document.NewStyle();
        _writable = writable;
        var section = "";
        List<Entry> info = [], styles = [], events = [];
        for (var i = 0; i < _source.Length; i++)
        {
            var line = _source[i]; var trim = line.Raw.Trim();
            if (trim.StartsWith('[') && trim.EndsWith(']')) { section = trim; continue; }
            Group? group = line.Record switch
            {
                AssEvent => Group.Dialogue, AssStyle => Group.Style, _ => null
            };
            if (group is null && section.Equals("[Script Info]", StringComparison.OrdinalIgnoreCase))
            {
                var colon = line.Raw.IndexOf(':');
                if (colon > 0 && !trim.StartsWith(';'))
                {
                    var key = line.Raw[..colon].Trim();
                    if (!ProjectKeys.Contains(key) && !key.StartsWith("Automation Settings ", StringComparison.Ordinal))
                        group = Group.Info;
                }
            }
            if (group is not { } kind) continue;
            _slots[i] = kind;
            var id = Guid.NewGuid();
            if (line.Record is { } record)
            {
                _records[id] = record;
                _recordVariants[(id, Schema(record))] = record;
            }
            var entry = new Entry(id, kind, line with { Record = Clone(line.Record) });
            (kind == Group.Info ? info : kind == Group.Style ? styles : events).Add(entry);
        }
        _entries.AddRange(info); _entries.AddRange(styles); _entries.AddRange(events);
        _initial = _entries.ToArray(); Reindex();
    }

    public int Count { get { RequireAlive(); return _entries.Count; } }
    public AutomationLine Read(int index)
    {
        RequireAlive(); var entry = _entries[CheckIndex(index)];
        var line = new AutomationLine { Template = entry.Source.Record };
        line["class"] = entry.Group.ToString().ToLowerInvariant();
        line["section"] = Header(entry.Group);
        line["raw"] = entry.Source.Record?.Serialize() ?? entry.Source.Raw;
        if (entry.Source.Record is AssEvent e)
        {
            line["comment"] = e.IsComment;
            line["layer"] = Int(e.Layer);
            line["start_time"] = checked((int)(e.StartMilliseconds ?? 0));
            line["end_time"] = checked((int)(e.EndMilliseconds ?? 0));
            line["style"] = e.Style; line["actor"] = e.Actor;
            line["margin_l"] = Int(e.MarginL); line["margin_r"] = Int(e.MarginR);
            line["margin_t"] = line["margin_b"] = Int(e.MarginV);
            line["effect"] = e.Effect; line["text"] = e.Text;
            line["extra"] = new Dictionary<string, string>(StringComparer.Ordinal);
        }
        else if (entry.Source.Record is AssStyle s)
        {
            foreach (var (lua, ass) in StyleStrings) line[lua] = s.Get(ass);
            foreach (var (lua, ass) in StyleNumbers) line[lua] = Number(s.Get(ass));
            foreach (var (lua, ass) in StyleIntegers) line[lua] = Int(s.Get(ass));
            foreach (var (lua, ass) in StyleBooleans) line[lua] = Int(s.Get(ass)) != 0;
            foreach (var (lua, ass) in StyleColors) line[lua] = s.Get(ass).TrimEnd('&') + "&";
            line["margin_b"] = line["margin_t"]; line["relative_to"] = 2;
        }
        else
        {
            var colon = entry.Source.Raw.IndexOf(':');
            line["key"] = entry.Source.Raw[..colon].Trim();
            line["value"] = entry.Source.Raw[(colon + 1)..].Trim();
        }
        return line;
    }

    public void Write(int index, AutomationLine? line)
    {
        RequireWritable();
        if (index == 0) { Append(line ?? throw new ArgumentException("Can't convert nil to AssEntry.")); return; }
        if (index < 0) { Insert(checked(-index), line ?? throw new ArgumentException("Can't convert nil to AssEntry.")); return; }
        var slot = CheckIndex(index);
        if (line is null) { Delete(index); return; }
        var entry = ConvertLine(line, _entries[slot]);
        _entries[slot] = entry; _changed = true; Reindex();
    }

    public void Append(params AutomationLine[] lines)
    {
        RequireWritable();
        // Validate all arguments before modifying the working collection.
        var converted = lines.Select(l => ConvertLine(l, null)).ToArray();
        foreach (var entry in converted)
        {
            var index = _last.TryGetValue(entry.Group, out var last) ? last + 1 : _entries.Count;
            _entries.Insert(index, entry);
            if (index == _entries.Count - 1) _last[entry.Group] = index;
            else Reindex();
        }
        _changed |= converted.Length > 0;
    }

    public void Insert(int before, params AutomationLine[] lines)
    {
        RequireWritable();
        if (before < 1 || before > _entries.Count + 1) throw new ArgumentOutOfRangeException(nameof(before));
        if (before == _entries.Count + 1) { Append(lines); return; }
        var converted = lines.Select(l => ConvertLine(l, null)).ToArray();
        _entries.InsertRange(before - 1, converted); _changed |= converted.Length > 0; Reindex();
    }

    public void Delete(params int[] indexes)
    {
        RequireWritable();
        var sorted = indexes.Select(CheckIndex).Order().ToArray();
        var cursor = 0; var output = 0;
        // Preserve 3.2.2's duplicate-index behavior: do not deduplicate.
        for (var i = 0; i < _entries.Count; i++)
        {
            if (cursor < sorted.Length && sorted[cursor] == i) cursor++;
            else _entries[output++] = _entries[i];
        }
        _changed |= output != _entries.Count;
        _entries.RemoveRange(output, _entries.Count - output); Reindex();
    }

    public void DeleteRange(int first, int last)
    {
        RequireWritable();
        // Source uses unsigned conversion. Negative endpoints are rejected here
        // rather than reproducing platform-dependent unsigned wraparound.
        if (first < 0 || last < 0) throw new ArgumentOutOfRangeException(nameof(first));
        var start = Math.Max(first, 1) - 1; var end = Math.Min(last, _entries.Count);
        if (start >= end) return;
        _entries.RemoveRange(start, end - start); _changed = true; Reindex();
    }

    public void SetUndoPoint(string name)
    {
        RequireWritable();
        if (!_changed) return;
        _checkpoints.Add(new(name, _entries.ToArray())); _changed = false;
    }

    public int IndexOf(AssEvent line)
    {
        RequireAlive();
        for (var i = 0; i < _entries.Count; i++)
            if (_records.TryGetValue(_entries[i].Id, out var record) && ReferenceEquals(record, line)) return i + 1;
        return 0;
    }

    public IReadOnlyList<int> SelectionIndexes(IEnumerable<AssEvent> events) =>
        events.Select(IndexOf).Where(i => i > 0).Distinct().Order().ToArray();

    // Must run on the host's document thread, only after successful script return.
    public IReadOnlyDictionary<int, AssEvent> Commit(SubtitleEditor editor, string macroName,
        AutomationSelectionState? initialSelection = null, AutomationMacroResult? result = null, Action<AutomationSelectionState>? restoreSelection = null)
    {
        RequireWritable();
        if (!ReferenceEquals(editor.Document, _document)) throw new InvalidOperationException("Automation editor ownership mismatch.");
        if (_document.Revision != _revision) throw new InvalidOperationException("The document changed while Automation was running.");
        if (editor.Undo.IsTransactionActive) throw new InvalidOperationException("Finish the editor gesture before Automation commit.");
        var checkpoints = _checkpoints.ToList();
        if (_changed) checkpoints.Add(new(macroName, _entries.ToArray()));
        var previous = _initial;
        var publishing = true;
        void Restore(Entry[] entries, bool final)
        {
            if (publishing || initialSelection is null || restoreSelection is null) return;
            var available = entries.Select(e => _records.GetValueOrDefault(e.Id)).OfType<AssEvent>().ToArray();
            AssEvent? At(int index) => index > 0 && index <= entries.Length ? _records.GetValueOrDefault(entries[index - 1].Id) as AssEvent : null;
            var selected = final && result?.Selection is { } returned
                ? returned.Select(At).OfType<AssEvent>().Distinct().ToArray()
                : initialSelection.Selection.Where(available.Contains).ToArray();
            var active = final && result?.ActiveLine is { } activeIndex ? At(activeIndex) : null;
            active ??= available.Contains(initialSelection.ActiveLine) ? initialSelection.ActiveLine : selected.FirstOrDefault() ?? available.FirstOrDefault();
            restoreSelection(new(selected.Length == 0 && active is not null ? [active] : selected, active));
        }
        List<IUndoOperation> operations = [];
        for (var pointIndex = 0; pointIndex < checkpoints.Count; pointIndex++)
        {
            var point = checkpoints[pointIndex];
            var before = previous; var after = point.Entries;
            var final = pointIndex == checkpoints.Count - 1;
            // Materialize/validate source before touching the live model.
            var beforeSource = BuildSource(before); var afterSource = BuildSource(after);
            operations.Add(new DelegateUndoOperation(point.Name,
                () => { Apply(beforeSource); Restore(before, false); },
                () => { Apply(afterSource); Restore(after, final); }));
            previous = after;
        }
        using (_document.BeginUpdate()) editor.Undo.ExecuteBatch(operations);
        publishing = false;
        _expired = true;
        Dictionary<int, AssEvent> events = [];
        for (var i = 0; i < _entries.Count; i++)
            if (_entries[i].Group == Group.Dialogue && _records.GetValueOrDefault(_entries[i].Id) is AssEvent line) events[i + 1] = line;
        return events;
    }

    public void Dispose() { _expired = true; _entries.Clear(); _checkpoints.Clear(); }
    private void RequireAlive() { if (_expired) throw new InvalidOperationException("Subtitles object is no longer valid."); }
    private void RequireWritable() { RequireAlive(); if (!_writable) throw new InvalidOperationException("Attempt to modify subtitles in read-only feature context."); }
    private int CheckIndex(int index) => index > 0 && index <= _entries.Count ? index - 1 : throw new ArgumentOutOfRangeException(nameof(index), "Requested out-of-range line from subtitle file.");
    private void Reindex() { _last.Clear(); for (var i = 0; i < _entries.Count; i++) _last[_entries[i].Group] = i; }
    private static AssRecord? Clone(AssRecord? record) => record switch { AssEvent e => e.Clone(), AssStyle s => s.Clone(), _ => null };
    private static string Header(Group group) => group switch { Group.Info => "[Script Info]", Group.Style => "[V4+ Styles]", _ => "[Events]" };
    private static int Int(string text) => (int)Math.Clamp(Math.Truncate(Number(text)), int.MinValue, int.MaxValue);
    private static double Number(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : 0;

    private Entry ConvertLine(AutomationLine line, Entry? replaced)
    {
        var kind = line.String("class").ToLowerInvariant();
        var group = kind switch { "info" => Group.Info, "style" => Group.Style, "dialogue" => Group.Dialogue, _ => throw new ArgumentException($"Found line with unknown class: {kind}") };
        var template = line.Template ?? (replaced?.Group == group ? replaced.Source.Record : null);
        AssRecord? record;
        string raw = "";
        if (group == Group.Dialogue)
        {
            var e = template is AssEvent old ? old.Clone() : _eventTemplate.Clone();
            e.SetKind(line.Boolean("comment"));
            SetInt(e, e.Index("Layer") >= 0 ? "Layer" : "Marked", line.Integer("layer"));
            SetTime(e, "Start", line.Integer("start_time")); SetTime(e, "End", line.Integer("end_time"));
            SetString(e, "Style", line.String("style"));
            SetString(e, e.Index("Name") >= 0 ? "Name" : "Actor", line.String("actor"));
            SetInt(e, "MarginL", line.Integer("margin_l")); SetInt(e, "MarginR", line.Integer("margin_r")); SetInt(e, "MarginV", line.Integer("margin_t"));
            SetString(e, "Effect", line.String("effect")); SetString(e, "Text", line.String("text"));
            if (line["extra"] is not null && line["extra"] is not IReadOnlyDictionary<string, string>)
                throw new ArgumentException("dialogue extradata must be a table");
            if (line["extra"] is IReadOnlyDictionary<string, string> { Count: > 0 })
                throw new NotSupportedException("Writing Aegisub extradata is not implemented yet.");
            record = e;
        }
        else if (group == Group.Style)
        {
            var s = template is AssStyle old ? old.Clone() : _styleTemplate.Clone();
            foreach (var (lua, ass) in StyleStrings) SetString(s, ass, line.String(lua));
            foreach (var (lua, ass) in StyleNumbers) SetNumber(s, ass, line.Number(lua));
            foreach (var (lua, ass) in StyleIntegers) SetInt(s, ass, line.Integer(lua));
            foreach (var (lua, ass) in StyleBooleans) { var b = line.Boolean(lua); if ((Int(s.Get(ass)) != 0) != b) SetInt(s, ass, b ? -1 : 0); }
            foreach (var (lua, ass) in StyleColors)
            {
                var value = line.String(lua);
                if (!AssColor.TryParse(value, out var color)) throw new ArgumentException($"Invalid style color {lua}.");
                if (!AssColor.TryParse(s.Get(ass), out var previous) || previous != color) SetString(s, ass, value.TrimEnd('&'));
            }
            record = s;
        }
        else
        {
            var key = line.String("key"); var value = line.String("value");
            if (key.IndexOfAny([':', '\r', '\n']) >= 0 || value.IndexOfAny(['\r', '\n']) >= 0) throw new ArgumentException("Invalid Script Info value.");
            raw = key + ": " + value;
            if (replaced?.Group == Group.Info)
            {
                var prior = replaced.Source.Raw; var colon = prior.IndexOf(':');
                if (prior[..colon].Trim() == key && prior[(colon + 1)..].Trim() == value) raw = prior;
            }
            record = null;
        }
        return new(replaced?.Id ?? Guid.NewGuid(), group, new(raw, replaced?.Source.Ending ?? "\r\n", record));
    }

    private static void SetString(AssRecord record, string field, string value)
    {
        if (record.Get(field) == value) return;
        record.Set(field, value);
    }
    private static void SetInt(AssRecord record, string field, int value) { if (Int(record.Get(field)) != value) SetString(record, field, value.ToString(CultureInfo.InvariantCulture)); }
    private static void SetNumber(AssRecord record, string field, double value) { if (Number(record.Get(field)) != value) SetString(record, field, value.ToString("G17", CultureInfo.InvariantCulture)); }
    private static void SetTime(AssEvent record, string field, int value)
    {
        var previous = field == "Start" ? record.StartMilliseconds : record.EndMilliseconds;
        if (previous != value) SetString(record, field, AssTime.Format(value));
    }

    private sealed record Materialized(Guid? Id, AssDocument.SourceLine Source);
    private static string Schema(AssRecord record) => (record is AssEvent ? "event:" : "style:") + string.Join('\0', record.Format);
    private Materialized[] BuildSource(Entry[] entries)
    {
        var queues = Enum.GetValues<Group>().ToDictionary(g => g, g => new Queue<Entry>(entries.Where(e => e.Group == g)));
        var lastSlots = _slots.GroupBy(p => p.Value).ToDictionary(g => g.Key, g => g.Max(p => p.Key));
        List<Materialized> output = [];
        for (var i = 0; i < _source.Length; i++)
        {
            if (!_slots.TryGetValue(i, out var group)) { output.Add(new(null, _source[i])); continue; }
            if (queues[group].TryDequeue(out var entry)) output.Add(new(entry.Id, entry.Source with { Ending = _source[i].Ending }));
            if (i != lastSlots[group]) continue;
            while (queues[group].TryDequeue(out entry)) output.Add(new(entry.Id, entry.Source));
        }
        foreach (var (group, queue) in queues)
        {
            if (queue.Count == 0) continue;
            // A group with no original slots still belongs in its section.
            var header = output.FindIndex(l => l.Source.Raw.Trim().Equals(Header(group), StringComparison.OrdinalIgnoreCase));
            var insert = header + 1;
            if (header < 0) { insert = output.Count; output.Add(new(null, new(Header(group), "\r\n"))); insert++; }
            else while (insert < output.Count && !output[insert].Source.Raw.TrimStart().StartsWith('[')) insert++;
            output.InsertRange(insert, queue.Select(e => new Materialized(e.Id, e.Source)));
        }
        for (var i = 0; i + 1 < output.Count; i++)
            if (output[i].Source.Ending.Length == 0) output[i] = output[i] with { Source = output[i].Source with { Ending = "\r\n" } };
        return output.ToArray();
    }

    private void Apply(Materialized[] state)
    {
        using (_document.BeginUpdate())
        {
            var source = new AssDocument.SourceLine[state.Length];
            for (var i = 0; i < state.Length; i++)
            {
                var item = state[i]; var line = item.Source;
                if (item.Id is { } id && line.Record is { } model)
                {
                    var key = (id, Schema(model));
                    if (!_recordVariants.TryGetValue(key, out var live))
                        _recordVariants[key] = live = Clone(model)!;
                    _records[id] = live;
                    if (!live.Fields.SequenceEqual(model.Fields)) live.RestoreFields(model.Fields);
                    if (live is AssEvent e && e.Prefix != model.Prefix) e.RestorePrefix(model.Prefix);
                    line = line with { Record = live };
                }
                source[i] = line;
            }
            _document.Restore(source, resetCollections: true);
        }
    }

    private static readonly (string Lua, string Ass)[] StyleStrings = [("name", "Name"), ("fontname", "Fontname")];
    private static readonly (string Lua, string Ass)[] StyleNumbers = [("fontsize", "Fontsize"), ("scale_x", "ScaleX"), ("scale_y", "ScaleY"), ("spacing", "Spacing"), ("angle", "Angle"), ("outline", "Outline"), ("shadow", "Shadow")];
    private static readonly (string Lua, string Ass)[] StyleIntegers = [("borderstyle", "BorderStyle"), ("align", "Alignment"), ("margin_l", "MarginL"), ("margin_r", "MarginR"), ("margin_t", "MarginV"), ("encoding", "Encoding")];
    private static readonly (string Lua, string Ass)[] StyleBooleans = [("bold", "Bold"), ("italic", "Italic"), ("underline", "Underline"), ("strikeout", "StrikeOut")];
    private static readonly (string Lua, string Ass)[] StyleColors = [("color1", "PrimaryColour"), ("color2", "SecondaryColour"), ("color3", "OutlineColour"), ("color4", "BackColour")];
    public static void ValidateStyle(AutomationLine style)
    {
        if (!style.String("class").Equals("style", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Not a style entry.");
        foreach (var (field, _) in StyleStrings) _ = style.String(field);
        foreach (var (field, _) in StyleNumbers) _ = style.Number(field);
        foreach (var (field, _) in StyleIntegers) _ = style.Integer(field);
        foreach (var (field, _) in StyleBooleans) _ = style.Boolean(field);
        foreach (var (field, _) in StyleColors)
            if (!AssColor.TryParse(style.String(field), out _)) throw new ArgumentException($"Invalid style color {field}.");
    }
}
