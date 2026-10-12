using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Yoake.Core.Subtitles;
using Yoake.UI.ViewModels;

namespace Yoake.UI;

// Input presentation only: conversion and seeking use the owning command model.
public sealed class VideoJumpWindow : Window
{
    private readonly TextBox _frame=new(){Name="JumpFrame"};
    private readonly TextBox _time=new(){Name="JumpTime"};
    private readonly TextBlock _error=new(){Name="JumpError",TextWrapping=Avalonia.Media.TextWrapping.Wrap};
    private readonly MainWindowViewModel _model;private readonly Guid? _document;
    private bool _sync;private VideoJumpRequest? _request;
    public VideoJumpWindow(MainWindowViewModel model)
    {
        _model=model;_document=model.ActiveDocumentId;Name="VideoJumpDialog";Title="Jump to frame or time";Width=380;MinWidth=320;
        SizeToContent=SizeToContent.Height;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var names=new NameScope();NameScope.SetNameScope(this,names);
        var panel=new StackPanel{Margin=new Thickness(12),Spacing=8};Content=panel;
        panel.Children.Add(new TextBlock{Text="Frame"});panel.Children.Add(_frame);panel.Children.Add(new TextBlock{Text="Time (h:mm:ss.cc)"});panel.Children.Add(_time);panel.Children.Add(_error);
        var row=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,HorizontalAlignment=HorizontalAlignment.Right};panel.Children.Add(row);
        var jump=new Button{Name="JumpConfirm",Content="Jump",IsDefault=true};var cancel=new Button{Name="JumpCancel",Content="Cancel",IsCancel=true};row.Children.Add(jump);row.Children.Add(cancel);
        foreach(var c in new Control[]{_frame,_time,_error,jump,cancel})names.Register(c.Name!,c);
        _frame.TextChanged+=(_,_)=>ReadFrame();_time.TextChanged+=(_,_)=>ReadTime();
        _frame.Text=model.CurrentFrame.ToString(CultureInfo.InvariantCulture);
        jump.Click+=(_,_)=>{if(_request is not null&&model.ActiveDocumentId==_document)Close(_request);else _error.Text="Enter a valid frame or time within this video.";};
        cancel.Click+=(_,_)=>Close(null);
        AddHandler(KeyDownEvent,(_,e)=>
        {
            if(e.Key!=Key.Enter)return;
            if((e.Source as Control)?.GetVisualAncestors().Prepend(e.Source as Control).OfType<TextBox>()
                .Any(input=>input.GetVisualDescendants().OfType<TextPresenter>().Any(p=>!string.IsNullOrEmpty(p.PreeditText)))==true)e.Handled=true;
        },RoutingStrategies.Tunnel);
        Opened+=(_,_)=>{_frame.Focus();_frame.SelectAll();};names.Complete();
    }
    private void ReadFrame()
    {
        if(_sync)return;_request=null;
        if(!int.TryParse(_frame.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var frame)||frame<0||frame>=_model.FrameTimes.Count)
        {_error.Text="Choose a frame from 0 to "+Math.Max(0,_model.FrameTimes.Count-1)+".";return;}
        _sync=true;try{_time.Text=AssTime.Format((long)Math.Round(_model.FrameTimes[frame]*1000));}finally{_sync=false;}
        _request=new(Frame:frame);_error.Text="";
    }
    private void ReadTime()
    {
        if(_sync)return;_request=null;
        if(!AssTime.TryParse(_time.Text,out var ms)){_error.Text="Enter h:mm:ss.cc.";return;}
        int frame;
        try{frame=_model.VideoFrameAtMilliseconds(ms);}catch(Exception e)when(e is ArgumentException or OverflowException){_error.Text="Choose a time within the loaded video.";return;}
        if(frame<0||frame>=_model.FrameTimes.Count){_error.Text="Choose a time within the loaded video.";return;}
        _sync=true;try{_frame.Text=frame.ToString(CultureInfo.InvariantCulture);}finally{_sync=false;}
        _request=new(Frame:frame);_error.Text="";
    }
}
