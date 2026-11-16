using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yoake.Core.Subtitles;

namespace Yoake.UI;

public sealed class FuriganaWindow : Window
{
    public FuriganaWindow(string source,int start,int end)
    {
        var baseText=AssInlineInsertion.FuriganaBase(source,start,end);
        Name="FuriganaDialog";Title="Insert furigana";Width=620;Height=340;MinWidth=440;MinHeight=280;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var names=new NameScope();NameScope.SetNameScope(this,names);
        var root=new Grid { Margin=new Thickness(14),RowDefinitions=new("*,Auto,Auto"),RowSpacing=8 };Content=root;
        var fields=new Grid { ColumnDefinitions=new("Auto,*"),RowDefinitions=new("Auto,Auto,Auto,Auto"),RowSpacing=8,ColumnSpacing=10 };
        root.Children.Add(new ScrollViewer { Content=fields,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        void Field(string label,Control control,int row)
        {
            fields.Children.Add(new TextBlock { Text=label,VerticalAlignment=VerticalAlignment.Center,[Grid.RowProperty]=row });
            Grid.SetRow(control,row);Grid.SetColumn(control,1);fields.Children.Add(control);
            if(control.Name is {} name)names.Register(name,control);
        }
        Field("Base",new TextBlock { Text=baseText,TextWrapping=TextWrapping.Wrap,MaxHeight=65 },0);
        var reading=new TextBox { Name="FuriganaReading",PlaceholderText="Reading, e.g. にほんご" };
        var opposite=new TextBox { Name="FuriganaOpposite",PlaceholderText="Optional reading on the opposite side" };
        var preview=new TextBox { Name="FuriganaPreview",IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MinHeight=50,MaxHeight=90 };
        Field("Reading",reading,1);Field("Opposite side",opposite,2);Field("ASS preview",preview,3);
        var status=new TextBlock { TextWrapping=TextWrapping.Wrap };Grid.SetRow(status,1);root.Children.Add(status);
        var actions=new StackPanel { Orientation=Orientation.Horizontal,Spacing=8,HorizontalAlignment=HorizontalAlignment.Right };Grid.SetRow(actions,2);root.Children.Add(actions);
        var apply=new Button { Name="ApplyFurigana",Content="Insert",IsDefault=true };var cancel=new Button { Content="Cancel",IsCancel=true };
        actions.Children.Add(apply);actions.Children.Add(cancel);names.Register(apply.Name!,apply);
        FuriganaReading Choice()=>new(reading.Text??"",opposite.Text??"");
        void Update()
        {
            try
            {
                var edit=AssInlineInsertion.Furigana(source,start,end,Choice());preview.Text=edit.Text;
                status.Text="Readings are literal text. Existing timing tags outside the selection stay intact.";apply.IsEnabled=true;
            }
            catch(ArgumentException error){preview.Text="";status.Text=error.Message;apply.IsEnabled=false;}
        }
        reading.TextChanged+=(_,_)=>Update();opposite.TextChanged+=(_,_)=>Update();Update();
        apply.Click+=(_,_)=>{if(apply.IsEnabled)Close(Choice());};cancel.Click+=(_,_)=>Close((FuriganaReading?)null);
        AddHandler(KeyDownEvent,(_,e)=>{if(e.Key is Key.Enter or Key.Escape&&this.GetVisualDescendants().OfType<TextPresenter>().Any(p=>!string.IsNullOrEmpty(p.PreeditText)))e.Handled=true;},RoutingStrategies.Tunnel);
        Opened+=async (_,_)=>{await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);if(IsVisible)reading.Focus();};
        names.Complete();
    }
}
