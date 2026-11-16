using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Yoake.Core.Media;

namespace Yoake.UI;

public sealed class SubtitleTrackWindow : Window
{
    public SubtitleTrackWindow(IReadOnlyList<MatroskaSubtitleTrack> tracks)
    {
        Name="SubtitleTrackDialog";Title="Open embedded subtitles";Width=680;Height=360;MinWidth=440;MinHeight=240;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var names=new NameScope();NameScope.SetNameScope(this,names);
        var root=new Grid { Margin=new Thickness(16),RowDefinitions=new("Auto,*,Auto"),RowSpacing=12 };
        root.Children.Add(new TextBlock { Text="Choose a subtitle track. Extraction opens a separate unsaved document; the video file stays intact.",TextWrapping=TextWrapping.Wrap });
        var list=new ListBox { Name="SubtitleTracks",ItemsSource=tracks,SelectedItem=tracks.FirstOrDefault(t=>t.Supported) };Grid.SetRow(list,1);root.Children.Add(list);
        var actions=new StackPanel { Orientation=Orientation.Horizontal,Spacing=8,HorizontalAlignment=HorizontalAlignment.Right };Grid.SetRow(actions,2);root.Children.Add(actions);
        var open=new Button { Name="ExtractSubtitleTrack",Content="Open",IsDefault=true };var cancel=new Button { Content="Cancel",IsCancel=true };actions.Children.Add(open);actions.Children.Add(cancel);
        void Update()=>open.IsEnabled=list.SelectedItem is MatroskaSubtitleTrack { Supported:true };
        list.SelectionChanged+=(_,_)=>Update();Update();
        open.Click+=(_,_)=>{if(list.SelectedItem is MatroskaSubtitleTrack { Supported:true } track)Close((ulong?)track.Number);};
        cancel.Click+=(_,_)=>Close((ulong?)null);
        names.Register(list.Name!,list);names.Register(open.Name!,open);names.Complete();Content=root;
    }
}
