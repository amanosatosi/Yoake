using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Interactivity;
using Yoake.Core.Hotkeys;
using Yoake.UI.Commands;
using Yoake.UI.ViewModels;

namespace Yoake.UI;

public sealed class OptionsWindow : Window
{
    private sealed record CommandChoice(string Id,string Label,bool Registered)
    {
        public override string ToString()=>Label+" · "+Id+(Registered?"":"  (backend not available)");
    }
    private readonly MainWindowViewModel _model;
    private readonly ComboBox _context=new(){Name="HotkeyContext",MinWidth=200};
    private readonly TextBox _filter=new(){Name="HotkeyFilter",PlaceholderText="Filter commands…"};
    private readonly ListBox _commands=new(){Name="HotkeyCommands"};
    private readonly ListBox _bindings=new(){Name="EffectiveHotkeys",MinHeight=72};
    private readonly TextBox _gesture=new(){Name="HotkeyGesture",PlaceholderText="e.g. Ctrl-Alt-K"};
    private readonly TextBox _capture=new(){Name="HotkeyCapture",PlaceholderText="Focus here and press a shortcut",IsReadOnly=true};
    private readonly TextBlock _status=new(){Name="HotkeyStatus",TextWrapping=Avalonia.Media.TextWrapping.Wrap};
    private readonly CheckBox _global=new(){Name="GlobalTimingHotkeys",Content="Global timing keypad overrides (Medusa)"};
    private bool _refreshing;
    private HotkeyContext Context=>(HotkeyContext)(_context.SelectedItem??HotkeyContext.Default);
    private CommandChoice? Choice=>_commands.SelectedItem as CommandChoice;

    public OptionsWindow(MainWindowViewModel model)
    {
        _model=model;Title="Options — Keyboard shortcuts";Name="OptionsDialog";Width=860;Height=620;MinWidth=650;MinHeight=460;
        var names=new NameScope();NameScope.SetNameScope(this,names);
        foreach(var control in new Control[]{_context,_filter,_commands,_bindings,_gesture,_capture,_status,_global})names.Register(control.Name!,control);
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new Grid{Margin=new Thickness(12),RowDefinitions=new("Auto,Auto,*,Auto,Auto"),RowSpacing=8};Content=root;
        var heading=new StackPanel{Orientation=Orientation.Horizontal,Spacing=12};root.Children.Add(heading);
        heading.Children.Add(new TextBlock{Text="Context",VerticalAlignment=VerticalAlignment.Center});heading.Children.Add(_context);heading.Children.Add(_global);
        _context.ItemsSource=Enum.GetValues<HotkeyContext>();_context.SelectedItem=HotkeyContext.Default;
        At(root,_filter,1);
        var body=new Grid{ColumnDefinitions=new("3*,2*"),ColumnSpacing=10};At(root,body,2);body.Children.Add(_commands);
        var edit=new StackPanel{Spacing=8};Grid.SetColumn(edit,1);body.Children.Add(edit);
        edit.Children.Add(new TextBlock{Text="Current bindings",FontWeight=Avalonia.Media.FontWeight.SemiBold});edit.Children.Add(_bindings);
        edit.Children.Add(_gesture);edit.Children.Add(_capture);
        var row=new WrapPanel{Orientation=Orientation.Horizontal};edit.Children.Add(row);
        Button Action(string text,string name,Func<Task> action)
        {
            var button=new Button{Content=text,Name=name,Margin=new Thickness(0,0,5,5)};
            names.Register(name,button);
            button.Click+=async(_,_)=>{await action();};row.Children.Add(button);return button;
        }
        Action("Add","AddHotkey",()=>Modify("add"));Action("Change","ChangeHotkey",()=>Modify("change"));Action("Remove","RemoveHotkey",()=>Modify("remove"));
        Action("Clear command","ClearHotkeys",()=>Apply("settings/hotkeys/set",Choice is {} choice?new HotkeyChange(Context,choice.Id,[]):null));
        Action("Reset command","ResetCommandHotkeys",()=>Apply("settings/hotkeys/reset-command",Choice is {} choice?new HotkeyCommandReset(Context,choice.Id):null));
        Action("Reset context","ResetContextHotkeys",()=>Apply("settings/hotkeys/reset-context",Context));Action("Reset all","ResetAllHotkeys",()=>Apply("settings/hotkeys/reset-all"));
        edit.Children.Add(new TextBlock{Text="Changes apply and save immediately. Each context can bind the same key differently. A conflict leaves existing bindings unchanged.",TextWrapping=Avalonia.Media.TextWrapping.Wrap});
        At(root,_status,3);var close=new Button{Content="Close",HorizontalAlignment=HorizontalAlignment.Right,IsCancel=true};close.Click+=(_,_)=>Close();At(root,close,4);
        _context.SelectionChanged+=(_,_)=>RefreshCommands();_filter.TextChanged+=(_,_)=>RefreshCommands();
        _commands.SelectionChanged+=(_,_)=>RefreshBindings();_bindings.SelectionChanged+=(_,_)=>{if(_bindings.SelectedItem is string key)_gesture.Text=key;};
        _capture.AddHandler(InputElement.KeyDownEvent,(_,args)=>
        {
            args.Handled=true;
            if(args.Key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)return;
            var modifiers=EditorKeyNames.FromModifiers(args.KeyModifiers);
            _gesture.Text=new HotkeyGesture(EditorKeyNames.FromEvent(args),modifiers).Format();_capture.Text=_gesture.Text;
        },RoutingStrategies.Tunnel);
        _global.Click+=async(_,_)=>{if(!_refreshing)await Apply("app/toggle/global_hotkeys");};
        _model.HotkeyProfile.Changed+=ProfileChanged;_model.Registry.StateChanged+=CommandStateChanged;
        Closed+=(_,_)=>{_model.HotkeyProfile.Changed-=ProfileChanged;_model.Registry.StateChanged-=CommandStateChanged;};
        RefreshCommands();
        names.Complete();
    }

