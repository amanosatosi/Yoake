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
        var lifetime=new CancellationTokenSource();
        // Copy the original selection; finding a result must not shrink scope.
        var scope=model.SelectedEvents.ToArray();
        SubtitleSearchMatch? current=null;var offset=0;var busy=false;
        SubtitleSearch Search()=>new(new(find.Text??"",matchCase.IsChecked==true,regex.IsChecked==true));
        IReadOnlyList<AssEvent> Lines()=>selected.IsChecked==true?scope:model.Events.ToArray();
        find.TextChanged+=(_,_)=>{current=null;offset=0;};
        Add("search/next","Find next",()=>Find(false));Add("search/previous","Find previous",()=>Find(true));
        Add("search/replace","Replace",async ()=>
        {
            if(current is null){await Find(false);return;}
            Search().Replace(model.ActiveEditor!,current,replace.Text??"");offset=current.Index+(replace.Text??"").Length;current=null;await Find(false);
        });
        Add("search/replace-all","Replace all",async ()=>
        {
            if(!model.CommitDraft())return;var search=Search();var lines=Lines();var replacement=replace.Text??"";
            var edits=await model.RunAnalysisAsync("Find and replace",ct=>search.PrepareReplacements(lines,replacement,ct),lifetime.Token);
            var count=SubtitleSearch.ApplyReplacements(model.ActiveEditor!,edits);status.Text=$"Changed {count} subtitle rows";current=null;
        });
        Add("search/close","Close",()=>{Close();return Task.CompletedTask;});
        Closed+=(_,_)=>{lifetime.Cancel();lifetime.Dispose();};
        async Task Find(bool backwards)
        {
            if(!model.CommitDraft())return;var search=Search();var lines=Lines();var selectedLine=model.SelectedEvent;var start=offset;
            current=await model.RunAnalysisAsync("Find subtitle",ct=>search.Find(lines,selectedLine,start,backwards,ct),lifetime.Token);
            if(current is null){status.Text="No matches";return;}model.SelectSearchResult(current);offset=backwards?current.Index:current.Index+Math.Max(1,current.Length);
            var character=new System.Globalization.StringInfo(current.Line.Text[..current.Index]).LengthInTextElements+1;status.Text=$"Row {current.Line.Number}, character {character}";
        }
        void Add(string id,string label,Func<Task> action)
        {
            commands.Register(new AppCommand(new(id,label,label,"Search"),async (_,_)=>
            {
                busy=true;commands.NotifyStateChanged();
                try{await action();}catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}finally{busy=false;commands.NotifyStateChanged();}
            },_=>!busy));
            buttons.Children.Add(new Button{Content=label,Command=new RegistryCommand(commands,id,()=>new()),Margin=new Thickness(2),Padding=new Thickness(7,3)});
        }
    }
}
