using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Yoake.Core.Subtitles;

namespace Yoake.UI.Controls;

// Small command surface shared by all four channels; checkerboard keeps exact
// transparency visible even for a fully transparent/black subtitle color.
public sealed class AssColorButton : UserControl
{
    public static readonly StyledProperty<AssColor?> ValueProperty = AvaloniaProperty.Register<AssColorButton,AssColor?>(nameof(Value));
    public static readonly StyledProperty<string> LabelProperty = AvaloniaProperty.Register<AssColorButton,string>(nameof(Label), "");
    public static readonly StyledProperty<ICommand?> CommandProperty = AvaloniaProperty.Register<AssColorButton,ICommand?>(nameof(Command));
    private readonly Border _swatch = new();
    private readonly TextBlock _unknown = new(){Text="?",FontSize=11,Foreground=Brushes.Black,FontWeight=FontWeight.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
    private readonly AssColorButtonGlyph _icon = new(){Width=24,Height=24};
    // Keep the 24-DIP artwork intact, with one DIP of border on each side.
    // Fluent's default button height is larger than the editor toolbar.
    private readonly Button _button = new(){Height=26,MinHeight=26,MinWidth=0,Padding=new Thickness(2,0),BorderThickness=new Thickness(1),VerticalContentAlignment=VerticalAlignment.Center};
    public AssColor? Value { get=>GetValue(ValueProperty); set=>SetValue(ValueProperty,value); }
    public string Label { get=>GetValue(LabelProperty); set=>SetValue(LabelProperty,value); }
    public ICommand? Command { get=>GetValue(CommandProperty); set=>SetValue(CommandProperty,value); }
    public AssColorButton()
    {
        Height=26;MinHeight=26;VerticalAlignment=VerticalAlignment.Center;
        var swatch = new Grid{Width=10,Height=18};
        swatch.Children.Add(new CheckerboardControl());swatch.Children.Add(_swatch);swatch.Children.Add(_unknown);
        _icon.Bind(AssColorButtonGlyph.AccentProperty,this.GetResourceObservable("VisualToolPrimaryBrush"));
        _icon.Bind(AssColorButtonGlyph.InkProperty,this.GetResourceObservable("IconForegroundBrush"));
        var panel = new StackPanel{Orientation=Orientation.Horizontal,Spacing=2};
        var swatchFrame=new Border { BorderThickness=new Thickness(1),Child=swatch,VerticalAlignment=VerticalAlignment.Center };
        swatchFrame.Bind(Border.BorderBrushProperty,this.GetResourceObservable("IconForegroundBrush"));
        panel.Children.Add(_icon);panel.Children.Add(swatchFrame);_button.Classes.Add("editorCompact");_button.Content=panel;Content=_button;Refresh();
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if(change.Property==ValueProperty||change.Property==LabelProperty||change.Property==CommandProperty)Refresh();
    }
    private void Refresh()
    {
        _swatch.Background=Value is {} color?new SolidColorBrush(Color.FromArgb(color.Opacity,color.Red,color.Green,color.Blue)):Brushes.Transparent;
        _unknown.IsVisible=Value is null;
        if(Value is null)ToolTip.SetTip(_button,"Renderer-only color expression. Mangetsu owns its effective color; choosing a color replaces the expression.");
        else _button.ClearValue(ToolTip.TipProperty);
        _icon.Channel=Label;
        _button.Command=Command;
    }
}
