using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Yoake.Core.Commands;
using Yoake.Core.Logging;
using Yoake.Core.Subtitles;
using Yoake.Native;
using Yoake.UI.Commands;

namespace Yoake.UI.Controls;

public sealed class AssColorDialog : Window
{
    private AssColor _color;
    private readonly ColorSpectrum _spectrum=new(){Name="ColorSpectrum"};
    private readonly Slider _hue=new(){Name="ColorHue",Minimum=0,Maximum=359.99,Orientation=Orientation.Vertical,Width=24};
    private readonly Slider _alpha=new(){Name="ColorTransparency",Minimum=0,Maximum=255,Orientation=Orientation.Vertical,Width=24};
    private readonly NumericUpDown[][] _channels=[new NumericUpDown[3],new NumericUpDown[3],new NumericUpDown[3]];
    private readonly NumericUpDown _alphaNumber=new(){Minimum=0,Maximum=255,Increment=1,Width=92,FormatString="0",Padding=new Thickness(3,1)};
    private readonly TextBox _exact=new(){Name="AssHex",Padding=new Thickness(4,1)};
    private readonly TextBox _html=new(){Name="HtmlHex",Padding=new Thickness(4,1)};
    private readonly Border _preview=new();
    private readonly TextBlock _error=new(){TextWrapping=TextWrapping.Wrap,FontSize=11};
    private readonly RecentColorStore _recent;
    private readonly CommandRegistry _commands=new(new FileAppLog(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Yoake","authoring.log")));
    private readonly List<Window> _droppers=[];
    private bool _sync,_valid=true;
    public AssColor SelectedColor=>_color;
    public AssColorDialog(AssColor color,string? historyPath=null)
    {
        _color=color;_recent=new(historyPath??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Yoake","recent-colors.txt"));
        Title="Subtitle color";Width=690;SizeToContent=SizeToContent.Height;CanResize=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;FontSize=12;
        var root=new Grid{Margin=new Thickness(10),ColumnDefinitions=new("316,*"),RowDefinitions=new("Auto,Auto,Auto"),ColumnSpacing=12,RowSpacing=8};Content=root;
        var left=new StackPanel{Spacing=6};root.Children.Add(left);
        left.Children.Add(new TextBlock{Text="Color spectrum · HSV/H",FontWeight=FontWeight.SemiBold});
        var spectra=new Grid{ColumnDefinitions=new("*,24,24"),ColumnSpacing=4,Height=256};spectra.Children.Add(_spectrum);Grid.SetColumn(_hue,1);spectra.Children.Add(_hue);var alphaStrip=new Grid();alphaStrip.Children.Add(new CheckerboardControl());alphaStrip.Children.Add(_alpha);Grid.SetColumn(alphaStrip,2);spectra.Children.Add(alphaStrip);left.Children.Add(spectra);
        var hueStops=new GradientStops();for(var h=0;h<=360;h+=60){var c=ColorSpace.FromHsv(h,1,1);hueStops.Add(new(Color.FromRgb(c.Red,c.Green,c.Blue),h/360d));}
        _hue.Background=new LinearGradientBrush{StartPoint=new(0,0,RelativeUnit.Relative),EndPoint=new(0,1,RelativeUnit.Relative),GradientStops=hueStops};
        ToolTip.SetTip(_hue,"Hue (degrees)");ToolTip.SetTip(_alpha,"ASS transparency: 00 opaque, FF transparent");
        var previews=new Grid{ColumnDefinitions=new("*,*"),Height=40};var original=Preview(color);previews.Children.Add(original);var current=new Grid();current.Children.Add(new CheckerboardControl());current.Children.Add(_preview);Grid.SetColumn(current,1);previews.Children.Add(current);left.Children.Add(previews);
        left.Children.Add(new TextBlock{Text="Original                                      Current",FontSize=11});
        var right=new StackPanel{Spacing=5};Grid.SetColumn(right,1);root.Children.Add(right);
        for(var space=0;space<3;space++)
        {
            var index=space;right.Children.Add(new TextBlock{Text=new[]{"RGB · 0–255","HSV · degrees / % / %","HSL · degrees / % / %"}[space],FontWeight=FontWeight.SemiBold});
            var row=new UniformGrid{Columns=3};right.Children.Add(row);
            for(var channel=0;channel<3;channel++)
            {
                var box=new StackPanel{Spacing=2,Margin=new Thickness(0,0,4,0)};box.Children.Add(new TextBlock{Text=(space==0?new[]{"R","G","B"}:space==1?new[]{"H","S","V"}:new[]{"H","S","L"})[channel],FontSize=11});
                var input=new NumericUpDown{Minimum=0,Maximum=space==0?255:channel==0?360:100,Increment=1,FormatString=space==0?"0":"0.##",MinWidth=86,Padding=new Thickness(3,1)};_channels[space][channel]=input;box.Children.Add(input);row.Children.Add(box);
                input.ValueChanged+=(_,_)=>{if(!_sync)FromNumbers(index);};
            }
        }
        var alphaRow=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6};alphaRow.Children.Add(new TextBlock{Text="ASS transparency",VerticalAlignment=VerticalAlignment.Center});alphaRow.Children.Add(_alphaNumber);right.Children.Add(alphaRow);
        right.Children.Add(new TextBlock{Text="0 = opaque · 255 = transparent",FontSize=11});
        right.Children.Add(new TextBlock{Text="ASS &HAABBGGRR"});right.Children.Add(_exact);right.Children.Add(new TextBlock{Text="HTML #RRGGBB · alpha stays separate"});right.Children.Add(_html);
        _spectrum.ValueChanged+=(_,_)=>{if(!_sync){_color=_spectrum.Color(_color.Transparency);Refresh();}};
        _hue.PropertyChanged+=(_,e)=>{if(e.Property==Slider.ValueProperty&&!_sync)_spectrum.SetHue(_hue.Value);};
        _alpha.PropertyChanged+=(_,e)=>{if(e.Property==Slider.ValueProperty&&!_sync){_color=_color with{Transparency=(byte)Math.Round(_alpha.Value)};Refresh();}};
        _alphaNumber.ValueChanged+=(_,_)=>{if(!_sync&&_alphaNumber.Value is {} value){_color=_color with{Transparency=(byte)value};Refresh();}};
        _exact.PropertyChanged+=(_,e)=>{if(e.Property!=TextBox.TextProperty||_sync)return;if(AssColor.TryParse(_exact.Text,out var c)){_color=c;Refresh(false);}else Invalid("Enter a complete ASS hex or signed decimal color.");};
        _html.PropertyChanged+=(_,e)=>{if(e.Property!=TextBox.TextProperty||_sync)return;if(ColorSpace.TryHtml(_html.Text,_color.Transparency,out var c)){_color=c;Refresh(updateHtml:false);}else Invalid("Enter HTML #RRGGBB.");};
        var palettes=new Grid{ColumnDefinitions=new("*,*"),ColumnSpacing=12};Grid.SetRow(palettes,1);Grid.SetColumnSpan(palettes,2);root.Children.Add(palettes);
        var fixedColors=new uint[]{0xFFFFFF,0xC0C0C0,0x808080,0x000000,0xFF0000,0xFF8000,0xFFFF00,0x80FF00,0x00FF00,0x00FFFF,0x0080FF,0x0000FF,0x8000FF,0xFF00FF,0xFF80C0,0x804000};
        palettes.Children.Add(Palette("Palette",fixedColors.Select(rgb=>new AssColor((byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb,0)),false));
        var recent=Palette("Recent colors",_recent.Load(),true);recent.Name="RecentColors";Grid.SetColumn(recent,1);palettes.Children.Add(recent);
        var footer=new StackPanel{Spacing=5};Grid.SetRow(footer,2);Grid.SetColumnSpan(footer,2);root.Children.Add(footer);footer.Children.Add(_error);
        var bar=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Spacing=5};footer.Children.Add(bar);
        _commands.CommandFailed+=(_,e)=>_error.Text=e.Exception.Message;
        ActionButton(bar,"color/eyedropper","Screen…",()=>{StartDropper();return Task.CompletedTask;},OperatingSystem.IsWindows());
        ActionButton(bar,"color/copy","Copy exact",async()=>{if(Clipboard is null)return;var data=new DataTransfer();data.Add(DataTransferItem.CreateText(_color.StyleValue));await Clipboard.SetDataAsync(data);await Clipboard.FlushAsync();});
        ActionButton(bar,"color/paste","Paste",async()=>{if(Clipboard is null)return;using var data=await Clipboard.TryGetDataAsync();var text=data is null?null:await data.TryGetTextAsync();if(ColorSpace.TryHtml(text,_color.Transparency,out var c)||AssColor.TryParse(text,out c)){_color=c;Refresh();}else Invalid("Clipboard does not contain an ASS or HTML color.");});
        ActionButton(bar,"color/cancel","Cancel",()=>{Close();return Task.CompletedTask;},cancel:true);
        ActionButton(bar,"color/accept","OK",()=>{if(_valid){_recent.Remember(_color);Close((AssColor?)_color);}return Task.CompletedTask;},accept:true);
        Closed+=(_,_)=>EndDropper();Refresh();
    }
    private void ActionButton(Panel bar,string id,string text,Func<Task> action,bool enabled=true,bool cancel=false,bool accept=false)
    {
        _commands.Register(new AppCommand(new(id,text,text,"Color"),async(_,_)=>await action(),_=>enabled));
        bar.Children.Add(new Button{Content=text,IsCancel=cancel,IsDefault=accept,Command=new RegistryCommand(_commands,id,()=>new()),Padding=new Thickness(7,2)});
    }
    private static Grid Preview(AssColor color){var grid=new Grid();grid.Children.Add(new CheckerboardControl());grid.Children.Add(new Border{Background=new SolidColorBrush(Color.FromArgb(color.Opacity,color.Red,color.Green,color.Blue))});return grid;}
    private StackPanel Palette(string label,IEnumerable<AssColor> colors,bool exact)
    {
        var panel=new StackPanel{Spacing=3};panel.Children.Add(new TextBlock{Text=label,FontWeight=FontWeight.SemiBold});var swatches=new WrapPanel();panel.Children.Add(swatches);
        foreach(var color in colors){var button=new Button{Content=Preview(color),Width=24,Height=24,Padding=new Thickness(1),Margin=new Thickness(1)};ToolTip.SetTip(button,color.StyleValue+" · "+ColorSpace.Html(color));button.Click+=(_,_)=>{_color=exact?color:color with{Transparency=_color.Transparency};Refresh();};swatches.Children.Add(button);}
        if(!swatches.Children.Any())panel.Children.Add(new TextBlock{Text="Accepted colors appear here",FontSize=11});return panel;
    }
    private void FromNumbers(int space)
    {
        var numbers=_channels[space];double N(int i)=>(double)(numbers[i].Value??0);
        _color=space switch{0=>new((byte)N(0),(byte)N(1),(byte)N(2),_color.Transparency),1=>ColorSpace.FromHsv(N(0),N(1)/100,N(2)/100,_color.Transparency),_=>ColorSpace.FromHsl(N(0),N(1)/100,N(2)/100,_color.Transparency)};Refresh();
    }
    private void Invalid(string message){_valid=false;_error.Text=message;}
    private void Refresh(bool updateExact=true,bool updateHtml=true)
    {
        _sync=true;_valid=true;_error.Text="";_spectrum.SetColor(_color);_hue.Value=_spectrum.Hue;_alpha.Value=_color.Transparency;_alphaNumber.Value=_color.Transparency;
        var hsv=ColorSpace.Hsv(_color);var hsl=ColorSpace.Hsl(_color);double[][] values=[[_color.Red,_color.Green,_color.Blue],[hsv.Hue,hsv.Saturation*100,hsv.Component*100],[hsl.Hue,hsl.Saturation*100,hsl.Component*100]];
        for(var s=0;s<3;s++)for(var i=0;i<3;i++)_channels[s][i].Value=(decimal)Math.Round(values[s][i],2);
        if(updateExact)_exact.Text=_color.StyleValue;if(updateHtml)_html.Text=ColorSpace.Html(_color);
        _preview.Background=new SolidColorBrush(Color.FromArgb(_color.Opacity,_color.Red,_color.Green,_color.Blue));
        _alpha.Background=new LinearGradientBrush{StartPoint=new(0,0,RelativeUnit.Relative),EndPoint=new(0,1,RelativeUnit.Relative),GradientStops=[new(Color.FromRgb(_color.Red,_color.Green,_color.Blue),0),new(Colors.Transparent,1)]};_sync=false;
    }
    private void StartDropper()
    {
        if(!OperatingSystem.IsWindows()||_droppers.Count>0)return;
        foreach(var screen in Screens.All)
        {
            var overlay=new Window{WindowDecorations=WindowDecorations.None,ShowInTaskbar=false,Topmost=true,CanResize=false,Background=Brushes.Transparent,TransparencyLevelHint=[WindowTransparencyLevel.Transparent],Position=screen.Bounds.Position,Width=screen.Bounds.Width/screen.Scaling,Height=screen.Bounds.Height/screen.Scaling,Cursor=new Cursor(StandardCursorType.Cross)};
            overlay.KeyDown+=(_,e)=>{if(e.Key==Key.Escape){EndDropper();e.Handled=true;}};
            overlay.PointerPressed+=(_,e)=>{if(!e.GetCurrentPoint(overlay).Properties.IsLeftButtonPressed){EndDropper();return;}try{_color=WindowsScreenColor.AtCursor(_color.Transparency);EndDropper();Refresh();}catch(Exception ex){EndDropper();_commands.ReportFailure("color/eyedropper",ex);}e.Handled=true;};
            _droppers.Add(overlay);overlay.Show();
        }
        _error.Text="Click a screen pixel; Escape or right-click cancels. Alpha is unchanged.";
    }
    private void EndDropper(){foreach(var overlay in _droppers.ToArray())overlay.Close();_droppers.Clear();}
}
