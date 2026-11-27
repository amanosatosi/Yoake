using Avalonia.Controls;
using Avalonia.Interactivity;
using Yoake.UI.ViewModels;

namespace Yoake.UI;

public partial class MainWindow
{
    private void TimingUnitClick(object? sender,RoutedEventArgs args)
    {
        if(_model is not {} model)return;
        model.SetTimingByFrames(ReferenceEquals(sender,TimingByFrame));
        TimingByTime.IsChecked=!model.TimingByFrames;TimingByFrame.IsChecked=model.TimingByFrames;
    }

    private void UpdateVideoReadoutLayout()
    {
        var compact=VideoPlaybackControls.Bounds.Width<500;
        var columns=VideoPlaybackControls.ColumnDefinitions;
        columns[1].Width=new GridLength(1,GridUnitType.Star);
        columns[2].Width=compact?new GridLength(0):new GridLength(1,GridUnitType.Star);
        VideoPlaybackControls.RowDefinitions[1].Height=compact?GridLength.Auto:new GridLength(0);
        Grid.SetRow(VideoPositionBox,compact?1:0);Grid.SetColumn(VideoPositionBox,compact?0:1);Grid.SetColumnSpan(VideoPositionBox,compact?2:1);
        Grid.SetRow(VideoOffsetBox,compact?1:0);Grid.SetColumn(VideoOffsetBox,2);Grid.SetColumnSpan(VideoOffsetBox,compact?3:1);
    }

    private void ConfigureVideoReadouts()
    {
        Configure(VideoStartOffset,true);Configure(VideoEndOffset,false);
        VideoPlaybackControls.SizeChanged+=(_,_)=>UpdateVideoReadoutLayout();
        void Configure(Button button,bool fromStart)
        {
            async Task Run(string operation,string? color=null)
            {
                if(_model is not {} model)return;
                await model.Registry.InvokeAsync("video/readout/action",new(model.ActiveDocumentId),new VideoReadoutRequest(fromStart,operation,color));
            }
            button.Click+=async(_,_)=>await Run("copy");
            MenuItem Item(string title,string operation,string? color=null)
            {
                var item=new MenuItem{Header=title};item.Click+=async(_,_)=>await Run(operation,color);return item;
            }
            var copy=Item("Copy","copy");var insert=Item("Insert at cursor","insert");
            var fade=Item(fromStart?"Fade in from here":"Fade out from here","fade");
            var other=new MenuItem{Header="Other fade options",ItemsSource=new[]
            {
                Item(fromStart?"From white":"To white","fade","&HFFFFFF&"),
                Item(fromStart?"From black":"To black","fade","&H000000&"),
                Item(fromStart?"From custom color…":"To custom color…","fade","custom")
            }};
            var menu=new ContextMenu{ItemsSource=new object[]{copy,insert,new Separator(),fade,other}};
            menu.Opened+=(_,_)=>{var available=_model?.CanFadeFromReadout(fromStart)==true;fade.IsEnabled=other.IsEnabled=available;};
            button.ContextMenu=menu;
        }
    }
}
