using Avalonia;
using Avalonia.Controls;
using Yoake.Core.Subtitles;

namespace Yoake.UI.Controls;

// Only realized rows create a projection; changing modes does not reparse a
// whole document, alter record fields or invalidate the renderer.
public sealed class SubtitleGridText : TextBlock
{
    public static readonly StyledProperty<string?> SourceTextProperty=AvaloniaProperty.Register<SubtitleGridText,string?>(nameof(SourceText));
    public static readonly AttachedProperty<GridTagDisplayMode> TagDisplayModeProperty=AvaloniaProperty.RegisterAttached<SubtitleGridText,Control,GridTagDisplayMode>(nameof(TagDisplayMode),GridTagDisplayMode.Simplified,inherits:true);
    public static GridTagDisplayMode GetTagDisplayMode(AvaloniaObject control)=>control.GetValue(TagDisplayModeProperty);
    public static void SetTagDisplayMode(AvaloniaObject control,GridTagDisplayMode value)=>control.SetValue(TagDisplayModeProperty,value);
    public string? SourceText {get=>GetValue(SourceTextProperty);set=>SetValue(SourceTextProperty,value);}
    public GridTagDisplayMode TagDisplayMode {get=>GetValue(TagDisplayModeProperty);set=>SetValue(TagDisplayModeProperty,value);}
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if(change.Property==SourceTextProperty||change.Property==TagDisplayModeProperty)Text=AssGridText.Project(SourceText??"",TagDisplayMode);
    }
}
