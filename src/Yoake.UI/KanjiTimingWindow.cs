using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Yoake.UI.ViewModels;

namespace Yoake.UI;

public sealed class KanjiTimingWindow : Window
{
    private readonly KanjiTimingSession _session;
    private readonly MainWindowViewModel _model;
    private readonly NameScope _names=new();
    private readonly ComboBox _sourceStyle,_destinationStyle;
    private readonly TextBox _source=new() { Name="KanjiSourceLine",IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=70 },_destination=new() { Name="KanjiDestinationLine",IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=70 };
    private readonly ListBox _syllables=new() { Name="KanjiSourceSyllables",SelectionMode=SelectionMode.Multiple,Focusable=true },_characters=new() { Name="KanjiDestinationCharacters",SelectionMode=SelectionMode.Multiple,Focusable=true },_groups=new() { Name="KanjiLinkedGroups" };
    private readonly NumericUpDown _sourceCount=new() { Name="KanjiSourceCount",Minimum=0,Increment=1,FormatString="0",Width=85 },_destinationCount=new() { Name="KanjiDestinationCount",Minimum=0,Increment=1,FormatString="0",Width=85 };
    private readonly TextBlock _status=new() { Name="KanjiTimingStatus",TextWrapping=TextWrapping.Wrap };
    private readonly CheckBox _interpolate=new() { Name="KanjiAutoSuggest",Content="Suggest groups automatically",IsChecked=true,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,0,0) };
    private bool _updating;
    public KanjiTimingWindow(MainWindowViewModel model)
    {
        _model=model;_session=model.BeginKanjiTiming();Name="KanjiTimingDialog";Title="Kanji Timing Copier";Width=900;Height=640;MinWidth=660;MinHeight=440;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        NameScope.SetNameScope(this,_names);
        _sourceStyle=new() { Name="KanjiSourceStyle",ItemsSource=_session.Styles,SelectedIndex=0,MinWidth=140 };
        _destinationStyle=new() { Name="KanjiDestinationStyle",ItemsSource=_session.Styles,SelectedIndex=Math.Min(1,_session.Styles.Length-1),MinWidth=140 };
        var root=new Grid { Margin=new Thickness(14),RowDefinitions=new("Auto,Auto,Auto,*,Auto,Auto,Auto"),RowSpacing=10 };
        var styles=new WrapPanel { Orientation=Orientation.Horizontal };root.Children.Add(styles);
        styles.Children.Add(new TextBlock { Text="Source style",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0) });styles.Children.Add(_sourceStyle);
        styles.Children.Add(new TextBlock { Text="Destination style",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,8,0) });styles.Children.Add(_destinationStyle);
        var start=Button("Start / restart","KanjiStart","start",()=>new KanjiTimingStart(_session,_sourceStyle.SelectedItem as string??"",_destinationStyle.SelectedItem as string??""));start.Margin=new Thickness(12,0,0,0);styles.Children.Add(start);styles.Children.Add(_interpolate);
        ToolTip.SetTip(_interpolate,"Use Aegisub 3.2.2 romaji/kana and nearby-kanji heuristics to select counts. Review every suggested group before linking; this never changes text automatically.");
        var originals=new Grid { ColumnDefinitions=new("*,*"),ColumnSpacing=10 };At(root,originals,1);originals.Children.Add(_source);Grid.SetColumn(_destination,1);originals.Children.Add(_destination);
        var labels=new Grid { ColumnDefinitions=new("*,*,*"),ColumnSpacing=10 };At(root,labels,2);
        foreach(var (text,column) in new[] { ("Source syllables (centiseconds)",0),("Destination characters",1),("Linked groups",2) }){var label=new TextBlock { Text=text,TextWrapping=TextWrapping.Wrap };Grid.SetColumn(label,column);labels.Children.Add(label);}
        var lists=new Grid { ColumnDefinitions=new("*,*,*"),ColumnSpacing=10 };At(root,lists,3);lists.Children.Add(_syllables);Grid.SetColumn(_characters,1);lists.Children.Add(_characters);Grid.SetColumn(_groups,2);lists.Children.Add(_groups);
        var matching=new WrapPanel();At(root,matching,4);
        matching.Children.Add(new TextBlock { Text="Source count",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,6,0) });matching.Children.Add(_sourceCount);
        matching.Children.Add(new TextBlock { Text="Destination count",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,6,0) });matching.Children.Add(_destinationCount);
        matching.Children.Add(Button("Suggest group","KanjiSuggest","suggest"));
        matching.Children.Add(Button("Link group","KanjiLink","link",()=>new KanjiTimingGroup(_session,Count(_sourceCount),Count(_destinationCount))));
        matching.Children.Add(Button("Unlink last","KanjiUnlink","unlink"));matching.Children.Add(Button("Accept line →","KanjiAccept","accept"));
        var navigation=new WrapPanel();At(root,navigation,5);
        navigation.Children.Add(Button("Previous pair","KanjiPrevious","previous"));navigation.Children.Add(Button("Skip source","KanjiSkipSource","skip-source"));navigation.Children.Add(Button("Skip destination","KanjiSkipDestination","skip-destination"));
        ToolTip.SetTip(_syllables,"Click the last item for a group. With either matching list focused: Up/Down changes source count, Left/Right changes destination count, Enter links (or accepts a complete line), Backspace unlinks.");ToolTip.SetTip(_characters,ToolTip.GetTip(_syllables));
        var footer=new Grid { ColumnDefinitions=new("*,Auto,Auto"),ColumnSpacing=8 };At(root,footer,6);footer.Children.Add(_status);
        var apply=Button("Apply accepted lines","KanjiApply","apply");Grid.SetColumn(apply,1);footer.Children.Add(apply);
        var cancel=new Button { Name="KanjiCancel",Content="Cancel / close",IsCancel=true };cancel.Click+=(_,_)=>Close();Grid.SetColumn(cancel,2);footer.Children.Add(cancel);
        ToolTip.SetTip(apply,"Apply only accepted lines in one undo step. Unfinished current groups stay in the matching workspace. Cancel discards unapplied lines.");
        foreach(var control in new Control[] { _sourceStyle,_destinationStyle,_source,_destination,_syllables,_characters,_groups,_sourceCount,_destinationCount,_status,_interpolate,cancel })_names.Register(control.Name!,control);
        _sourceCount.ValueChanged+=(_,_)=>SelectCounts();_destinationCount.ValueChanged+=(_,_)=>SelectCounts();
        foreach(var (list,count) in new[] { (_syllables,_sourceCount),(_characters,_destinationCount) })
            list.AddHandler(PointerPressedEvent,(_,e)=>
            {
                if(!e.GetCurrentPoint(list).Properties.IsLeftButtonPressed||e.Source is not Control control)return;
                var row=control.GetVisualAncestors().Prepend(control).OfType<ListBoxItem>().FirstOrDefault();if(row is null)return;
                var index=list.IndexFromContainer(row);if(index<0)return;
                count.Value=index+1;row.Focus();e.Handled=true;
            },RoutingStrategies.Tunnel);
        _interpolate.IsCheckedChanged+=(_,_)=>{if(_interpolate.IsChecked==true&&_session.Alignment is not null)_=Run("suggest",_session);};
        _session.PropertyChanged+=(_,e)=>
        {
            if(e.PropertyName==nameof(KanjiTimingSession.Status))Refresh();
            else if(e.PropertyName==nameof(KanjiTimingSession.Suggestion)&&_session.Suggestion is {} suggestion)
            { _sourceCount.Value=suggestion.SourceCount;_destinationCount.Value=suggestion.DestinationCount; }
        };_session.Invalidated+=(_,_)=>Close();Closed+=(_,_)=>_session.Dispose();
        AddHandler(KeyDownEvent,async (_,e)=>
        {
            if(e.Key is Key.Enter or Key.Escape&&this.GetVisualDescendants().OfType<TextPresenter>().Any(p=>!string.IsNullOrEmpty(p.PreeditText))){e.Handled=true;return;}
            if(e.Source is not Control control||!control.GetVisualAncestors().Append(control).Any(v=>ReferenceEquals(v,_syllables)||ReferenceEquals(v,_characters))||e.KeyModifiers!=KeyModifiers.None)return;
            e.Handled=e.Key is Key.Up or Key.Down or Key.Left or Key.Right or Key.Enter or Key.Back;
            if(!e.Handled)return;
            try
            {
            switch(e.Key)
            {
                case Key.Up:_sourceCount.Value=Math.Min(_sourceCount.Maximum,(_sourceCount.Value??0)+1);break;
                case Key.Down:_sourceCount.Value=Math.Max(0,(_sourceCount.Value??0)-1);break;
                case Key.Right:_destinationCount.Value=Math.Min(_destinationCount.Maximum,(_destinationCount.Value??0)+1);break;
                case Key.Left:_destinationCount.Value=Math.Max(0,(_destinationCount.Value??0)-1);break;
                case Key.Enter:await Run(_session.Alignment?.Complete==true?"accept":"link",_session.Alignment?.Complete==true?_session:new KanjiTimingGroup(_session,Count(_sourceCount),Count(_destinationCount)));break;
                case Key.Back:await Run("unlink",_session);break;
                default:return;
            }
            }
            catch(ArgumentException error){_status.Text=error.Message;}
            e.Handled=true;
        },RoutingStrategies.Tunnel);
        _names.Complete();Content=root;Refresh();
    }
    private Button Button(string label,string name,string action,Func<object>? parameter=null)
    {
        var button=new Button { Name=name,Content=label,Margin=new Thickness(5,0,0,3) };_names.Register(name,button);
        button.Click+=async (_,_)=>
        {
            try { await Run(action,parameter?.Invoke()??_session); }
            catch(ArgumentException error){_status.Text=error.Message;}
        };return button;
    }
    private async Task Run(string action,object parameter)
    {
        var ok=await _model.Registry.InvokeAsync("tool/time/kanji/"+action,new(_session.DocumentId),parameter);
        if(!ok)_status.Text=_model.SubtitleStatus;
        else if(action is "start" or "link" or "unlink" or "suggest" or "accept" or "skip-source" or "skip-destination" or "previous")
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(()=>{if(IsVisible)_syllables.Focus();},Avalonia.Threading.DispatcherPriority.Background);
    }
    private static int Count(NumericUpDown value)=>value.Value is {} count&&count==decimal.Truncate(count)&&!DataValidationErrors.GetHasErrors(value)?(int)count:throw new ArgumentException("Enter whole source and destination counts.");
    private void Refresh()
    {
        _updating=true;
        try
        {
            _source.Text=_session.SourceLine;_destination.Text=_session.DestinationLine;_status.Text=_session.Status;
            _syllables.ItemsSource=_session.SourceSyllables;_characters.ItemsSource=_session.DestinationCharacters;_groups.ItemsSource=_session.LinkedGroups;
            _sourceCount.Maximum=_session.Alignment?.SourceRemaining??0;_destinationCount.Maximum=_session.Alignment?.DestinationRemaining??0;
            _sourceCount.Value=Math.Min(1,_sourceCount.Maximum);_destinationCount.Value=Math.Min(1,_destinationCount.Maximum);
        }
        finally{_updating=false;}SelectCounts();
        if(_interpolate.IsChecked==true&&_session.Alignment is not null)_=Run("suggest",_session);
    }
    private void SelectCounts()
    {
        if(_updating)return;
        foreach(var (list,count) in new[] { (_syllables,(int)(_sourceCount.Value??0)),(_characters,(int)(_destinationCount.Value??0)) })
        {
            list.SelectedItems?.Clear();foreach(var item in list.Items.Take(count))list.SelectedItems?.Add(item);
        }
    }
    private static void At(Grid grid,Control control,int row){Grid.SetRow(control,row);grid.Children.Add(control);}
}
