namespace Yoake.Core.Subtitles;

// One logical draft across script and library selections. Validate/commit before
// changing targets, so a rejected edit stays editable on its original style.
public sealed class StyleEditSession
{
    public SubtitleEditor? Editor { get; private set; }
    public AssStyle? Style { get; private set; }
    public StyleDraft? Draft { get; private set; }

    public bool Commit()
    {
        if (Draft is null || Editor is null || !Draft.IsChanged) return false;
        Draft.Apply(Editor);
        Draft = new(Style!);
        return true;
    }

    public void Select(SubtitleEditor? editor, AssStyle? style)
    {
        if (style is not null && editor?.Document.Styles.Contains(style) != true)
            throw new ArgumentException("Style must belong to the selected document.");
        Commit();
        Editor = editor;
        Style = style;
        Reload();
    }

    public void Reload() => Draft = Style is not null && Editor?.Document.Styles.Contains(Style) == true ? new(Style) : null;
}
