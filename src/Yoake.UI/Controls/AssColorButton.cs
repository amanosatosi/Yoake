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
    private readonly TextBlock _unknown = new(){Text="?",Foreground=Brushes.Black,FontWeight=FontWeight.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
    private readonly TextBlock _label = new(){FontSize=10,VerticalAlignment=VerticalAlignment.Center};
    private readonly Button _button = new(){Padding=new Thickness(4,2),MinHeight=26};
    public AssColor? Value { get=>GetValue(ValueProperty); set=>SetValue(ValueProperty,value); }
    public string Label { get=>GetValue(LabelProperty); set=>SetValue(LabelProperty,value); }
    public ICommand? Command { get=>GetValue(CommandProperty); set=>SetValue(CommandProperty,value); }
    public AssColorButton()
    {
        var swatch = new Grid{Width=20,Height=16};
        swatch.Children.Add(new CheckerboardControl());swatch.Children.Add(_swatch);swatch.Children.Add(_unknown);
        var panel = new StackPanel{Orientation=Orientation.Horizontal,Spacing=3};
        panel.Children.Add(swatch);panel.Children.Add(_label);_button.Content=panel;Content=_button;Refresh();
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
        _label.Text=Label;_button.Command=Command;
    }
}
