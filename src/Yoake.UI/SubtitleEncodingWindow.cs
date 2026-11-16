using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using Yoake.Core.Subtitles;

namespace Yoake.UI;

public sealed class SubtitleEncodingWindow : Window
{
    public SubtitleEncodingWindow()
    {
        Name="SubtitleEncodingDialog";Title="Open with character set";Width=620;Height=240;MinWidth=420;MinHeight=220;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var names=new NameScope();NameScope.SetNameScope(this,names);
        var choices=SubtitleEncodings.Choices();
        var choice=new AutoCompleteBox { Name="SubtitleEncoding",ItemsSource=choices,SelectedItem=choices.First(e=>e.CodePage==65001),MinimumPrefixLength=0,FilterMode=AutoCompleteFilterMode.Contains,MaxDropDownHeight=240 };
        var root=new Grid { Margin=new Thickness(16),RowDefinitions=new("Auto,Auto,*,Auto"),RowSpacing=10 };
        root.Children.Add(new TextBlock { Text="Search a character set by name or code page. Japanese Windows files commonly use Shift-JIS (932). Saving retains the chosen encoding; use Save As UTF-8 for Burmese translation.",TextWrapping=TextWrapping.Wrap });Grid.SetRow(choice,1);root.Children.Add(choice);
        var actions=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Spacing=8 };Grid.SetRow(actions,3);root.Children.Add(actions);
        var open=new Button { Name="OpenWithEncoding",Content="Open",IsDefault=true };var cancel=new Button { Content="Cancel",IsCancel=true };actions.Children.Add(open);actions.Children.Add(cancel);
        open.Click+=(_,_)=>{if(choice.SelectedItem is SubtitleEncodingChoice value)Close((int?)value.CodePage);};cancel.Click+=(_,_)=>Close((int?)null);
        AddHandler(KeyDownEvent,(_,e)=> { if(e.Key is Key.Enter or Key.Escape&&this.GetVisualDescendants().OfType<TextPresenter>().Any(p=>!string.IsNullOrEmpty(p.PreeditText)))e.Handled=true; },RoutingStrategies.Tunnel);
        names.Register(choice.Name!,choice);names.Register(open.Name!,open);names.Complete();Content=root;
    }
}
