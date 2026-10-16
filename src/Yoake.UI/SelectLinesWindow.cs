using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI.Commands;
using Yoake.UI.ViewModels;

namespace Yoake.UI;

public sealed class SelectLinesWindow:Window
{
    private readonly MainWindowViewModel _model;private readonly Guid _document;private bool _closed;
    private readonly TextBox _query=new(){Name="SelectQuery"};
    private readonly ComboBox _field=new(){Name="SelectField",ItemsSource=Enum.GetValues<SubtitleSearchField>()};
    private readonly ComboBox _mode=new(){Name="SelectMode",ItemsSource=Enum.GetValues<SubtitleSelectionMode>()};
    private readonly ComboBox _action=new(){Name="SelectAction",ItemsSource=Enum.GetValues<SubtitleSelectionAction>()};
    private readonly CheckBox _case=new(){Name="SelectCase",Content="Match case"};
    private readonly CheckBox _invert=new(){Name="SelectInvert",Content="Select non-matching lines"};
    private readonly CheckBox _dialogue=new(){Name="SelectDialogue",Content="Dialogue lines"};
    private readonly CheckBox _comments=new(){Name="SelectComments",Content="Comment lines"};
    private readonly CheckBox _raw=new(){Name="SelectRaw",Content="Search raw ASS source (include override tags)"};
    private readonly TextBlock _status=new(){Name="SelectStatus",MaxHeight=36,TextWrapping=Avalonia.Media.TextWrapping.Wrap,TextTrimming=Avalonia.Media.TextTrimming.CharacterEllipsis};
    public SelectLinesWindow(MainWindowViewModel model)
    {
        _model=model;_document=model.ActiveDocumentId??throw new InvalidOperationException("No subtitle document.");
        Name="SelectLinesDialog";Title="Select Lines";Width=660;Height=450;MinWidth=600;MinHeight=430;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var names=new NameScope();NameScope.SetNameScope(this,names);
        foreach(var control in new Control[]{_query,_field,_mode,_action,_case,_invert,_dialogue,_comments,_raw,_status})names.Register(control.Name!,control);
        var root=new Grid{Margin=new Thickness(12),RowDefinitions=new("Auto,*,Auto,Auto"),RowSpacing=10};Content=root;
        var query=new StackPanel{Spacing=4};query.Children.Add(new TextBlock{Text="Match text"});query.Children.Add(_query);root.Children.Add(query);
        var criteria=new Grid{ColumnDefinitions=new("*,*"),ColumnSpacing=14};Grid.SetRow(criteria,1);root.Children.Add(criteria);
        var left=new StackPanel{Spacing=7};criteria.Children.Add(left);left.Children.Add(new TextBlock{Text="Field"});left.Children.Add(_field);left.Children.Add(new TextBlock{Text="Match mode"});left.Children.Add(_mode);left.Children.Add(_case);left.Children.Add(_invert);
        var right=new StackPanel{Spacing=7};Grid.SetColumn(right,1);criteria.Children.Add(right);right.Children.Add(new TextBlock{Text="Selection action"});right.Children.Add(_action);right.Children.Add(_dialogue);right.Children.Add(_comments);
        Grid.SetRow(_raw,2);root.Children.Add(_raw);
        var footer=new StackPanel{Spacing=7};Grid.SetRow(footer,3);root.Children.Add(footer);footer.Children.Add(_status);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Spacing=8};footer.Children.Add(buttons);
        var select=new Button{Name="SelectConfirm",Content="Select and close",IsDefault=true,Command=new RegistryCommand(model.Registry,"selection/apply",()=>new(_document),parameterFactory:()=>ReadRequest(true))};
        var apply=new Button{Name="SelectApply",Content="Apply",Command=new RegistryCommand(model.Registry,"selection/apply",()=>new(_document),parameterFactory:()=>ReadRequest(false))};
        var cancel=new Button{Name="SelectCancel",Content="Cancel",IsCancel=true,Command=new RegistryCommand(model.Registry,"selection/close",()=>new(_document))};
        foreach(var button in new[]{select,apply,cancel}){names.Register(button.Name!,button);buttons.Children.Add(button);}
        _mode.ItemTemplate=new FuncDataTemplate<object>((value,_)=>new TextBlock{Text=value switch{SubtitleSelectionMode.Exact=>"Exact match",SubtitleSelectionMode.RegularExpression=>"Regular expression",_=>"Contains"}});
        _action.ItemTemplate=new FuncDataTemplate<object>((value,_)=>new TextBlock{Text=value switch{SubtitleSelectionAction.Add=>"Add to selection",SubtitleSelectionAction.Subtract=>"Subtract from selection",SubtitleSelectionAction.Intersect=>"Intersect with selection",_=>"Replace selection"}});
        var options=model.SelectionPreferences;_query.Text=options.Query;_field.SelectedItem=options.Field;_mode.SelectedItem=options.Mode;_action.SelectedItem=options.Action;
        _case.IsChecked=options.MatchCase;_invert.IsChecked=options.Invert;_dialogue.IsChecked=options.IncludeDialogue;_comments.IsChecked=options.IncludeComments;_raw.IsChecked=options.RawSource;
        model.PropertyChanged+=ModelChanged;model.SelectionWindowCloseRequested+=CloseRequested;model.Registry.CommandFailed+=Failure;
        AddHandler(InputElement.KeyDownEvent,(_,args)=>
        {
            if(args.Key!=Key.Enter)return;var source=args.Source as Control;var ancestors=source?.GetVisualAncestors().OfType<Control>().Prepend(source).ToArray()??[];
            // Dropdown items own native Enter selection before the default button.
            // Do not intercept that route; only composing text suppresses submit.
            if(ancestors.OfType<TextBox>().Any(input=>input.GetVisualDescendants().OfType<TextPresenter>().Any(presenter=>!string.IsNullOrEmpty(presenter.PreeditText))))args.Handled=true;
        },RoutingStrategies.Tunnel);
        Opened+=(_,_)=>{_query.Focus();_query.SelectAll();};
        Closed+=(_,_)=>{_closed=true;model.PropertyChanged-=ModelChanged;model.SelectionWindowCloseRequested-=CloseRequested;model.Registry.CommandFailed-=Failure;_=model.Registry.InvokeAsync("selection/close",new(_document));};names.Complete();
    }
    private SubtitleSelectionRequest ReadRequest(bool close)=>new(new(){Query=_query.Text??"",Field=(SubtitleSearchField)(_field.SelectedItem??SubtitleSearchField.Text),
        Mode=(SubtitleSelectionMode)(_mode.SelectedItem??SubtitleSelectionMode.Contains),Action=(SubtitleSelectionAction)(_action.SelectedItem??SubtitleSelectionAction.Replace),
        MatchCase=_case.IsChecked==true,Invert=_invert.IsChecked==true,IncludeDialogue=_dialogue.IsChecked==true,IncludeComments=_comments.IsChecked==true,RawSource=_raw.IsChecked==true},close);
    private void CloseRequested(object? sender,SelectionWindowCloseEventArgs args){if(!_closed&&args.Document==_document)Close();}
    private void ModelChanged(object? sender,PropertyChangedEventArgs args)
    {
        if(!Dispatcher.UIThread.CheckAccess()){Dispatcher.UIThread.Post(()=>ModelChanged(sender,args));return;}
        if(!_closed)_status.Text=_model.ActiveDocumentId==_document?_model.SelectionStatus:"Activate this window's subtitle document to select lines.";
    }
    private void Failure(object? sender,CommandFailureEventArgs args)
    {if(args.CommandId=="selection/apply")Dispatcher.UIThread.Post(()=>{if(!_closed)_status.Text=args.Exception.Message;});}
}
