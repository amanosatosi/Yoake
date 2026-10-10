namespace Yoake.Core.Subtitles;

// Static authoring state, not a second renderer. Animated or mixed selection
// flags are indeterminate; Mangetsu remains the authority for rendered results.
public sealed record AssFormattingState(bool? Bold, bool? Italic, bool? Underline, bool? Strikeout,
    AssColor? Primary, AssColor? Secondary, AssColor? Outline, AssColor? Shadow)
{
    public static AssFormattingState Empty { get; } = new(false, false, false, false,
        new(255,255,255,0), new(255,0,0,0), new(0,0,0,0), new(0,0,0,0));

    public static AssFormattingState Read(string text, int start, int end, AssStyle style, Func<string,AssStyle?>? resolve = null)
    {
        var left = AssFormatting.Boundary(text, Math.Min(start,end), start == end);
        var right = start == end ? left : AssFormatting.Boundary(text, Math.Max(start,end), true);
        var state = AssFormatting.State(text,left,style,resolve);
        var uncertain = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in AssSyntax.Tags(text))
        {
            if (tag.Start >= right) break;
            if (tag.Name == "t" || tag.Start >= left && tag.Name == "r")
                uncertain.UnionWith(new[]{"b","i","u","s"});
            else if (tag.Start >= left) uncertain.Add(tag.Name);
        }
        bool? Flag(string tag) => uncertain.Contains(tag) ? null : state.GetValueOrDefault(tag) switch { "0" => false, "1" or "-1" => true, _ => null };
        AssColor? Color(int channel)
        {
            if(!AssColor.TryParse(state.GetValueOrDefault(channel+"c"),out var rgb)||
                !AssColor.TryParse(state.GetValueOrDefault(channel+"a"),out var alpha))return null;
            return rgb with { Transparency = alpha.Red };
        }
        return new(Flag("b"),Flag("i"),Flag("u"),Flag("s"),Color(1),Color(2),Color(3),Color(4));
    }
}
