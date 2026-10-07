using System.Globalization;

namespace Yoake.Core.Subtitles;

public enum AssSyntaxKind { Text, Brace, Tag, MangetsuTag, Karaoke, Parameter, Number, Color, Escape, Separator, Drawing, Unknown, Furigana }
public readonly record struct AssSyntaxToken(int Start, int Length, AssSyntaxKind Kind);
public readonly record struct AssSyntaxTagSpan(int Start, int End, string Name, int ValueStart, int BlockStart, int BlockEnd);

// Presentation catalog, not a validation whitelist. Unknown names always survive.
// Update alongside libassmod/mangetsu's ass_parse.c. Families are explicit names,
// so a future tag sharing a short standard prefix is not mislabeled as that tag.
public static class AssTagCatalog
{
    private const string Standard = "b i u s fn fs fscx fscy fsp fr frx fry frz fax fay fe r a an pos move org fad fade bord xbord ybord shad xshad yshad blur be c 1c 2c 3c 4c alpha 1a 2a 3a 4a clip iclip t p pbo q";
    private const string Karaoke = "k K kf ko kO kt";
    private const string Mangetsu = "readmark readtime vert vtype vdir vcolsp vsp ctan wtan ctx cty ct colsp colan col rnds rndx rndy rndz rnd distort xblur yblur scale fsc furiplaceauto furichangepos furipos furifsp furistyle furisx furisy furis furiap furi fsvp fshp movevc mover moves3 moves4 frs z ortho tan ta jitter0 blend clippos clips img 1img 2img 3img 4img vc 1vc 2vc 3vc 4vc va 1va 2va 3va 4va 5c 5a msg msgleft msgright msgtitle msgm msgshowname msgstartcount msgtime msganim msgtitlegbc msgtitlec bubbc bubba bubbs bubc buba bc ba bs bbs bbc bba boxpx boxpy boxp boxr box grd 1grd 2grd 3grd 4grd 5grd 1gra 2gra 3gra 4gra 5gra pgrd 1pgrd cyc polc pols scroll scrollsl scrollt 3sc 3svc 3sgrd";
    private static readonly Dictionary<string, AssSyntaxKind> Entries = Build();
    private static readonly string[] Names = Entries.Keys.OrderByDescending(n => n.Length).ToArray();
    private static Dictionary<string, AssSyntaxKind> Build()
    {
        var entries = new Dictionary<string, AssSyntaxKind>(StringComparer.Ordinal);
        foreach (var name in Standard.Split(' ')) entries[name] = AssSyntaxKind.Tag;
        foreach (var name in Mangetsu.Split(' ')) entries[name] = AssSyntaxKind.MangetsuTag;
        foreach (var name in Karaoke.Split(' ')) entries[name] = AssSyntaxKind.Karaoke;
        return entries;
    }
    public static (int Length, AssSyntaxKind Kind) Match(ReadOnlySpan<char> source)
    {
        foreach (var name in Names)
            if (source.StartsWith(name, StringComparison.Ordinal) &&
                (source.Length == name.Length || !char.IsLetter(source[name.Length]) || name is "fn" or "r"))
                return (name.Length, Entries[name]);
        var length = 0;
        while (length < source.Length && (char.IsLetterOrDigit(source[length]) || source[length] == '_')) length++;
        return (length, AssSyntaxKind.Unknown);
    }
}

public sealed class AssSyntaxDocument
{
    private string? _source;
    private IReadOnlyList<AssSyntaxToken> _tokens = [];
    // A cached, allocation-bounded linear scan of one event; layout/selection
    // changes do not re-tokenize. No regex engine or document-wide reparsing.
    public IReadOnlyList<AssSyntaxToken> Update(string source)
    {
        if (_source == source) return _tokens;
        _source = source; return _tokens = AssSyntax.Tokenize(source);
    }
}

