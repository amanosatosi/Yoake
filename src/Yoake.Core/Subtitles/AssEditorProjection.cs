using System.Text;

namespace Yoake.Core.Subtitles;

// One event's editing presentation. All application tools continue to use ASS
// source offsets; only the TextBox, clipboard and native caret use display offsets.
public sealed class AssEditorProjection
{
    private readonly int[] _displayToSource, _sourceToDisplay;
    public string Source { get; }
    public string Display { get; }
    public AssEditorProjection(string source)
    {
        Source = source;
        var breaks = new HashSet<int>();
        foreach (var token in AssSyntax.Tokenize(source))
        {
            if (token.Kind != AssSyntaxKind.Escape) continue;
            for (var i = token.Start; i < token.Start + token.Length;)
            {
                if (source[i + 1] == 'N') breaks.Add(i);
                i += source[i + 1] == '|' && i + 2 < source.Length && source[i + 2] == '\\' ? 3 : 2;
            }
        }
        var text = new StringBuilder(source.Length);
        List<int> offsets = [0];
        _sourceToDisplay = new int[source.Length + 1];
        for (var i = 0; i < source.Length;)
        {
            _sourceToDisplay[i] = text.Length;
            if (breaks.Contains(i))
            {
                _sourceToDisplay[i + 1] = text.Length;
                text.Append('\n'); i += 2;
            }
            else text.Append(source[i++]);
            offsets.Add(i);
        }
        _sourceToDisplay[source.Length] = text.Length;
        Display = text.ToString(); _displayToSource = offsets.ToArray();
    }
    public int ToSource(int display) => _displayToSource[Math.Clamp(display, 0, Display.Length)];
    public int ToDisplay(int source) => _sourceToDisplay[Math.Clamp(source, 0, Source.Length)];

    // Splice only the changed displayed range. Retained source, including
    // escaped literals and unknown extensions, is copied byte for byte.
    public string Edit(string display)
    {
        var prefix = 0;
        while (prefix < Display.Length && prefix < display.Length && Display[prefix] == display[prefix]) prefix++;
        var suffix = 0;
        while (suffix < Display.Length - prefix && suffix < display.Length - prefix && Display[^(suffix + 1)] == display[^(suffix + 1)]) suffix++;
        return Source[..ToSource(prefix)] + EncodeNewlines(display[prefix..(display.Length - suffix)]) + Source[ToSource(Display.Length - suffix)..];
    }
    public static string EncodeNewlines(string text)
    {
        var result = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is '\r' or '\n')
            {
                if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                result.Append(@"\N");
            }
            else result.Append(text[i]);
        }
        return result.ToString();
    }
}