    private static void At(Grid parent,Control child,int row){Grid.SetRow(child,row);parent.Children.Add(child);}
    private void ProfileChanged(object? sender,EventArgs args)=>RefreshBindings();
    private void CommandStateChanged(object? sender,EventArgs args)
    {
        if(!Avalonia.Threading.Dispatcher.UIThread.CheckAccess()){Avalonia.Threading.Dispatcher.UIThread.Post(()=>CommandStateChanged(sender,args));return;}
        _refreshing=true;_global.IsChecked=_model.GlobalTimingHotkeys;_refreshing=false;
    }
    private void RefreshCommands()
    {
        var selected=Choice?.Id;var filter=_filter.Text??"";
        var ids=_model.Hotkeys.Bindings.Where(binding=>binding.Context==Context).Select(binding=>binding.CommandId)
            .Concat(_model.Registry.Commands.Select(command=>command.Metadata.Id)).Distinct(StringComparer.Ordinal);
        var choices=ids.Select(id=>_model.Registry.TryGet(id,out var command)&&command is not null
            ?new CommandChoice(id,command.Metadata.Label,true):new CommandChoice(id,id,false))
            .Where(choice=>choice.Id.Contains(filter,StringComparison.OrdinalIgnoreCase)||choice.Label.Contains(filter,StringComparison.OrdinalIgnoreCase))
            .OrderBy(choice=>choice.Label,StringComparer.OrdinalIgnoreCase).ToArray();
        _commands.ItemsSource=choices;_commands.SelectedItem=choices.FirstOrDefault(choice=>choice.Id==selected)??choices.FirstOrDefault();
        RefreshBindings();CommandStateChanged(null,EventArgs.Empty);
    }
    private void RefreshBindings()
    {
        if(!Avalonia.Threading.Dispatcher.UIThread.CheckAccess()){Avalonia.Threading.Dispatcher.UIThread.Post(RefreshBindings);return;}
        var selected=_bindings.SelectedItem as string;
        var keys=Choice is {} choice?_model.HotkeyProfile.EffectiveGestures(choice.Id,Context):[];
        _bindings.ItemsSource=keys;_bindings.SelectedItem=keys.FirstOrDefault(key=>key==selected)??keys.FirstOrDefault();
    }
    private Task Modify(string operation)
    {
        if(Choice is not {} choice){_status.Text="Choose a command.";return Task.CompletedTask;}
        var keys=_model.HotkeyProfile.EffectiveGestures(choice.Id,Context).ToList();
        if(operation is "change" or "remove")
        {
            if(_bindings.SelectedItem is not string selected){_status.Text="Choose a binding.";return Task.CompletedTask;}keys.Remove(selected);
        }
        if(operation!="remove")keys.Add(_gesture.Text??"");
        return Apply("settings/hotkeys/set",new HotkeyChange(Context,choice.Id,keys.ToArray()));
    }
    private async Task Apply(string command,object? parameter=null)
    {
        var success=await _model.Registry.InvokeAsync(command,new(_model.ActiveDocumentId),parameter);
        _status.Text=success?"Saved. Shortcuts and menus updated.":_model.SubtitleStatus;
        RefreshBindings();CommandStateChanged(null,EventArgs.Empty);
    }
}
