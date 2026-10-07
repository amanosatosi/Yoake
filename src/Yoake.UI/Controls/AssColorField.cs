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
    public string? RecentColorsPath {get;set;}
    public Action<Exception>? ReportFailure {get;set;}
    public event EventHandler? ValueChanged;
    public event EventHandler? ValueCommitted;
    public AssColorField()
    {
        var swatch=new Grid{Width=24,Height=16};swatch.Children.Add(new CheckerboardControl());swatch.Children.Add(_swatch);
        var grid=new Grid{ColumnDefinitions=new("Auto,*"),ColumnSpacing=3};var button=new Button{Content=swatch,Padding=new Thickness(3,2),MinHeight=26};
        ToolTip.SetTip(button,"Choose RGB color and alpha");grid.Children.Add(button);Grid.SetColumn(_exact,1);grid.Children.Add(_exact);Content=grid;
        // TextChanged is deferred until rendering. Draft state must be current
        // before a style-selection command can commit and replace its target.
        _exact.PropertyChanged+=(_,e)=>{if(e.Property==TextBox.TextProperty&&!_sync){SetCurrentValue(ValueProperty,_exact.Text??"");ValueChanged?.Invoke(this,EventArgs.Empty);}};
        button.Click+=async (_,_)=>
        {
            if(TopLevel.GetTopLevel(this) is not Window owner)return;
            try
            {
                var initial=AssColor.TryParse(Value,out var parsed)?parsed:new AssColor(255,255,255,0);
                var result=await new AssColorDialog(initial,RecentColorsPath).ShowDialog<AssColor?>(owner);
                if(result is {} color){SetCurrentValue(ValueProperty,color.StyleValue);ValueChanged?.Invoke(this,EventArgs.Empty);ValueCommitted?.Invoke(this,EventArgs.Empty);}
            }
            catch(Exception exception){ReportFailure?.Invoke(exception);ToolTip.SetTip(_exact,exception.Message);}
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

public sealed class CheckerboardControl : Control
{
    private static readonly IBrush Light=new SolidColorBrush(Color.FromRgb(184,184,184)),Dark=new SolidColorBrush(Color.FromRgb(132,132,132));
    public override void Render(DrawingContext context)
    {
        for(var y=0;y<Bounds.Height;y+=10)for(var x=0;x<Bounds.Width;x+=10)context.FillRectangle((x/10+y/10)%2==0?Light:Dark,new Rect(x,y,Math.Min(10,Bounds.Width-x),Math.Min(10,Bounds.Height-y)));
    }
}
