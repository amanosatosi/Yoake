// Adapted from Aegisub Toshi-ban audio_timing_toshiki.cpp, ass_karaoke.cpp
// and toshiki_timing_draft.h. Copyright (c) 2011 Thomas Goyne and
// (c) 2026 JibunSenyou contributors. ISC permission:
// Permission to use, copy, modify, and distribute this software for any
// purpose with or without fee is hereby granted, provided that the above
// copyright notice and this permission notice appear in all copies.
// THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
// WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
// MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
// ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
// WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION
// OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN
// CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
using System.Globalization;
using System.Text;
using Yoake.Core.Automation;
using Yoake.Core.Audio;

namespace Yoake.Core.Subtitles;

public sealed record ToshikiSlot(int Index, int TextStart, string Text, string Tag, long Start, long End, bool Assigned, bool Current);
public sealed record ToshikiRecovery(int Row, string Source, long Start, long End, int[] Cuts, string[] Tags, long[] Boundaries, int[] Recuts, int Current, bool Existing, bool Changed);
public sealed record KaraokeSource(string Raw, string Text, int[] Offsets, int[] Cuts, string[] Tags, long[] Durations)
{
    public bool Existing => Tags.Length > 0;

    // The Automation parser's balanced top-level scanner is shared. Its frozen
    // Lua view still drops empty zero-duration groups; authoring retains them.
    public static KaraokeSource Parse(string source)
    {
        var protectedBase = new bool[source.Length];
        var sourceFuri = new bool[source.Length];
        foreach(var token in AssSyntax.Tokenize(source).Where(t=>t.Kind==AssSyntaxKind.Furigana))
            for(var p=token.Start;p<token.Start+token.Length;p++)sourceFuri[p]=true;
        for(var p=0;p<source.Length;p++)if(source[p]=='<'&&sourceFuri[p])
        {
            var pipe=p+1;while(pipe<source.Length&&!(source[pipe]=='|'&&sourceFuri[pipe]))pipe++;
            for(var q=p;q<pipe;q++)protectedBase[q]=true;
            p=pipe;
        }
        var raw = new StringBuilder(); var anchors = new List<(int Offset, string Tag, long Duration)>();
        for (var cursor = 0; cursor < source.Length;)
        {
            if (source[cursor] == '\\' && cursor + 1 < source.Length && source[cursor + 1] is '{' or '}')
            { raw.Append(source.AsSpan(cursor, 2)); cursor += 2; continue; }
            var close = source[cursor] == '{' ? source.IndexOf('}', cursor + 1) : -1;
            if (close < 0) { raw.Append(source[cursor++]); continue; }
            var content = source.AsSpan(cursor + 1, close - cursor - 1);
            if (!content.Contains('\\')) { raw.Append(source.AsSpan(cursor, close - cursor + 1)); cursor = close + 1; continue; }
            var kept = new StringBuilder(); var local = new List<(string Tag, long Duration)>();
            foreach (var span in AutomationKaraokeParser.Segments(source, cursor + 1, close))
            {
                var value = source.AsSpan(span.Start, span.Length); var match = AssTagCatalog.Match(value.Length > 0 && value[0] == '\\' ? value[1..] : value);
                var name = value.Length > 0 && value[0] == '\\' ? value.Slice(1, match.Length).ToString() : "";
                if (name == "kt" && !protectedBase[span.Start]) throw new ArgumentException("Absolute \\kt timing must be converted before Toshiki K-Timing. The source is preserved.");
                if (!protectedBase[span.Start] && name is ("k" or "K" or "kf" or "ko" or "kO"))
                {
                    if (!long.TryParse(value[(name.Length + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cs) || cs < 0 || cs > 8640000)
                        throw new ArgumentException("Invalid karaoke duration. Correct it before timing; the source is preserved.");
                    local.Add((name, checked(cs * 10)));
                }
                else kept.Append(value);
            }
            if (kept.Length > 0) raw.Append('{').Append(kept).Append('}');
            foreach (var tag in local) anchors.Add((raw.Length, tag.Tag, tag.Duration));
            cursor = close + 1;
        }
        var cleaned = raw.ToString(); var tokens = AssSyntax.Tokenize(cleaned);
        var plain = new bool[cleaned.Length]; var furi = new bool[cleaned.Length];
        foreach (var token in tokens)
            if (token.Kind is AssSyntaxKind.Text or AssSyntaxKind.Escape or AssSyntaxKind.Furigana)
                for (var i = token.Start; i < token.Start + token.Length; i++) { plain[i] = true; furi[i] = token.Kind == AssSyntaxKind.Furigana; }
        // Renderer ruby is timed on its reading, as in the Toshi-ban source map.
        // Escaped separators remain literal; two-reading/ambiguous groups stay
        // intact as one group rather than receiving corrupt internal cuts.
        var excluded = new bool[cleaned.Length];
        for (var i = 0; i < cleaned.Length; i++)
        {
            if (cleaned[i] != '<' || !furi[i]) continue;
            var end = i + 1; var pipes = new List<int>();
            for (; end < cleaned.Length; end++)
            {
                if (cleaned[end] == '\\' && end + 1 < cleaned.Length) { end += cleaned[end + 1] == '|' && end + 2 < cleaned.Length && cleaned[end + 2] == '\\' ? 2 : 1; continue; }
                if (cleaned[end] == '|' && furi[end]) pipes.Add(end);
                if (cleaned[end] == '>' && furi[end]) break;
            }
            if (end >= cleaned.Length) continue;
            if (pipes.Count == 1) { for (var j = i; j <= pipes[0]; j++) excluded[j] = true; excluded[end] = true; }
            i = end;
        }
        var text = new StringBuilder(); var offsets = new List<int>(); var actual = new List<int>(); var pendingOffset=0;
        for (var i = 0; i < cleaned.Length; i++)
        {
            if(excluded[i]){pendingOffset=i+1;continue;}
            if (plain[i]) { offsets.Add(pendingOffset); actual.Add(i); text.Append(cleaned[i]); pendingOffset=i+1; }
        }
        offsets.Add(cleaned.Length);
        actual.Add(cleaned.Length);
        int Logical(int offset) { var found = actual.BinarySearch(offset); return found >= 0 ? Math.Min(found, text.Length) : Math.Min(~found, text.Length); }
        var cuts = new List<int>(); var tags = new List<string>(); var durations = new List<long>();
        foreach (var anchor in anchors)
        {
            var logical = Logical(anchor.Offset);
            if (cuts.Count == 0 && logical > 0) { cuts.Add(0); tags.Add("k"); durations.Add(0); }
            cuts.Add(logical); tags.Add(anchor.Tag); durations.Add(anchor.Duration);
        }
        return new(cleaned, text.ToString(), offsets.ToArray(), cuts.ToArray(), tags.ToArray(), durations.ToArray());
    }
    public string Render(IReadOnlyList<int> cuts, IReadOnlyList<string> tags, IReadOnlyList<long> durations)
    {
        var output = new StringBuilder(); var copied = 0;
        for (var i = 0; i < cuts.Count; i++)
        {
            var position = Offsets[cuts[i]]; output.Append(Raw.AsSpan(copied, position - copied));
            output.Append("{\\").Append(tags[i]).Append(((durations[i] + 5) / 10).ToString(CultureInfo.InvariantCulture)).Append('}'); copied = position;
        }
        return output.Append(Raw.AsSpan(copied)).ToString();
    }
}

/// <summary>Document-local explicit slots and boundaries; no provider or clock.</summary>
public sealed class ToshikiTimingPlan
{
    private readonly KaraokeSource _source;
    private readonly List<int> _cuts = [];
    private readonly List<string> _tags = [];
    private readonly List<long> _boundaries = [];
    private readonly HashSet<int> _recuts = [];
    private bool _existing;
    public string Source { get; }
    public long Start { get; private set; }
    public long End { get; private set; }
    public int Current { get; private set; }
    public bool Changed { get; private set; }
    public int Count => _cuts.Count;
    public IReadOnlyList<long> Boundaries => _boundaries;
    public string Text => _source.Text;
    public int AssignedCount => Count == 1 ? (_existing ? 1 : 0) : _boundaries.Count == Count - 1 ? Count : _boundaries.Count;
    public ToshikiTimingPlan(string source, long start, long end, bool autoCut = true)
    {
        if (start < 0 || end < start || end > 86400000) throw new ArgumentException("Use valid line times before karaoke timing.");
        Source = source; Start = start; End = end; _source = KaraokeSource.Parse(source); _existing = _source.Existing;
        if (_existing)
        {
            _cuts.AddRange(_source.Cuts); _tags.AddRange(_source.Tags); var next = start;
            for (var i = 0; i < Count - 1; i++) { next = Math.Min(end, checked(next + _source.Durations[i])); _boundaries.Add(next); }
        }
        else { _cuts.Add(0); _tags.Add("k"); if (autoCut) AutoCut(); }
    }
    public ToshikiSlot[] Slots
    {
        get
        {
            var slots = new ToshikiSlot[Count]; var assigned = AssignedCount;
            for (var i = 0; i < Count; i++)
            {
                var left = i == 0 ? Start : i <= _boundaries.Count ? _boundaries[i - 1] : End;
                var right = i < _boundaries.Count ? _boundaries[i] : End;
                if (i > assigned) left = right = End;
                slots[i] = new(i, _cuts[i], Text[_cuts[i]..(i + 1 < Count ? _cuts[i + 1] : Text.Length)], _tags[i], left, right, i < assigned, i == Current);
            }
            return slots;
        }
    }
    public AudioClipInterval PrimaryRange { get { var slot = Slots[Current]; return new(slot.Start, slot.End); } }
    public bool Append(long ms)
    {
        if (Count < 2 || _boundaries.Count >= Count - 1) return false;
        var minimum = _boundaries.Count == 0 ? Start : _boundaries[^1];
        if (ms < minimum || ms > End) return false;
        _boundaries.Add(Clamp(ms, minimum, End)); Current = Math.Min(_boundaries.Count, Count - 1); Changed = true; return true;
    }
    public int Nearby(long ms, long sensitivity)
    {
        var result = -1; var distance = sensitivity + 1;
        for (var i = 0; i < _boundaries.Count; i++) if (Math.Abs(ms - _boundaries[i]) <= Math.Min(sensitivity, distance)) { result = i; distance = Math.Abs(ms - _boundaries[i]); }
        return result;
    }
    public int RecutAt(long ms) => _recuts.Order().FirstOrDefault(i => i < _boundaries.Count && ms >= (i == 0 ? Start : _boundaries[i - 1]) && ms < _boundaries[i], -1);
    public int AssignedAt(long ms) => Array.FindIndex(Slots, s => s.Assigned && ms >= s.Start && ms < s.End);
    public void Select(int index) => Current = Math.Clamp(index, 0, Count - 1);
    public bool Navigate(int delta) { var index = Current + delta; if (index < 0 || index >= Math.Max(1, _boundaries.Count + 1)) return false; Select(index); return true; }
    public void MoveBoundary(int index, long ms)
    {
        if (index < 0 || index >= _boundaries.Count) return;
        var position = Clamp(ms, index == 0 ? Start : _boundaries[index - 1], index + 1 < _boundaries.Count ? _boundaries[index + 1] : End);
        if (position == _boundaries[index]) return; _boundaries[index] = position; Changed = true;
    }
    public void Adjust(long startDelta, long endDelta)
    {
        if (startDelta != 0 && Current > 0) MoveBoundary(Current - 1, checked(_boundaries[Current - 1] + startDelta));
        if (endDelta != 0 && Current < _boundaries.Count) MoveBoundary(Current, checked(_boundaries[Current] + endDelta));
    }
    public void Lead(long startDelta, long endDelta)
    { Start = Math.Max(0, checked(Start + startDelta)); End = Math.Max(Start, checked(End + endDelta)); for (var i = 0; i < _boundaries.Count; i++) _boundaries[i] = Clamp(_boundaries[i], Start, End); Changed = true; }
    public void SetTag(string tag, bool currentOnly = false)
    {
        if (tag is not ("k" or "K" or "kf" or "ko" or "kO")) throw new ArgumentException("Choose k, K, kf, ko or kO.");
        if (currentOnly) _tags[Current] = tag; else for (var i = 0; i < Count; i++) _tags[i] = tag;
        Changed = true;
    }
    public void Split(int slot, int logicalPosition)
    {
        if (slot < 0 || slot >= Count || logicalPosition < _cuts[slot] || logicalPosition > (slot + 1 < Count ? _cuts[slot + 1] : Text.Length) ||
            !SafeCuts().Contains(logicalPosition)) return;
        var assigned = slot < AssignedCount;
        if (assigned)
        {
            _boundaries.Insert(slot, slot < _boundaries.Count ? _boundaries[slot] : End);
            var moved = _recuts.Select(i => i >= slot ? i + 1 : i).ToArray(); _recuts.Clear(); _recuts.UnionWith(moved); _recuts.Add(slot);
        }
        _cuts.Insert(slot + 1, logicalPosition); _tags.Insert(slot + 1, _tags[slot]); if (slot < Current) Current++; Changed = true;
    }
    public void Join(int right)
    {
        if (right <= 0 || right >= Count) return;
        var boundary = right - 1; if (boundary < _boundaries.Count) _boundaries.RemoveAt(boundary);
        var moved = _recuts.Where(i => i != boundary).Select(i => i > boundary ? i - 1 : i).ToArray(); _recuts.Clear(); _recuts.UnionWith(moved);
        _cuts.RemoveAt(right); _tags.RemoveAt(right); if (right <= Current) Current--; Changed = true;
    }
    public int[] SafeCuts()
    {
        var positions = StringInfo.ParseCombiningCharacters(Text).Append(Text.Length).ToArray();
        // ASS escapes, escaped furigana separators and ambiguous angle groups
        // are atomic. A grapheme cut must also preserve their source spelling.
        var unsafePositions = new HashSet<int>();
        for (var i = 0; i < Text.Length; i++)
        {
            if (Text[i] == '\\' && i + 1 < Text.Length) { unsafePositions.Add(i + 1); if (Text[i + 1] == '|' && i + 2 < Text.Length && Text[i + 2] == '\\') unsafePositions.Add(++i + 1); i++; }
            else if (Text[i] == '<') { var end = Text.IndexOf('>', i + 1); if (end >= 0) { for (var j = i + 1; j <= end; j++) unsafePositions.Add(j); i = end; } }
        }
        return positions.Where(i => !unsafePositions.Contains(i)).ToArray();
    }
    public void AutoCut()
    {
        if (_existing || _boundaries.Count > 0) return;
        var safe = SafeCuts(); var units = ToshikiKanaCut.Units(Text, safe);
        _cuts.Clear(); _tags.Clear(); var position = 0;
        foreach (var unit in units) { _cuts.Add(position); _tags.Add("k"); position += unit.Length; }
        if (Count == 0) { _cuts.Add(0); _tags.Add("k"); } Current = 0; Changed = Count > 1;
    }
    public string PreviewText => Count == 1 && !_existing ? Source : _source.Render(_cuts, _tags, Slots.Select(s => s.End - s.Start).ToArray());
    public ToshikiRecovery Capture(int row) => new(row, Source, Start, End, _cuts.ToArray(), _tags.ToArray(), _boundaries.ToArray(), _recuts.Order().ToArray(), Current, _existing, Changed);
    public static ToshikiTimingPlan Restore(ToshikiRecovery recovery)
    {
        if (recovery.Source is null || recovery.Cuts is null || recovery.Tags is null || recovery.Boundaries is null || recovery.Recuts is null || recovery.Cuts.Length is <1 or >100000 || recovery.Tags.Length != recovery.Cuts.Length)
            throw new InvalidDataException("Invalid karaoke recovery slots.");
        var plan = new ToshikiTimingPlan(recovery.Source, recovery.Start, recovery.End, false);
        var safe = plan.SafeCuts().ToHashSet();
        if (recovery.Cuts[0] != 0 || recovery.Cuts.Any(c => !safe.Contains(c)) || !recovery.Cuts.SequenceEqual(recovery.Cuts.Order()) ||
            recovery.Boundaries.Length >= recovery.Cuts.Length || recovery.Boundaries.Any(b => b < recovery.Start || b > recovery.End || b % 10 != 0) || !recovery.Boundaries.SequenceEqual(recovery.Boundaries.Order()) ||
            recovery.Current < 0 || recovery.Current >= recovery.Cuts.Length || recovery.Recuts.Any(i => i < 0 || i >= recovery.Boundaries.Length) || recovery.Tags.Any(t => t is not ("k" or "K" or "kf" or "ko" or "kO")))
            throw new InvalidDataException("Invalid karaoke recovery boundaries.");
        plan._cuts.Clear(); plan._cuts.AddRange(recovery.Cuts); plan._tags.Clear(); plan._tags.AddRange(recovery.Tags);
        plan._boundaries.Clear(); plan._boundaries.AddRange(recovery.Boundaries); plan._recuts.Clear(); plan._recuts.UnionWith(recovery.Recuts);
        plan.Current = recovery.Current; plan._existing = recovery.Existing; plan.Changed = recovery.Changed; return plan;
    }
    private static long Clamp(long ms, long minimum, long maximum) => Math.Clamp(checked((ms + 5) / 10 * 10), minimum, maximum);
}

internal static class ToshikiKanaCut
{
    // Song-sane AutoKanaCut from ass_karaoke.cpp: no forced one-tap-per-mora.
    public static string[] Units(string text, int[] safe)
    {
        var units = new List<string>(); var prefix = "";
        bool Kana(int c) => c is >=0x3040 and <=0x309f or >=0x30a0 and <=0x30ff or >=0x31f0 and <=0x31ff or >=0xff66 and <=0xff9f;
        bool Cjk(int c) => c is >=0x3400 and <=0x4dbf or >=0x4e00 and <=0x9fff or >=0xf900 and <=0xfaff or >=0x20000 and <=0x2ebef;
        bool Roman(int c) => c < 128 && char.IsAsciiLetterOrDigit((char)c);
        bool Space(int c) => c is 32 or 9 or 0x3000;
        bool Special(string s) => s.Length == 0 || s is "#" or "＃" || Space(Rune.GetRuneAt(s, 0).Value);
        bool Small(int c) => "ぁぃぅぇぉゃゅょゎァィゥェォャュョヮｧｨｩｪｫｬｭｮ".EnumerateRunes().Any(r => r.Value == c);
        bool Open(int c) => "([<‘“〈《「『【〔〖〘〚〝（［｛".EnumerateRunes().Any(r => r.Value == c);
        bool Close(int c) => ")] >’”〉》」』】〕〗〙〛〞〟）］｝".Replace(" ", "").EnumerateRunes().Any(r => r.Value == c);
        bool Punctuation(int c) => c < 128 && !Roman(c) && !Space(c) || c is >=0x3001 and <=0x303f or 0xff01 or 0xff0c or 0xff0e or 0xff1f;
        void Push(string s) { units.Add(prefix + s); prefix = ""; }
        for (var i = 0; i < safe.Length - 1; i++)
        {
            var unit = text[safe[i]..safe[i + 1]]; var cp = Rune.GetRuneAt(unit, 0).Value;
            if (unit.StartsWith('<') || unit.StartsWith('\\')) { Push(unit); continue; }
            if (cp is '#' or 0xff03) { if (prefix.Length > 0) Push(""); Push(unit); continue; }
            if (Open(cp)) { prefix += unit; continue; }
            if (Close(cp)) { if (prefix.Length > 0) prefix += unit; else if (units.Count == 0 || Special(units[^1])) Push(unit); else units[^1] += unit; continue; }
            if (Space(cp)) { if (prefix.Length > 0) Push(""); Push(unit); continue; }
            if (Small(cp) || cp is 0x3099 or 0x309a || Punctuation(cp)) { if (prefix.Length > 0 || units.Count == 0 || units[^1].Length == 0) Push(unit); else units[^1] += unit; continue; }
            var last = units.Count > 0 && units[^1].Length > 0 ? units[^1].EnumerateRunes().Last().Value : 0;
            var attach = cp is 0x3063 or 0x30c3 or 0xff6f or 0x30fc or 0xff70 || cp == 0x3046 && "こごそぞとどのほぼぽもよょろを".Contains((char)last) || cp == 0x30a6 && "コゴソゾトドノホボポモヨョロヲ".Contains((char)last) || cp is 0x3046 or 0x30a6 && Small(last);
            if (Kana(cp)) { if (prefix.Length == 0 && units.Count > 0 && !Special(units[^1]) && attach) units[^1] += unit; else Push(unit); continue; }
            if (Cjk(cp)) { Push(unit); continue; }
            if (prefix.Length > 0 || units.Count == 0 || units[^1].Length == 0) Push(unit);
            else if (!Roman(cp) || Roman(Rune.GetRuneAt(units[^1], 0).Value)) units[^1] += unit;
            else Push(unit);
        }
        if (prefix.Length > 0) Push(""); return units.ToArray();
    }
}
