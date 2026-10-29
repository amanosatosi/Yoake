using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yoake.Core.Settings;
using Yoake.Core.Subtitles;
using Yoake.UI.Commands;
using Yoake.UI.ViewModels;

namespace Yoake.UI;

public sealed class ShiftTimesWindow:Window
{
    private readonly MainWindowViewModel _model;
    private readonly Guid _document;
    private readonly AssEvent[] _selection;
    private readonly TextBox _time=new(){Name="ShiftTime",PlaceholderText="h:mm:ss.cc"};
    private readonly TextBox _frames=new(){Name="ShiftFrames"};
    private readonly CheckBox _byFrames=new(){Name="ShiftByFrames",Content="Shift by frames"};
    private readonly CheckBox _backward=new(){Name="ShiftBackward",Content="Backward (earlier)"};
    private readonly ComboBox _fields=new(){Name="ShiftFields",ItemsSource=Enum.GetValues<SubtitleTimingFields>()};
    private readonly ComboBox _scope=new(){Name="ShiftScope",ItemsSource=Enum.GetValues<SubtitleTimingScope>()};
    private readonly ListBox _history=new(){Name="ShiftHistory",Height=140};
    private readonly TextBlock _status=new(){Name="ShiftStatus",TextWrapping=Avalonia.Media.TextWrapping.Wrap};
    private bool _closed;
    public ShiftTimesWindow(MainWindowViewModel model)
    {
        _model=model;_document=model.ActiveDocumentId??throw new InvalidOperationException("No subtitle document.");_selection=model.SelectedEvents.ToArray();
        Name="SubtitleShiftTimesDialog";Title="Shift Times";Width=740;Height=410;MinWidth=640;MinHeight=380;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var names=new NameScope();NameScope.SetNameScope(this,names);
        foreach(var control in new Control[]{_time,_frames,_byFrames,_backward,_fields,_scope,_history,_status})names.Register(control.Name!,control);
        var root=new Grid{Margin=new Thickness(12),ColumnDefinitions=new("*,*"),RowDefinitions=new("*,Auto,Auto"),ColumnSpacing=14,RowSpacing=8};Content=root;
        var options=new StackPanel{Spacing=8};root.Children.Add(options);
        options.Children.Add(new TextBlock{Text="Time amount"});options.Children.Add(_time);
        var frameRow=new Grid{ColumnDefinitions=new("*,90"),ColumnSpacing=8};frameRow.Children.Add(_byFrames);Grid.SetColumn(_frames,1);frameRow.Children.Add(_frames);options.Children.Add(frameRow);
        options.Children.Add(_backward);options.Children.Add(new TextBlock{Text="Affect rows"});options.Children.Add(_scope);
        options.Children.Add(new TextBlock{Text="Change times"});options.Children.Add(_fields);
        var history=new StackPanel{Spacing=6};Grid.SetColumn(history,1);root.Children.Add(history);history.Children.Add(new TextBlock{Text="Previous shifts — select to reuse options"});history.Children.Add(_history);
        var clear=new Button{Name="ShiftClearHistory",Content="Clear history",Command=new RegistryCommand(model.Registry,"time/shift/history/clear",()=>new(_document))};history.Children.Add(clear);names.Register(clear.Name!,clear);
        history.Children.Add(new TextBlock{Text="Frame shifts use each endpoint's VFR frame boundary. Invalid ranges are rejected before changing any rows.",TextWrapping=Avalonia.Media.TextWrapping.Wrap});
        Grid.SetRow(_status,1);Grid.SetColumnSpan(_status,2);root.Children.Add(_status);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Spacing=8};Grid.SetRow(buttons,2);Grid.SetColumnSpan(buttons,2);root.Children.Add(buttons);
        var apply=new Button{Name="ShiftApply",Content="Apply shift",IsDefault=true,Command=new RegistryCommand(model.Registry,"time/shift/apply",()=>new(_document),parameterFactory:ReadRequest)};
        var cancel=new Button{Name="ShiftCancel",Content="Cancel",IsCancel=true,Command=new RegistryCommand(model.Registry,"time/shift/close",()=>new(_document))};
        foreach(var button in new[]{apply,cancel}){names.Register(button.Name!,button);buttons.Children.Add(button);}
        Restore(model.ShiftPreferences);_history.ItemsSource=model.ShiftHistory;
        _fields.ItemTemplate=new FuncDataTemplate<object>((value,_)=>new TextBlock{Text=value switch{SubtitleTimingFields.Start=>"Start only",SubtitleTimingFields.End=>"End only",_=>"Start and end"}});
        _scope.ItemTemplate=new FuncDataTemplate<object>((value,_)=>new TextBlock{Text=value switch{SubtitleTimingScope.Selected=>"Selected rows",SubtitleTimingScope.SelectionOnward=>"Selection onward",_=>"All rows"}});
        _byFrames.Click+=(_,_)=>UpdateInputs();_history.SelectionChanged+=(_,_)=>{if(_history.SelectedItem is SubtitleShiftHistoryEntry entry)Restore(entry.Preferences);};
        model.PropertyChanged+=ModelChanged;model.TimingWindowCloseRequested+=CloseRequested;
        AddHandler(InputElement.KeyDownEvent,(_,args)=>
        {
            if(args.Key!=Key.Enter)return;
            var source=args.Source as Control;var ancestors=source?.GetVisualAncestors().OfType<Control>().Prepend(source).ToArray()??[];
            if(ancestors.OfType<TextBox>().Any(input=>input.GetVisualDescendants().OfType<TextPresenter>().Any(presenter=>!string.IsNullOrEmpty(presenter.PreeditText))))args.Handled=true;
            if(ancestors.OfType<ComboBox>().Any(input=>input.IsDropDownOpen))args.Handled=true;
        },RoutingStrategies.Tunnel);
        Opened+=(_,_)=>{_time.Focus();_time.SelectAll();};
        Closed+=(_,_)=>{_closed=true;model.PropertyChanged-=ModelChanged;model.TimingWindowCloseRequested-=CloseRequested;_=model.Registry.InvokeAsync("time/shift/close",new(_document));};
        names.Complete();
    }
    private object ReadRequest()
    {
        var byFrames=_byFrames.IsChecked==true;
        var input=(_time.Text??"").Trim();
        if(!AssTime.TryParse(input,out var milliseconds)||milliseconds<0||!input.All(character=>char.IsAsciiDigit(character)||character is ':' or '.'))
        {if(!byFrames)throw new ArgumentException("Enter a time amount as h:mm:ss.cc.");milliseconds=_model.ShiftPreferences.Milliseconds;}
        if(!int.TryParse(_frames.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var frames)||frames<0)
        {if(byFrames)throw new ArgumentException("Enter a non-negative integer frame amount.");frames=_model.ShiftPreferences.Frames;}
        return new SubtitleShiftToolRequest(new(){Milliseconds=milliseconds,Frames=frames,ByFrames=_byFrames.IsChecked==true,Backward=_backward.IsChecked==true,
            Fields=(SubtitleTimingFields)(_fields.SelectedItem??SubtitleTimingFields.Both),Scope=(SubtitleTimingScope)(_scope.SelectedItem??SubtitleTimingScope.All)},_selection);
    }
    private void Restore(SubtitleShiftPreferences preferences)
    {
        _time.Text=AssTime.Format(preferences.Milliseconds);_frames.Text=preferences.Frames.ToString(CultureInfo.InvariantCulture);
        _byFrames.IsChecked=preferences.ByFrames&&_model.CanShiftByFrames;_backward.IsChecked=preferences.Backward;_fields.SelectedItem=preferences.Fields;
        _scope.SelectedItem=_selection.Length==0?SubtitleTimingScope.All:preferences.Scope;UpdateInputs();
    }
    private void UpdateInputs(){_byFrames.IsEnabled=_model.CanShiftByFrames;_time.IsEnabled=_byFrames.IsChecked!=true;_frames.IsEnabled=_byFrames.IsChecked==true;}
    private void CloseRequested(object? sender,TimingWindowCloseEventArgs args){if(!_closed&&args.Document==_document)Close();}
    private void ModelChanged(object? sender,PropertyChangedEventArgs args)
    {
        if(!Dispatcher.UIThread.CheckAccess()){Dispatcher.UIThread.Post(()=>ModelChanged(sender,args));return;}
        if(_closed)return;if(args.PropertyName==nameof(MainWindowViewModel.ShiftHistory))_history.ItemsSource=_model.ShiftHistory;
        if(args.PropertyName==nameof(MainWindowViewModel.SubtitleStatus))_status.Text=_model.SubtitleStatus;
    }
}
