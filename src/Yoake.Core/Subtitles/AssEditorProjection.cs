using System.Text;

namespace Yoake.Core.Subtitles;

// One event's editing presentation. All application tools continue to use ASS
// source offsets; only the TextBox, clipboard and native caret use display offsets.
public sealed class AssEditorProjection
{
    private const string MotionPlaceholder="{motion tracking}";
    private readonly record struct MotionSpan(int SourceStart,int SourceEnd,int DisplayStart,int DisplayEnd);
    private readonly int[] _displayToSource, _sourceToDisplay;
    private readonly List<MotionSpan> _motion=[];
    private readonly List<MotionBlock> _motionBlocks=[];
    public readonly record struct MotionBlock(int SourceStart,int SourceEnd,int DisplayStart,int DisplayEnd,bool Collapsed);
    public string Source { get; }
    public string Display { get; }
    public AssEditorProjection(string source,bool betterView=true,bool showMotionData=false,IReadOnlySet<int>? expandedMotionBlocks=null)
    {
        Source = source;
        var breaks = new HashSet<int>();
        foreach (var token in AssSyntax.Tokenize(source))
        {
            if(!betterView)break;
            if (token.Kind != AssSyntaxKind.Escape) continue;
            for (var i = token.Start; i < token.Start + token.Length;)
            {
                if (source[i + 1] == 'N') breaks.Add(i);
                i += source[i + 1] == '|' && i + 2 < source.Length && source[i + 2] == '\\' ? 3 : 2;
            }
        }
        var text = new StringBuilder(source.Length);
        var motionBlocks = betterView
            ? AssTextOperations.Blocks(source).Where(block=>block.Closed &&
                (source.AsSpan(block.Start,block.End-block.Start).StartsWith("{[yoake motion position]",StringComparison.Ordinal) ||
                 source.AsSpan(block.Start,block.End-block.Start).StartsWith("{[toshiban motion track helper]",StringComparison.Ordinal)))
                .ToDictionary(block=>block.Start,block=>block.End)
            : new Dictionary<int,int>();
        List<int> offsets = [0];
        _sourceToDisplay = new int[source.Length + 1];
        for (var i = 0; i < source.Length;)
        {
            _sourceToDisplay[i] = text.Length;
            if(motionBlocks.TryGetValue(i,out var rawEnd) && !showMotionData && expandedMotionBlocks?.Contains(i)!=true)
            {
                var begin=i;var displayStart=text.Length;var displayEnd=displayStart+MotionPlaceholder.Length;
                text.Append(MotionPlaceholder);
                for(var p=1;p<=MotionPlaceholder.Length;p++) offsets.Add(p<MotionPlaceholder.Length/2?begin:rawEnd);
                offsets[^1]=rawEnd;
                for(var p=begin+1;p<rawEnd;p++)_sourceToDisplay[p]=p-begin<(rawEnd-begin)/2?displayStart:displayEnd;
                _motion.Add(new(begin,rawEnd,displayStart,displayEnd));i=rawEnd;
                continue;
            }
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
        foreach(var block in motionBlocks)
            _motionBlocks.Add(new(block.Key,block.Value,_sourceToDisplay[block.Key],_sourceToDisplay[block.Value],
                !showMotionData && expandedMotionBlocks?.Contains(block.Key)!=true));
    }
    public MotionBlock? MotionAtDisplay(int position)
    {
        foreach(var block in _motionBlocks)if(position>=block.DisplayStart&&position<block.DisplayEnd)return block;
        return null;
    }
    public int ToSource(int display) => _displayToSource[Math.Clamp(display, 0, Display.Length)];
    public int ToDisplay(int source) => _sourceToDisplay[Math.Clamp(source, 0, Source.Length)];

    // Splice only the changed displayed range. Retained source, including
    // escaped literals and unknown extensions, is copied byte for byte.
    public string Edit(string display)
    {
        return TryEdit(display,out var source)?source:Source;
    }
    // A partial edit of a collapsed marker is rejected. The user can expose
    // the real block to edit it, or deliberately select and delete it whole.
    public bool TryEdit(string display,out string source)
    {
        var prefix = 0;
        while (prefix < Display.Length && prefix < display.Length && Display[prefix] == display[prefix]) prefix++;
        var suffix = 0;
        while (suffix < Display.Length - prefix && suffix < display.Length - prefix && Display[^(suffix + 1)] == display[^(suffix + 1)]) suffix++;
        var end=Display.Length-suffix;
        foreach(var block in _motion)
            if((prefix==end && prefix>block.DisplayStart&&prefix<block.DisplayEnd) ||
               (prefix<block.DisplayEnd&&end>block.DisplayStart&&(prefix>block.DisplayStart||end<block.DisplayEnd)))
            {source=Source;return false;}
        source=Source[..ToSource(prefix)] + EncodeNewlines(display[prefix..(display.Length - suffix)]) + Source[ToSource(end)..];
        return true;
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
