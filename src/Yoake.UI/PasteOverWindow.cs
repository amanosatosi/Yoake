using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Yoake.Core.Subtitles;

namespace Yoake.UI;

public sealed class PasteOverWindow:Window
{
    private readonly Dictionary<PasteOverFields,CheckBox> _fields=[];
    public PasteOverWindow(PasteOverFields initial)
    {
        Name="PasteOverDialog";Title="Select Fields to Paste Over";Width=355;MinWidth=320;
        SizeToContent=SizeToContent.Height;CanResize=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var names=new NameScope();NameScope.SetNameScope(this,names);
        var panel=new StackPanel{Margin=new Thickness(12),Spacing=6};Content=panel;
        panel.Children.Add(new TextBlock{Text="Select the fields to paste over:",TextWrapping=Avalonia.Media.TextWrapping.Wrap});
        foreach(var (flag,label) in new[]{(PasteOverFields.Comment,"Comment"),(PasteOverFields.Layer,"Layer"),(PasteOverFields.Start,"Start Time"),(PasteOverFields.End,"End Time"),(PasteOverFields.Style,"Style"),(PasteOverFields.Actor,"Actor"),(PasteOverFields.MarginL,"Margin Left"),(PasteOverFields.MarginR,"Margin Right"),(PasteOverFields.MarginV,"Margin Vertical"),(PasteOverFields.Effect,"Effect"),(PasteOverFields.Text,"Text")})
        {
            var check=new CheckBox{Name="PasteField"+flag,Content=label,IsChecked=initial.HasFlag(flag),MinHeight=24};
            _fields.Add(flag,check);names.Register(check.Name!,check);panel.Children.Add(check);
        }
        var presets=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6};panel.Children.Add(presets);
        foreach(var (label,flags) in new[]{("All",PasteOverFields.All),("None",PasteOverFields.None),("Times",PasteOverFields.Times),("Text",PasteOverFields.Text)})
        {
            var button=new Button{Name="PastePreset"+label,Content=label,MinWidth=64};
            button.Click+=(_,_)=>{foreach(var (flag,check) in _fields)check.IsChecked=flags.HasFlag(flag);};
            names.Register(button.Name!,button);presets.Children.Add(button);
        }
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Spacing=6,Margin=new Thickness(0,6,0,0)};panel.Children.Add(buttons);
        var ok=new Button{Name="PasteConfirm",Content="OK",IsDefault=true,MinWidth=72};var cancel=new Button{Name="PasteCancel",Content="Cancel",IsCancel=true,MinWidth=72};
        ok.Click+=(_,_)=>Close((PasteOverFields?)_fields.Where(p=>p.Value.IsChecked==true).Aggregate(PasteOverFields.None,(result,p)=>result|p.Key));
        cancel.Click+=(_,_)=>Close(null);buttons.Children.Add(ok);buttons.Children.Add(cancel);
        names.Register(ok.Name!,ok);names.Register(cancel.Name!,cancel);names.Complete();
        Opened+=(_,_)=>_fields[PasteOverFields.Comment].Focus();
    }
}
