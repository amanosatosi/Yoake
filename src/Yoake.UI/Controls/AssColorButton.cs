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
    public static readonly StyledProperty<AssColor> ValueProperty = AvaloniaProperty.Register<AssColorButton,AssColor>(nameof(Value));
    public static readonly StyledProperty<string> LabelProperty = AvaloniaProperty.Register<AssColorButton,string>(nameof(Label), "");
    public static readonly StyledProperty<ICommand?> CommandProperty = AvaloniaProperty.Register<AssColorButton,ICommand?>(nameof(Command));
    private readonly Border _swatch = new();
    private readonly TextBlock _label = new(){FontSize=10,VerticalAlignment=VerticalAlignment.Center};
    private readonly Button _button = new(){Padding=new Thickness(4,2),MinHeight=26};
    public AssColor Value { get=>GetValue(ValueProperty); set=>SetValue(ValueProperty,value); }
    public string Label { get=>GetValue(LabelProperty); set=>SetValue(LabelProperty,value); }
    public ICommand? Command { get=>GetValue(CommandProperty); set=>SetValue(CommandProperty,value); }
    public AssColorButton()
    {
        var swatch = new Grid{Width=20,Height=16};
        swatch.Children.Add(new CheckerboardControl());swatch.Children.Add(_swatch);
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
        _swatch.Background=new SolidColorBrush(Color.FromArgb(Value.Opacity,Value.Red,Value.Green,Value.Blue));
        _label.Text=Label;_button.Command=Command;
    }
}
