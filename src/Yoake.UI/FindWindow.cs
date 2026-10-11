using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yoake.Core.Commands;
using Yoake.Core.Hotkeys;
using Yoake.Core.Settings;
using Yoake.Core.Subtitles;
using Yoake.UI.Commands;
using Yoake.UI.ViewModels;

namespace Yoake.UI;

public sealed class FindWindow : Window
{
    private readonly MainWindowViewModel _model;
    private readonly SubtitleSearchToolState _tool;
    private readonly AssEvent[] _scope;
    private readonly TextBox _query=new(){Name="FindQuery",PlaceholderText="Find"};
    private readonly TextBox _replacement=new(){Name="FindReplacement",PlaceholderText="Replace with"};
    private readonly CheckBox _case=new(){Name="FindMatchCase",Content="Match case"};
    private readonly CheckBox _regex=new(){Name="FindRegex",Content="Regular expression"};
    private readonly CheckBox _whole=new(){Name="FindWholeField",Content="Whole field"};
    private readonly CheckBox _raw=new(){Name="FindRawSource",Content="Raw ASS source"};
    private readonly CheckBox _comments=new(){Name="FindIgnoreComments",Content="Skip comments"};
    private readonly CheckBox _selected=new(){Name="FindSelectedRows",Content="Selected rows"};
    private readonly ComboBox _field=new(){Name="FindField",MinWidth=130};
    private readonly TextBlock _status=new(){Name="FindStatus",TextWrapping=Avalonia.Media.TextWrapping.Wrap};
    private bool _closed;
    public Guid DocumentId {get;}
    public FindWindow(MainWindowViewModel model,bool focusReplacement=false)
    {
        _model=model;DocumentId=model.ActiveDocumentId??throw new InvalidOperationException("No subtitle document.");_tool=model.SearchState(DocumentId);
        _scope=model.SelectedEvents.ToArray();
        Name="SubtitleFindDialog";Title="Find / Replace — "+model.SuggestedSubtitleFileName;Width=760;Height=340;MinWidth=640;MinHeight=300;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var names=new NameScope();NameScope.SetNameScope(this,names);
        foreach(var control in new Control[]{_query,_replacement,_case,_regex,_whole,_raw,_comments,_selected,_field,_status})names.Register(control.Name!,control);
        var preferences=_tool.Preferences;
        _query.Text=preferences.Query;_replacement.Text=preferences.Replacement;_case.IsChecked=preferences.MatchCase;_regex.IsChecked=preferences.RegularExpression;
        _whole.IsChecked=preferences.WholeField;_raw.IsChecked=preferences.RawSource;_comments.IsChecked=preferences.IgnoreComments;_selected.IsChecked=preferences.SelectionOnly;
        _field.ItemsSource=Enum.GetValues<SubtitleSearchField>();_field.SelectedItem=preferences.Field;
        var root=new Grid{Margin=new Thickness(12),RowDefinitions=new("Auto,Auto,Auto,Auto,*,Auto"),RowSpacing=8};Content=root;
        At(_query,0);At(_replacement,1);
        var options=new WrapPanel{Orientation=Orientation.Horizontal};At(options,2);
        foreach(var check in new[]{_case,_regex,_whole,_raw,_comments}){check.Margin=new Thickness(0,0,12,4);options.Children.Add(check);}
        var scope=new StackPanel{Orientation=Orientation.Horizontal,Spacing=12};At(scope,3);scope.Children.Add(new TextBlock{Text="Field",VerticalAlignment=VerticalAlignment.Center});scope.Children.Add(_field);scope.Children.Add(_selected);
        var information=new StackPanel{Spacing=6};At(information,4);
        information.Children.Add(new TextBlock{Text="Visible-text search preserves override tags, drawings and Mangetsu syntax. Select Raw ASS source to search or change source syntax explicitly.",TextWrapping=Avalonia.Media.TextWrapping.Wrap});information.Children.Add(_status);
        var buttons=new WrapPanel{Orientation=Orientation.Horizontal};At(buttons,5);
        Add(CommandIds.FindNext,"Find next","FindNext",isDefault:true);Add("search/previous","Find previous","FindPrevious");
        Add("search/replace","Replace","FindReplace");Add("search/replace-all","Replace all","FindReplaceAll");
        Add("search/cancel","Cancel search","FindCancel");Add("search/close","Close","FindClose",isCancel:true);
        _query.TextChanged+=OptionsChanged;_replacement.TextChanged+=OptionsChanged;
        foreach(var check in new[]{_case,_regex,_whole,_raw,_comments,_selected})check.Click+=OptionsChanged;
        _field.SelectionChanged+=OptionsChanged;
        _tool.PropertyChanged+=ToolChanged;_model.SearchWindowCloseRequested+=CloseRequested;
        _model.Registry.StateChanged+=CommandStateChanged;
        AddHandler(InputElement.KeyDownEvent,HandleKey,RoutingStrategies.Tunnel);
        Opened+=(_,_)=>FocusSearch(focusReplacement);
        Closed+=(_,_)=>
        {
            _closed=true;_tool.PropertyChanged-=ToolChanged;_model.SearchWindowCloseRequested-=CloseRequested;_model.Registry.StateChanged-=CommandStateChanged;
            _=_model.Registry.InvokeAsync("search/cancel",new(DocumentId));
        };
        names.Complete();ToolChanged(null,new(nameof(SubtitleSearchToolState.Status)));
        void At(Control control,int row){Grid.SetRow(control,row);root.Children.Add(control);}
        void Add(string id,string label,string name,bool isDefault=false,bool isCancel=false)
        {
            var adapter=new RegistryCommand(_model.Registry,id,()=>new(DocumentId),hotkeys:()=>_model.HotkeyProfile,parameterFactory:()=>ReadRequest());
            var button=new Button{Name=name,Content=label,Command=adapter,Margin=new Thickness(0,0,6,0),Padding=new Thickness(8,4),IsDefault=isDefault,IsCancel=isCancel};
            ToolTip.SetTip(button,adapter.Tooltip);adapter.PropertyChanged+=(_,args)=>{if(args.PropertyName==nameof(RegistryCommand.Tooltip))ToolTip.SetTip(button,adapter.Tooltip);};
            names.Register(name,button);buttons.Children.Add(button);
        }
    }
    public void FocusSearch(bool replacement=false)
    {
        Activate();Dispatcher.UIThread.Post(()=>{if(_closed)return;var input=replacement?_replacement:_query;input.Focus();input.SelectAll();});
    }
    private SubtitleSearchRequest ReadRequest()=>new(new SubtitleSearchPreferences
    {
        Query=_query.Text??"",Replacement=_replacement.Text??"",MatchCase=_case.IsChecked==true,RegularExpression=_regex.IsChecked==true,
        RawSource=_raw.IsChecked==true,IgnoreComments=_comments.IsChecked==true,WholeField=_whole.IsChecked==true,SelectionOnly=_selected.IsChecked==true,
        Field=(SubtitleSearchField)(_field.SelectedItem??SubtitleSearchField.Text),
    },_scope);
    private void OptionsChanged(object? sender,EventArgs args)
    {
        // A deferred TextChanged can arrive after Find already captured exactly
        // these values. Cancel only genuinely obsolete work.
        if(_tool.Preferences!=ReadRequest().Preferences)_=_model.Registry.InvokeAsync("search/cancel",new(DocumentId));
    }
    private void CloseRequested(object? sender,SearchWindowCloseEventArgs args){if(args.Document==DocumentId)Close();}
    private void ToolChanged(object? sender,PropertyChangedEventArgs args)
    {
        if(!Dispatcher.UIThread.CheckAccess()){Dispatcher.UIThread.Post(()=>ToolChanged(sender,args));return;}
        if(!_closed)_status.Text=_tool.Status;
    }
    private void CommandStateChanged(object? sender,EventArgs args)
    {
        if(!Dispatcher.UIThread.CheckAccess()){Dispatcher.UIThread.Post(()=>CommandStateChanged(sender,args));return;}
        if(!_closed)_status.Text=_model.ActiveDocumentId==DocumentId?_tool.Status:"Activate this window's subtitle document to search.";
    }
    private async void HandleKey(object? sender,KeyEventArgs args)
    {
        if(args.Handled)return;
        var source=args.Source as Control;var ancestors=source?.GetVisualAncestors().OfType<Control>().Prepend(source).ToArray()??[];
        if(ancestors.OfType<TextBox>().Any(input=>input.GetVisualDescendants().OfType<TextPresenter>().Any(presenter=>!string.IsNullOrEmpty(presenter.PreeditText))))
        {if(args.Key==Key.Enter)args.Handled=true;return;}
        if(args.Key==Key.Enter&&ancestors.Any(input=>input is ComboBox{IsDropDownOpen:true}))return;
        var gesture=new HotkeyGesture(EditorKeyNames.FromEvent(args),EditorKeyNames.FromModifiers(args.KeyModifiers));
        var binding=_model.Hotkeys.Resolve(gesture,[HotkeyContext.Default],_model.GlobalTimingHotkeys);
        if(binding is null||binding.CommandId is not (CommandIds.EditFind or CommandIds.FindNext or CommandIds.FindReplace))return;
        if(!HotkeyInputPolicy.MayRoute(binding,gesture,ancestors.Any(input=>input is TextBox)))return;
        args.Handled=true;await _model.Registry.InvokeAsync(binding.CommandId,new(DocumentId,FocusContext:HotkeyContext.Default.ToString()),binding.CommandId==CommandIds.FindNext?ReadRequest():null);
    }
}
