using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Yoake.Core.Subtitles;
using Yoake.UI.Commands;
using Yoake.UI.ViewModels;

namespace Yoake.UI;

public sealed class ResampleWindow:Window
{
    private readonly MainWindowViewModel _model;
    private readonly Guid _document;
    private readonly TextBox _sourceX=new(){Name="ResampleSourceX"},_sourceY=new(){Name="ResampleSourceY"},_targetX=new(){Name="ResampleTargetX"},_targetY=new(){Name="ResampleTargetY"};
    private readonly TextBox _left=new(){Name="ResampleLeft",Text="0"},_right=new(){Name="ResampleRight",Text="0"},_top=new(){Name="ResampleTop",Text="0"},_bottom=new(){Name="ResampleBottom",Text="0"};
    private readonly ComboBox _aspect=new(){Name="ResampleAspect",ItemsSource=Enum.GetValues<ResampleAspect>(),SelectedItem=ResampleAspect.Stretch};
    private readonly CheckBox _symmetric=new(){Name="ResampleSymmetric",Content="Symmetric margins",IsChecked=true};
    private readonly TextBlock _summary=new(){Name="ResampleSummary",TextWrapping=TextWrapping.Wrap};
    private readonly ListBox _warnings=new(){Name="ResampleWarnings",MinHeight=60};
    private readonly Button _video=new(){Name="ResampleFromVideo",Content="From video"};
    private readonly Control[] _inputs;
    private bool _changing,_closed;
    public ResampleWindow(MainWindowViewModel model,Guid document)
    {
        _model=model;_document=document;Name="ResampleDialog";Title="Resample Resolution";Width=650;Height=570;MinWidth=590;MinHeight=530;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        _inputs=[_sourceX,_sourceY,_targetX,_targetY,_left,_right,_top,_bottom,_aspect,_symmetric];
        var names=new NameScope();NameScope.SetNameScope(this,names);foreach(var control in _inputs.Append(_summary).Append(_warnings).Append(_video))names.Register(control.Name!,control);
        var root=new Grid{Margin=new Thickness(12),RowDefinitions=new("Auto,Auto,Auto,Auto,*,Auto"),RowSpacing=10};Content=root;
        var dimensions=new Grid{ColumnDefinitions=new("*,*"),ColumnSpacing=14};root.Children.Add(dimensions);
        StackPanel Dimensions(string label,TextBox x,TextBox y)
        {var p=new StackPanel{Spacing=5};p.Children.Add(new TextBlock{Text=label});var row=new Grid{ColumnDefinitions=new("*,Auto,*"),ColumnSpacing=7};row.Children.Add(x);var cross=new TextBlock{Text="×",VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(cross,1);row.Children.Add(cross);Grid.SetColumn(y,2);row.Children.Add(y);p.Children.Add(row);return p;}
        var source=Dimensions("Source script resolution",_sourceX,_sourceY);dimensions.Children.Add(source);var target=Dimensions("Target resolution",_targetX,_targetY);Grid.SetColumn(target,1);dimensions.Children.Add(target);
        var script=new Button{Name="ResampleFromScript",Content="From script"};names.Register(script.Name!,script);source.Children.Add(script);target.Children.Add(_video);
        var aspectPanel=new StackPanel{Spacing=5};Grid.SetRow(aspectPanel,1);root.Children.Add(aspectPanel);aspectPanel.Children.Add(new TextBlock{Text="Aspect-ratio change"});aspectPanel.Children.Add(_aspect);
        _aspect.ItemTemplate=new FuncDataTemplate<object>((item,_)=>new TextBlock{Text=item switch{ResampleAspect.AddBorders=>"Add borders",ResampleAspect.RemoveBorders=>"Remove borders",ResampleAspect.Manual=>"Manual margin offsets",_=>"Stretch"}});
        var margins=new Grid{ColumnDefinitions=new("*,*,*,*"),RowDefinitions=new("Auto,Auto,Auto"),ColumnSpacing=8,RowSpacing=4};Grid.SetRow(margins,2);root.Children.Add(margins);
        var fields=new[]{_left,_right,_top,_bottom};var labels=new[]{"Left","Right","Top","Bottom"};
        for(var i=0;i<4;i++){var label=new TextBlock{Text=labels[i]};Grid.SetColumn(label,i);margins.Children.Add(label);Grid.SetColumn(fields[i],i);Grid.SetRow(fields[i],1);margins.Children.Add(fields[i]);}
        Grid.SetRow(_symmetric,2);Grid.SetColumnSpan(_symmetric,4);margins.Children.Add(_symmetric);
        var description=new StackPanel{Spacing=6};Grid.SetRow(description,3);root.Children.Add(description);
        description.Children.Add(new TextBlock{Text="Preview before applying. The valid active draft is included in the same undo operation. Colors and YCbCr matrix remain unchanged. Unsupported extension geometry is preserved and listed below.",TextWrapping=TextWrapping.Wrap});description.Children.Add(_summary);
        Grid.SetRow(_warnings,4);root.Children.Add(_warnings);_warnings.ItemTemplate=new FuncDataTemplate<ResampleWarning>((warning,_)=>new TextBlock{Text=warning?.ToString(),TextWrapping=TextWrapping.Wrap});
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Spacing=8};Grid.SetRow(buttons,5);root.Children.Add(buttons);
        var preview=new Button{Name="ResamplePreview",Content="Preview",IsDefault=true,Command=new RegistryCommand(model.Registry,"resample/preview",()=>new(_document),parameterFactory:ReadOptions)};
        var apply=new Button{Name="ResampleApply",Content="Apply reviewed changes",Command=new RegistryCommand(model.Registry,"resample/apply",()=>new(_document))};
        var cancel=new Button{Name="ResampleCancel",Content="Cancel",IsCancel=true,Command=new RegistryCommand(model.Registry,"resample/cancel",()=>new(_document))};
        foreach(var button in new[]{preview,apply,cancel}){names.Register(button.Name!,button);buttons.Children.Add(button);}
        _changing=true;SetDimensions(_sourceX,_sourceY,model.ScriptResolution);var video=model.CurrentVideoDetails;SetDimensions(_targetX,_targetY,video is null?model.ScriptResolution:(video.Width,video.Height));_changing=false;
        script.Click+=(_,_)=>{SetDimensions(_sourceX,_sourceY,model.ScriptResolution);Invalidate();};
        _video.Click+=(_,_)=>{if(model.CurrentVideoDetails is {} current){SetDimensions(_targetX,_targetY,(current.Width,current.Height));Invalidate();}};
        foreach(var field in _inputs.OfType<TextBox>())field.TextChanged+=(_,_)=>Invalidate();_aspect.SelectionChanged+=(_,_)=>Invalidate();_symmetric.Click+=(_,_)=>Invalidate();
        model.PropertyChanged+=ModelChanged;model.ResampleCloseRequested+=CloseRequested;
        AddHandler(InputElement.KeyDownEvent,(_,args)=>
        {
            if(args.Key is not (Key.Enter or Key.Escape))return;var control=args.Source as Control;
            var ancestors=control?.GetVisualAncestors().OfType<Control>().Prepend(control).ToArray()??[];
            if(ancestors.OfType<TextBox>().Any(input=>input.GetVisualDescendants().OfType<TextPresenter>().Any(p=>!string.IsNullOrEmpty(p.PreeditText))))args.Handled=true;
            if(args.Key==Key.Enter&&ancestors.OfType<ComboBox>().Any(input=>input.IsDropDownOpen))args.Handled=true;
        },RoutingStrategies.Tunnel);
        Opened+=(_,_)=>{_targetX.Focus();_targetX.SelectAll();};Closed+=(_,_)=>{_closed=true;model.PropertyChanged-=ModelChanged;model.ResampleCloseRequested-=CloseRequested;_=model.Registry.InvokeAsync("resample/cancel",new(_document));};
        Refresh();names.Complete();
    }
    private static void SetDimensions(TextBox x,TextBox y,(int Width,int Height) dimensions){x.Text=dimensions.Width.ToString(CultureInfo.InvariantCulture);y.Text=dimensions.Height.ToString(CultureInfo.InvariantCulture);}
    private object ReadOptions()
    {
        int Number(TextBox field,bool margin=false)
        {if(!int.TryParse(field.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var n)||(margin?n is <-9999 or >9999:n is <1 or >999999))throw new ArgumentException(margin?"Margin offsets must be −9999–9999.":"Dimensions must be integers between 1 and 999999.");return n;}
        var aspect=(ResampleAspect)(_aspect.SelectedItem??ResampleAspect.Stretch);var manual=aspect==ResampleAspect.Manual;
        return new ResampleOptions(Number(_sourceX),Number(_sourceY),Number(_targetX),Number(_targetY),aspect,manual?Number(_left,true):0,manual?Number(_right,true):0,manual?Number(_top,true):0,manual?Number(_bottom,true):0);
    }
    private void Invalidate()
    {
        if(_changing||_closed)return;_changing=true;
        try{if(_symmetric.IsChecked==true){_right.Text=_left.Text;_bottom.Text=_top.Text;}_= _model.Registry.InvokeAsync("resample/invalidate",new(_document));Refresh();}finally{_changing=false;}
    }
    private void Refresh()
    {
        var busy=_model.HasEditOperation;foreach(var control in _inputs)control.IsEnabled=!busy;
        var manual=_aspect.SelectedItem is ResampleAspect.Manual;_left.IsEnabled=_top.IsEnabled=_symmetric.IsEnabled=manual&&!busy;_right.IsEnabled=_bottom.IsEnabled=manual&&!busy&&_symmetric.IsChecked!=true;
        _video.IsEnabled=!busy&&_model.CurrentVideoDetails is not null;
        _summary.Text=busy?"Calculating on an immutable document snapshot…":_model.ResamplePreview?.Summary??"Set dimensions and choose Preview.";_warnings.ItemsSource=_model.ResamplePreview?.Warnings;
    }
    private void ModelChanged(object? sender,PropertyChangedEventArgs args)
    {
        if(_closed)return;if(_model.ActiveDocumentId!=_document){Close();return;}
        if(args.PropertyName is nameof(MainWindowViewModel.ResamplePreview) or nameof(MainWindowViewModel.HasEditOperation))Refresh();
        if(args.PropertyName==nameof(MainWindowViewModel.SubtitleStatus)&&_model.ResamplePreview is null)_summary.Text=_model.SubtitleStatus;
    }
    private void CloseRequested(object? sender,ResampleCloseEventArgs args){if(!_closed&&args.Document==_document)Close();}
}
