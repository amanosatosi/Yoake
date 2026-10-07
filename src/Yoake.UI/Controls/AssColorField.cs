using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Yoake.Core.Subtitles;

namespace Yoake.UI.Controls;

public sealed class AssColorField : UserControl
{
    public static readonly StyledProperty<string> ValueProperty=AvaloniaProperty.Register<AssColorField,string>(nameof(Value),"&H00FFFFFF",defaultBindingMode:BindingMode.TwoWay);
    private readonly Border _swatch=new(){Width=24,Height=16,BorderBrush=Brushes.Gray,BorderThickness=new Thickness(1)};
    private readonly TextBox _exact=new(){MinWidth=106,Padding=new Thickness(4,1),FontSize=11};
    private bool _sync;
    public string Value {get=>GetValue(ValueProperty);set=>SetValue(ValueProperty,value);}
    public event EventHandler? ValueChanged;
    public event EventHandler? ValueCommitted;
    public AssColorField()
    {
        var swatch=new Grid{Width=24,Height=16};swatch.Children.Add(new CheckerboardControl());swatch.Children.Add(_swatch);
        var grid=new Grid{ColumnDefinitions=new("Auto,*"),ColumnSpacing=3};var button=new Button{Content=swatch,Padding=new Thickness(3,2),MinHeight=26};
        ToolTip.SetTip(button,"Choose RGB color and alpha");grid.Children.Add(button);Grid.SetColumn(_exact,1);grid.Children.Add(_exact);Content=grid;
        _exact.TextChanged+=(_,_)=>{if(!_sync){SetCurrentValue(ValueProperty,_exact.Text??"");ValueChanged?.Invoke(this,EventArgs.Empty);}};
        button.Click+=async (_,_)=>
        {
            if(TopLevel.GetTopLevel(this) is not Window owner)return;
            try
            {
                var initial=AssColor.TryParse(Value,out var parsed)?parsed:new AssColor(255,255,255,0);
                var result=await new AssColorDialog(initial).ShowDialog<AssColor?>(owner);
                if(result is {} color){SetCurrentValue(ValueProperty,color.StyleValue);ValueChanged?.Invoke(this,EventArgs.Empty);ValueCommitted?.Invoke(this,EventArgs.Empty);}
            }
            catch(Exception exception){ToolTip.SetTip(_exact,exception.Message);}
        };
        Refresh();
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change){base.OnPropertyChanged(change);if(change.Property==ValueProperty)Refresh();}
    private void Refresh()
    {
        _sync=true;_exact.Text=Value;_sync=false;
        if(AssColor.TryParse(Value,out var color)){_swatch.Background=new SolidColorBrush(Color.FromArgb(color.Opacity,color.Red,color.Green,color.Blue));ToolTip.SetTip(_exact,"ASS &HAABBGGRR; alpha 00 is opaque, FF is transparent");}
        else {_swatch.Background=Brushes.Transparent;ToolTip.SetTip(_exact,"Keep the exact source value, or choose a valid ASS color.");}
    }
}