public static class AssSyntax
{
    public static IReadOnlyList<AssSyntaxToken> Tokenize(string source)
    {
        List<AssSyntaxToken> tokens = [];
        var block = false; var depth = 0; var drawing = false; var furi = true; var vertical = false;
        var tag = ""; var furiganaEnd = -1; var literalEnd = -1; int[] pipes = [];
        void Add(int start, int length, AssSyntaxKind kind, bool coalesce = true)
        {
            if (length == 0) return;
            if (coalesce && tokens.Count > 0 && tokens[^1].Kind == kind && tokens[^1].Start + tokens[^1].Length == start)
                tokens[^1] = tokens[^1] with { Length = tokens[^1].Length + length };
            else tokens.Add(new(start, length, kind));
        }
        for (var i = 0; i < source.Length;)
        {
            var c = source[i];
            if (!block && c == '\\' && i + 1 < source.Length && (source[i + 1] is 'N' or 'n' or 'h' or '{' or '}' || furi && source[i + 1] is '<' or '>' or '|' or '\\'))
            {
                var length = source[i + 1] == '|' && i + 2 < source.Length && source[i + 2] == '\\' ? 3 : 2;
                Add(i, length, AssSyntaxKind.Escape); i += length; continue;
            }
            if (!block && c == '{') { Add(i++, 1, AssSyntaxKind.Brace); block = true; depth = 0; tag = ""; continue; }
            if (block && c == '}') { Add(i++, 1, AssSyntaxKind.Brace); block = false; continue; }
            if (block && c == '\\')
            {
                var match = AssTagCatalog.Match(source.AsSpan(i + 1));
                tag = source.Substring(i + 1, match.Length);
                Add(i, match.Length + 1, match.Kind, false); i += match.Length + 1;
                if (depth == 0)
                {
                    var end = i; while (end < source.Length && source[end] is not '\\' and not '}') end++;
                    var value = source.AsSpan(i, end - i).Trim();
                    if (tag == "p" && int.TryParse(value, out var scale)) drawing = scale > 0;
                    if (tag == "r") { drawing = false; furi = true; vertical = false; }
                    if (tag == "furi" && int.TryParse(value, out var enabled)) furi = enabled != 0;
                    if (tag == "vert" && int.TryParse(value, out var mode)) vertical = mode != 0;
                }
                continue;
            }
            if (block && c is '(' or ')' or ',' or ';')
            {
                if (c == '(') depth++; else if (c == ')') depth = Math.Max(0, depth - 1);
                Add(i++, 1, AssSyntaxKind.Separator); continue;
            }
            if (block && c == '&' && i + 1 < source.Length && source[i + 1] is 'H' or 'h')
            {
                var start = i; i += 2; while (i < source.Length && char.IsAsciiHexDigit(source[i])) i++;
                if (i < source.Length && source[i] == '&') i++;
                Add(start, i - start, AssSyntaxKind.Color); continue;
            }
            if (block && c == '$')
            {
                var start = i++; while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) i++;
                Add(start, i - start, AssSyntaxKind.Color); continue;
            }
            if ((block || drawing) && (char.IsAsciiDigit(c) || c is '+' or '-' or '.' && i + 1 < source.Length && char.IsAsciiDigit(source[i + 1])))
            {
                var start = i++; while (i < source.Length && (char.IsAsciiDigit(source[i]) || source[i] == '.')) i++;
                Add(start, i - start, tag is "k" or "K" or "kf" or "ko" or "kO" or "kt" ? AssSyntaxKind.Karaoke : AssSyntaxKind.Number); continue;
            }
            if (!block && !drawing && furi && !vertical && c == '<' && i >= literalEnd)
            {
                var candidate = Furigana(source, i);
                if (candidate.End > i) literalEnd = candidate.End;
                if (candidate.Valid) { furiganaEnd = candidate.End; pipes = candidate.Pipes; }
            }
            var kind = block ? AssSyntaxKind.Parameter : AssSyntaxKind.Text;
            if (drawing || block && tag is "clip" or "iclip" && depth > 0 && "mnlbspc".Contains(c)) kind = AssSyntaxKind.Drawing;
            if (!block && i < furiganaEnd && (c is '<' or '>' || pipes.Contains(i) || pipes.Length > 0 && i > pipes[0])) kind = AssSyntaxKind.Furigana;
            Add(i++, 1, kind);
        }
        return tokens;
    }

    private static (bool Valid, int End, int[] Pipes) Furigana(string source, int start)
    {
        List<int> pipes = []; var malformed = false;
        for (var i = start + 1; i < source.Length; i++)
        {
            var c = source[i];
            if (c == '\\' && i + 1 < source.Length)
            {
                if (source[i + 1] == 'N') malformed = true;
                if (source[i + 1] == '|' && i + 2 < source.Length && source[i + 2] == '\\') { i += 2; continue; }
                if (source[i + 1] is '<' or '>' or '|' or '\\' or '{' or '}') { i++; continue; }
            }
            if (c == '{') { var close = source.IndexOf('}', i + 1); if (close < 0) return default; i = close; continue; }
            if (c is '<' or '}' or '\n' or '\r') malformed = true;
            if (c == '|') pipes.Add(i);
            if (c == '>')
                return (!malformed && pipes.Count is 1 or 2 && HasText(source, start + 1, pipes[0]) &&
                    (HasText(source, pipes[0] + 1, pipes.Count == 2 ? pipes[1] : i) || pipes.Count == 2 && HasText(source, pipes[1] + 1, i)), i + 1, pipes.ToArray());
        }
        return default;
    }
    private static bool HasText(string source, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (source[i] == '{') { var close = source.IndexOf('}', i + 1); if (close < end && close >= 0) { i = close; continue; } }
            if (!char.IsWhiteSpace(source[i])) return true;
        }
        return false;
    }

    // Top-level tags only: a tag inside a transform is not the current static
    // state. Parameter parentheses are balanced, never split on backslashes.
    public static IEnumerable<AssSyntaxTagSpan> Tags(string source)
    {
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] == '\\' && i + 1 < source.Length && source[i + 1] is '{' or '}') { i++; continue; }
            if (source[i] != '{') continue;
            var blockStart = i; var close = source.IndexOf('}', i + 1); if (close < 0) close = source.Length;
            for (i++; i < close;)
            {
                if (source[i] != '\\') { i++; continue; }
                var start = i++; var name = AssTagCatalog.Match(source.AsSpan(i));
                var tag = source.Substring(i, name.Length); i += name.Length; var valueStart = i; var depth = 0;
                while (i < close)
                {
                    if (source[i] == '(') depth++; else if (source[i] == ')') depth = Math.Max(0, depth - 1);
                    if (source[i] == '\\' && depth == 0) break;
                    i++;
                }
                yield return new(start, i, tag, valueStart, blockStart, close);
            }
            i = close;
        }
    }
}
