using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI.Commands;

namespace Yoake.UI;
public sealed class ScriptInfoWindow : Window
{
    public ScriptInfoWindow(SubtitleEditor editor)
    {
        Title="Script Info";Width=520;Height=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel{Margin=new Thickness(14)};var panel=new StackPanel{Spacing=5};var status=new TextBlock{TextWrapping=Avalonia.Media.TextWrapping.Wrap};var bar=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};DockPanel.SetDock(bar,Dock.Bottom);root.Children.Add(bar);root.Children.Add(new ScrollViewer{Content=panel});
        var fields=new Dictionary<string,TextBox>();foreach(var key in new[]{"Title","Original Script","Original Translation","Original Editing","Original Timing","Synch Point","Script Updated By","Update Details","PlayResX","PlayResY","WrapStyle","ScaledBorderAndShadow","YCbCr Matrix"}){panel.Children.Add(new TextBlock{Text=key});var input=new TextBox{Text=editor.Document.GetScriptInfo(key),Padding=new Thickness(5,2)};fields[key]=input;panel.Children.Add(input);}
        var commands=new CommandRegistry();commands.CommandFailed+=(_,e)=>status.Text=e.Exception.Message;
        commands.Register(new AppCommand(new("script-info/apply","Apply","Apply Script Info","Subtitle"),(_,_)=>{var values=fields.ToDictionary(p=>p.Key,p=>p.Value.Text??"");foreach(var key in new[]{"PlayResX","PlayResY"})if(values[key].Length>0&&(!int.TryParse(values[key],out var n)||n<=0))throw new ArgumentException($"{key} must be positive.");editor.SetScriptInfo(values.Where(p=>p.Value!=editor.Document.GetScriptInfo(p.Key)).ToDictionary());Close();return ValueTask.CompletedTask;}));
        bar.Children.Add(new Button{Content="Apply",Command=new RegistryCommand(commands,"script-info/apply",()=>new())});var cancel=new Button{Content="Cancel",IsCancel=true};cancel.Click+=(_,_)=>Close();bar.Children.Add(cancel);bar.Children.Add(status);Content=root;
    }
}