public sealed class AssColorDialog : Window
{
    private AssColor _color;
    private readonly Slider[] _sliders=new Slider[4];
    private readonly NumericUpDown[] _numbers=new NumericUpDown[4];
    private readonly TextBox _exact=new(){Padding=new Thickness(5,2)};
    private readonly TextBox _html=new(){Padding=new Thickness(5,2)};
    private readonly TextBlock _alpha=new(){FontSize=11};
    private readonly Border _preview=new(){Height=45};
    private readonly TextBlock _error=new(){FontSize=11};
    private bool _sync;
    public AssColorDialog(AssColor color)
    {
        _color=color;Title="Subtitle color";Width=410;SizeToContent=SizeToContent.Height;CanResize=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new StackPanel{Margin=new Thickness(12),Spacing=6};Content=root;
        var checker=new CheckerboardControl{Height=45};var preview=new Grid{Height=45};preview.Children.Add(checker);preview.Children.Add(_preview);root.Children.Add(preview);
        var palette=new WrapPanel();foreach(var rgb in new uint[]{0xFFFFFF,0xC0C0C0,0x808080,0x000000,0xFF0000,0xFF8000,0xFFFF00,0x80FF00,0x00FF00,0x00FFFF,0x0080FF,0x0000FF,0x8000FF,0xFF00FF,0xFF80C0,0x804000})
        {
            var c=Color.FromRgb((byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb);var b=new Button{Width=22,Height=22,Padding=new Thickness(1),Margin=new Thickness(1),Background=new SolidColorBrush(c)};
            ToolTip.SetTip(b,$"RGB #{rgb:X6}");b.Click+=(_,_)=>{_color=new(c.R,c.G,c.B,_color.Transparency);Refresh();};palette.Children.Add(b);
        }
        root.Children.Add(palette);
        var labels=new[]{"Red","Green","Blue","Opacity"};var colors=new[]{Colors.Red,Colors.Lime,Colors.Blue,Colors.White};
        for(var i=0;i<4;i++)
        {
            var channel=i;var row=new Grid{ColumnDefinitions=new("55,*,86"),ColumnSpacing=5};row.Children.Add(new TextBlock{Text=labels[i],VerticalAlignment=VerticalAlignment.Center});
            var slider=new Slider{Minimum=0,Maximum=255,TickFrequency=1,IsSnapToTickEnabled=true};_sliders[i]=slider;Grid.SetColumn(slider,1);row.Children.Add(slider);
            slider.Background=new LinearGradientBrush{StartPoint=new RelativePoint(0,0,RelativeUnit.Relative),EndPoint=new RelativePoint(1,0,RelativeUnit.Relative),GradientStops=[new GradientStop(Colors.Black,0),new GradientStop(colors[i],1)]};
            var number=new NumericUpDown{Minimum=0,Maximum=255,Increment=1,FormatString="0",Padding=new Thickness(3,1)};_numbers[i]=number;Grid.SetColumn(number,2);row.Children.Add(number);root.Children.Add(row);
            slider.PropertyChanged+=(_,e)=>{if(e.Property==Slider.ValueProperty&&!_sync)Change(channel,(byte)Math.Round(slider.Value));};
            number.ValueChanged+=(_,_)=>{if(!_sync&&number.Value is {} value)Change(channel,(byte)value);};
        }
        root.Children.Add(_alpha);
        root.Children.Add(new TextBlock{Text="ASS: &HAABBGGRR · 00 opaque / FF transparent",FontSize=11});root.Children.Add(_exact);
        root.Children.Add(new TextBlock{Text="RGB: #RRGGBB · alpha is kept separately",FontSize=11});root.Children.Add(_html);root.Children.Add(_error);
        _exact.TextChanged+=(_,_)=>{if(_sync)return;if(AssColor.TryParse(_exact.Text,out var parsed)){_color=parsed;Refresh(false);}else _error.Text="Enter a valid ASS hex or signed decimal color.";};
        _html.TextChanged+=(_,_)=>
        {
            if(_sync)return;var hex=(_html.Text??"").Trim().TrimStart('#');
            if(hex.Length==6&&uint.TryParse(hex,System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture,out var rgb))
            {_color=_color with{Red=(byte)(rgb>>16),Green=(byte)(rgb>>8),Blue=(byte)rgb};Refresh(updateHtml:false);}
            else _error.Text="Enter RGB #RRGGBB; ASS alpha is preserved.";
        };
        var bar=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6,HorizontalAlignment=HorizontalAlignment.Right};
        var copy=new Button{Content="Copy exact"};copy.Click+=async (_,_)=>{try{if(Clipboard is not null){var transfer=new DataTransfer();transfer.Add(DataTransferItem.CreateText(_exact.Text??""));await Clipboard.SetDataAsync(transfer);await Clipboard.FlushAsync();}}catch(Exception e){_error.Text=e.Message;}};
        var paste=new Button{Content="Paste"};paste.Click+=async (_,_)=>{try{if(Clipboard is not null){using var data=await Clipboard.TryGetDataAsync();if(data is not null)_exact.Text=await data.TryGetTextAsync();}}catch(Exception e){_error.Text=e.Message;}};
        var ok=new Button{Content="Use color",IsDefault=true};ok.Click+=(_,_)=>{if(AssColor.TryParse(_exact.Text,out var parsed))Close((AssColor?)parsed);};
        var cancel=new Button{Content="Cancel",IsCancel=true};cancel.Click+=(_,_)=>Close();bar.Children.Add(copy);bar.Children.Add(paste);bar.Children.Add(cancel);bar.Children.Add(ok);root.Children.Add(bar);Refresh();
    }
    private void Change(int channel,byte value){_color=channel switch{0=>_color with{Red=value},1=>_color with{Green=value},2=>_color with{Blue=value},_=>_color with{Transparency=(byte)(255-value)}};Refresh();}
    private void Refresh(bool updateExact=true,bool updateHtml=true)
    {
        _sync=true;var values=new[]{_color.Red,_color.Green,_color.Blue,_color.Opacity};for(var i=0;i<4;i++){_sliders[i].Value=values[i];_numbers[i].Value=values[i];}
        if(updateExact)_exact.Text=_color.StyleValue;
        if(updateHtml)_html.Text=$"#{_color.Red:X2}{_color.Green:X2}{_color.Blue:X2}";
        _alpha.Text=$"Opacity {_color.Opacity}/255 · ASS transparency {_color.Transparency:X2}";
        _error.Text="";_preview.Background=new SolidColorBrush(Color.FromArgb(_color.Opacity,_color.Red,_color.Green,_color.Blue));_sync=false;
    }
}

public sealed class CheckerboardControl : Control
{
    private static readonly IBrush Light=new SolidColorBrush(Color.FromRgb(184,184,184)),Dark=new SolidColorBrush(Color.FromRgb(132,132,132));
    public override void Render(DrawingContext context)
    {
        for(var y=0;y<Bounds.Height;y+=10)for(var x=0;x<Bounds.Width;x+=10)context.FillRectangle((x/10+y/10)%2==0?Light:Dark,new Rect(x,y,Math.Min(10,Bounds.Width-x),Math.Min(10,Bounds.Height-y)));
    }
}
