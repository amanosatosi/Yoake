using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI.Commands;
using Yoake.UI.ViewModels;

namespace Yoake.UI;
public sealed class FindWindow : Window
{
    public FindWindow(MainWindowViewModel model)
    {
        Title="Find / Replace";Width=580;SizeToContent=SizeToContent.Height;CanResize=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel{Margin=new Thickness(16),Spacing=8};var find=new TextBox{PlaceholderText="Find in event text"};var replace=new TextBox{PlaceholderText="Replace with"};panel.Children.Add(find);panel.Children.Add(replace);
        var options=new StackPanel{Orientation=Orientation.Horizontal,Spacing=12};var matchCase=new CheckBox{Content="Match case"};var regex=new CheckBox{Content="Regular expression"};var selected=new CheckBox{Content="Selected rows"};options.Children.Add(matchCase);options.Children.Add(regex);options.Children.Add(selected);panel.Children.Add(options);
        var status=new TextBlock{TextWrapping=Avalonia.Media.TextWrapping.Wrap};var buttons=new WrapPanel{Orientation=Orientation.Horizontal};panel.Children.Add(buttons);panel.Children.Add(status);Content=panel;
        var commands=new CommandRegistry();commands.CommandFailed+=(_,e)=>status.Text=e.Exception.Message;
        SubtitleSearchMatch? current=null;var offset=0;
        SubtitleSearch Search()=>new(new(find.Text??"",matchCase.IsChecked==true,regex.IsChecked==true));
        IReadOnlyList<AssEvent> Lines()=>selected.IsChecked==true?model.SelectedEvents.ToArray():model.Events;
        Add("search/next","Find next",()=>Find(false));Add("search/previous","Find previous",()=>Find(true));
        Add("search/replace","Replace",()=>{if(current is null){Find(false);return;}Search().Replace(model.ActiveEditor!,current,replace.Text??"");offset=current.Index+(replace.Text??"").Length;current=null;Find(false);});
        Add("search/replace-all","Replace all",()=>{if(!model.CommitDraft())return;var count=Search().ReplaceAll(model.ActiveEditor!,Lines(),replace.Text??"");status.Text=$"Changed {count} subtitle rows";current=null;});
        Add("search/close","Close",Close);
        void Find(bool backwards){if(!model.CommitDraft())return;current=Search().Find(Lines(),model.SelectedEvent,offset,backwards);if(current is null){status.Text="No matches";return;}model.SelectSearchResult(current);offset=backwards?current.Index:current.Index+Math.Max(1,current.Length);status.Text=$"Row {current.Line.Number}, character {current.Index+1}";}
        void Add(string id,string label,Action action){commands.Register(new AppCommand(new(id,label,label,"Search"),(_,_)=>{action();return ValueTask.CompletedTask;}));buttons.Children.Add(new Button{Content=label,Command=new RegistryCommand(commands,id,()=>new()),Margin=new Thickness(2),Padding=new Thickness(7,3)});}
    }
}
