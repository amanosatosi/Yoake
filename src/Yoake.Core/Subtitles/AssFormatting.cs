using System.Globalization;
using System.Text;

namespace Yoake.Core.Subtitles;

public sealed record AssTextEdit(string Text, int SelectionStart, int SelectionEnd);
public static class AssFormatting
{
    private static readonly Dictionary<string, string> StyleFields = new(StringComparer.Ordinal)
    { ["b"]="Bold",["i"]="Italic",["u"]="Underline",["s"]="StrikeOut",["fn"]="Fontname",["fs"]="Fontsize",
      ["1c"]="PrimaryColour",["2c"]="SecondaryColour",["3c"]="OutlineColour",["4c"]="BackColour" };

    public static Dictionary<string,string> State(string text, int position, AssStyle style, Func<string,AssStyle?>? resolve = null)
    {
        var state = Defaults(style);
        foreach (var tag in AssSyntax.Tags(text))
        {
            if (tag.End > position) break;
            var value = text[tag.ValueStart..tag.End].Trim(); var name = tag.Name == "c" ? "1c" : tag.Name;
            if (name == "r") { state = Defaults(resolve?.Invoke(value) ?? style); continue; }
            if (name == "alpha") { foreach (var a in new[]{"1a","2a","3a","4a"}) state[a] = value; }
            else if (state.ContainsKey(name) && value.Length > 0) state[name] = value;
        }
        return state;
    }
    private static Dictionary<string,string> Defaults(AssStyle style)
    {
        var state = new Dictionary<string,string>(StringComparer.Ordinal);
        foreach (var field in StyleFields)
        {
            var value = style.Get(field.Value);
            if (field.Key is "b" or "i" or "u" or "s") value = value == "0" || value.Length == 0 ? "0" : "1";
            if (field.Key.EndsWith('c') && AssColor.TryParse(value, out var color))
            { state[field.Key] = color.RgbOverride; state[field.Key[..1] + "a"] = color.AlphaOverride; }
            else state[field.Key] = value;
        }
        return state;
    }
    public static AssTextEdit Toggle(string text, int start, int end, string tag, AssStyle style, Func<string,AssStyle?>? resolve = null)
    {
        var boundary = Boundary(text, Math.Min(start,end), false);
        var state = State(text,boundary,style,resolve);
        var enabled = !int.TryParse(state.GetValueOrDefault(tag), out var value) || value != 0;
        return Apply(text,start,end,new Dictionary<string,string>{{tag,enabled?"0":"1"}},style,resolve);
    }
    public static AssTextEdit Apply(string text, int start, int end, IReadOnlyDictionary<string,string> values, AssStyle style, Func<string,AssStyle?>? resolve = null)
    {
        var hasSelection = start != end;
        var left = Boundary(text, Math.Min(start,end), false);
        var right = hasSelection ? Boundary(text,Math.Max(start,end),true) : left;
        var restore = State(text,right,style,resolve);
        var prefix = Block(values); var suffix = hasSelection ? Block(values.Keys.ToDictionary(k=>k,k=>restore.GetValueOrDefault(k,""))) : "";
        var selected = text[left..right];
        // Existing static overrides in the selection must not cancel the chosen
        // formatting. Splice only these parameters; unknown tags stay byte exact.
        foreach (var tag in AssSyntax.Tags(selected).Reverse())
        {
            var name = tag.Name == "c" ? "1c" : tag.Name;
            if (values.TryGetValue(name,out var value)) selected = selected[..tag.ValueStart] + value + selected[tag.End..];
            else if (tag.Name == "r") selected = selected[..tag.End] + string.Concat(values.Select(p=>"\\"+p.Key+p.Value)) + selected[tag.End..];
        }
        var result = text[..left] + prefix + selected + suffix + text[right..];
        return new(result,left+prefix.Length,left+prefix.Length+selected.Length);
    }
    public static AssTextEdit Reset(string text, int start, int end, AssStyle style, Func<string,AssStyle?>? resolve = null)
    {
        var left=Boundary(text,Math.Min(start,end),false); var right=start==end?left:Boundary(text,Math.Max(start,end),true);
        var suffix=start==end?"":Block(State(text,right,style,resolve));
        return new(text[..left]+"{\\r}"+text[left..right]+suffix+text[right..],left+4,left+4+right-left);
    }
    private static string Block(IEnumerable<KeyValuePair<string,string>> values) => "{"+string.Concat(values.Select(p=>"\\"+p.Key+p.Value))+"}";
    public static int Boundary(string text, int position, bool forward)
    {
        position=Math.Clamp(position,0,text.Length);
        var boundaries=StringInfo.ParseCombiningCharacters(text);
        if(position<text.Length && !boundaries.Contains(position)) position=forward?boundaries.FirstOrDefault(i=>i>position,text.Length):boundaries.LastOrDefault(i=>i<position);
        foreach(var tag in AssSyntax.Tags(text)) if(position>tag.BlockStart && position<=tag.BlockEnd) return forward?Math.Min(text.Length,tag.BlockEnd+1):tag.BlockStart;
        if(position>0&&position<text.Length&&text[position-1]=='\\'&&text[position] is 'N' or 'n' or 'h') position+=forward?1:-1;
        return position;
    }
}
