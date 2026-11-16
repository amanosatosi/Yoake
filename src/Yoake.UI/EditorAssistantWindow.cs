using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Yoake.Core.Hotkeys;
using Yoake.UI.Commands;
using Yoake.UI.ViewModels;

namespace Yoake.UI;

public sealed class EditorAssistantWindow : Window
{
    private readonly MainWindowViewModel _model;
    private readonly EditorAssistantSession _session;
    private readonly TextBox _original=new() { Name="AssistantOriginal",IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=65,MaxHeight=130 };
    private readonly TextBox _translation=new() { Name="AssistantTranslation",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=80 };
    private readonly AutoCompleteBox _style=new() { Name="AssistantStyle",MinimumPrefixLength=0,FilterMode=AutoCompleteFilterMode.StartsWith,MaxDropDownHeight=220 };
    private readonly ListBox _styles=new() { Name="AssistantStyles" };
    private readonly TextBlock _position=new() { Name="AssistantPosition" };
    private readonly TextBlock _status=new() { Name="AssistantStatus",TextWrapping=TextWrapping.Wrap };
    private readonly string _prefix;
    private bool _refreshing;
    public EditorAssistantMode Mode=>_session.Mode;
    public EditorAssistantWindow(MainWindowViewModel model,EditorAssistantMode mode)
    {
        _model=model;_session=model.BeginAssistant(mode);_prefix=mode==EditorAssistantMode.Translation?"tool/translation_assistant/":"tool/styling_assistant/";
        Name="EditorAssistantDialog";Title=mode+" Assistant";Width=700;Height=570;MinWidth=500;MinHeight=400;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var names=new NameScope();NameScope.SetNameScope(this,names);
        foreach(var control in new Control[] { _original,_translation,_style,_styles,_position,_status })names.Register(control.Name!,control);
        var root=new Grid { Margin=new Thickness(14),RowDefinitions=new("Auto,Auto,Auto,*,Auto,Auto"),RowSpacing=8 };
        root.Children.Add(_position);At(root,_original,1);
        var hint=new TextBlock { Text=mode==EditorAssistantMode.Translation?"Translate the current plain text block. Override tags, drawings and ruby readings are retained. Enter commits and advances; Shift+Enter inserts a line break.":"Choose a style or type its name. Click applies and stays; double-click or Enter applies and advances.",TextWrapping=TextWrapping.Wrap,Opacity=0.8 };At(root,hint,2);
        if(mode==EditorAssistantMode.Translation)At(root,_translation,3);
        else
        {
            var styling=new Grid { RowDefinitions=new("Auto,*"),RowSpacing=8 };styling.Children.Add(_style);At(styling,_styles,1);At(root,styling,3);
            _style.PropertyChanged+=(_,e)=>{if(!_refreshing&&e.Property==AutoCompleteBox.TextProperty)_session.Input=_style.Text??"";};
            void Choose(bool advance)
            {
                if(_styles.SelectedItem is not string style)return;
                _session.Input=style;_=Run(advance?"commit":"preview");_style.Focus();
            }
            bool IsRow(object? source)=>source is ListBoxItem||source is Control control&&control.GetVisualAncestors().OfType<ListBoxItem>().Any();
            _styles.Tapped+=(_,e)=>{if(IsRow(e.Source))Choose(false);};
            _styles.DoubleTapped+=(_,e)=>{if(IsRow(e.Source))Choose(true);};
        }
        _translation.TextChanged+=(_,_)=>{if(!_refreshing)_session.Input=_translation.Text??"";};
        var media=new WrapPanel();At(root,media,4);
        var seek=new CheckBox { Content="Seek video on navigation",IsChecked=true,Margin=new Thickness(0,0,12,4) };seek.IsCheckedChanged+=(_,_)=>_session.SeekVideo=seek.IsChecked==true;media.Children.Add(seek);
        AddButton(media,"Play audio","AssistantAudio",()=>RunMedia("audio/play/selection"));
        AddButton(media,"Play video","AssistantVideo",()=>RunMedia("video/play/line"));
        var footer=new StackPanel { Spacing=7 };At(root,footer,5);footer.Children.Add(_status);
        var actions=new WrapPanel();footer.Children.Add(actions);
        AddButton(actions,"Previous","AssistantPrevious",()=>Run("prev"));AddButton(actions,"Next","AssistantNext",()=>Run("next"));
        if(mode==EditorAssistantMode.Translation)AddButton(actions,"Insert Original","AssistantInsertOriginal",()=>Run("insert_original"));
        AddButton(actions,"Commit and stay","AssistantPreview",()=>Run("preview"));AddButton(actions,"Commit and next","AssistantCommit",()=>Run("commit"));
        AddButton(actions,"Reload","AssistantReload",()=>{_session.Reload();return Task.CompletedTask;});
        AddButton(actions,"Close","AssistantClose",()=>{Close();return Task.CompletedTask;});Content=root;
        void AddButton(Panel panel,string label,string name,Func<Task> action)
        {
            var button=new Button { Name=name,Content=label,Margin=new Thickness(0,0,6,4) };names.Register(name,button);panel.Children.Add(button);button.Click+=async (_,_)=>await action();
        }
        _session.PropertyChanged+=SessionChanged;_session.Invalidated+=Invalidated;
        Closed+=(_,_)=>{_session.PropertyChanged-=SessionChanged;_session.Invalidated-=Invalidated;_session.Dispose();};
        AddHandler(KeyDownEvent,async (_,e)=>
        {
            var composing=this.GetVisualDescendants().OfType<TextPresenter>().Any(p=>!string.IsNullOrEmpty(p.PreeditText));
            if(composing) { if(e.Key is Key.Enter or Key.Escape)e.Handled=true;return; }
            if(e.Key==Key.Escape) { Close();e.Handled=true;return; }
            var context=Mode==EditorAssistantMode.Translation?HotkeyContext.Translation:HotkeyContext.Styling;
            var binding=model.Hotkeys.Resolve(new(EditorKeyNames.FromEvent(e),EditorKeyNames.FromModifiers(e.KeyModifiers)),[context]);
            if(binding is null)return;
            var id=binding.CommandId;
            if(id.StartsWith(_prefix,StringComparison.Ordinal)) { e.Handled=true;await Run(id[_prefix.Length..]); }
            else if(id is "grid/line/next" or "grid/line/previous") { e.Handled=true;await Run(id=="grid/line/next"?"next":"prev"); }
            else if(id is "audio/play/selection" or "video/play/line") { e.Handled=true;await RunMedia(id); }
        },RoutingStrategies.Tunnel);
        Opened+=(_,_)=>{if(mode==EditorAssistantMode.Translation)_translation.Focus();else _style.Focus();};
        Refresh();names.Complete();
    }
    private void Invalidated(object? sender,EventArgs args)=>Close();
    private void SessionChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs args)=>Refresh();
    private void Refresh()
    {
        _refreshing=true;
        try
        {
            _original.Text=Mode==EditorAssistantMode.Translation?_session.Original:_session.ContextText;
            if(_translation.Text!=_session.Input)_translation.Text=_session.Input;
            if(_style.Text!=_session.Input)_style.Text=_session.Input;
            var styles=_session.Styles.ToArray();
            if(!(_styles.ItemsSource?.Cast<string>().SequenceEqual(styles)??false)) { _styles.ItemsSource=styles;_style.ItemsSource=styles; }
            if(Mode==EditorAssistantMode.Styling)_styles.SelectedItem=styles.FirstOrDefault(s=>s==_session.Input);
            _position.Text=_session.Position;_status.Text=_session.Status;
        }
        finally { _refreshing=false; }
    }
    private async Task Run(string action)
    {
        if(action=="insert_original") { _session.InputSelectionStart=_translation.SelectionStart;_session.InputSelectionEnd=_translation.SelectionEnd; }
        if(!await _model.Registry.InvokeAsync(_prefix+action,new(_session.DocumentId),_session))_status.Text=_model.SubtitleStatus;
        else if(action=="insert_original") { _translation.SelectionStart=_session.InputSelectionStart;_translation.SelectionEnd=_session.InputSelectionEnd;_translation.CaretIndex=_session.InputSelectionEnd; }
    }
    private async Task RunMedia(string id)
    {
        if(!await _model.Registry.InvokeAsync(id,new(_session.DocumentId)))_status.Text=_model.SubtitleStatus;
    }
    private static void At(Grid parent,Control child,int row) { Grid.SetRow(child,row);parent.Children.Add(child); }
}
