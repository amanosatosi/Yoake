using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Styling;
using Yoake.UI.Icons;
using Yoake.Core.Subtitles;

namespace Yoake.UI.Controls;

public sealed class FontPicker : UserControl
{
    public static readonly StyledProperty<string> FontNameProperty=AvaloniaProperty.Register<FontPicker,string>(nameof(FontName),"",defaultBindingMode:BindingMode.TwoWay);
    private static readonly Lazy<Task<string[]>> Installed = new(()=>Task.Run(()=>FontManager.Current.SystemFonts.Select(f=>f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToArray()));
    private readonly AutoCompleteBox _entry=new(){MinimumPrefixLength=0,MinimumPopulateDelay=TimeSpan.FromMilliseconds(100),MaxDropDownHeight=260,FilterMode=AutoCompleteFilterMode.Contains,Padding=new Thickness(4,1),MinWidth=120,MinHeight=26};
    private readonly TextBlock _status=new(){FontSize=10,Opacity=0.8,IsVisible=false};
    private string[] _fonts=[];
    private bool _sync;
    private bool _browserFocusPending;
    private readonly Button _browse=new(){Name="BrowseFonts",Width=26,Padding=new Thickness(2),MinHeight=26};
    private readonly ListBox _list=new(){Name="InstalledFonts",MaxHeight=300,MinWidth=260};
    private readonly Popup _popup=new(){Placement=PlacementMode.Bottom,IsLightDismissEnabled=true};
    public IReadOnlyList<string> InstalledFamilies=>_fonts;
    public bool IsBrowserOpen=>_popup.IsOpen;
    public string FontName {get=>GetValue(FontNameProperty);set=>SetValue(FontNameProperty,value);}
    public event EventHandler? ValueChanged;
    public FontPicker()
    {
        _list.Styles.Add(new Style(s=>s.OfType<ListBoxItem>()){Setters={new Setter(ListBoxItem.HeightProperty,26d),new Setter(ListBoxItem.MinHeightProperty,26d),new Setter(ListBoxItem.PaddingProperty,new Thickness(6,0)),new Setter(ListBoxItem.VerticalContentAlignmentProperty,Avalonia.Layout.VerticalAlignment.Center)}});
        var panel=new StackPanel();var row=new Grid{ColumnDefinitions=new("*,Auto")};row.Children.Add(_entry);Grid.SetColumn(_browse,1);row.Children.Add(_browse);panel.Children.Add(row);panel.Children.Add(_status);Content=panel;
        var arrow=new PathIcon{Width=12,Height=12,Data=IconGeometries.StepDown};arrow.Bind(PathIcon.ForegroundProperty,this.GetResourceObservable("IconForegroundBrush"));_browse.Content=arrow;
        _popup.PlacementTarget=this;var browser=new Border{Child=_list,BorderThickness=new Thickness(1),BorderBrush=Brushes.Gray};_popup.Child=browser;panel.Children.Add(_popup);
        browser.Bind(Border.BackgroundProperty,this.GetResourceObservable("AppBackgroundBrush"));
        _browse.Click+=(_,_)=>{if(_popup.IsOpen)CloseBrowser();else OpenBrowser();};ToolTip.SetTip(_browse,"Browse all installed font families (Alt+Down)");
        _entry.KeyDown+=(_,e)=>{if(e.Key==Key.Down&&(e.KeyModifiers is KeyModifiers.None or KeyModifiers.Alt)){OpenBrowser();_list.Focus();e.Handled=true;}else if(e.Key==Key.Escape)_popup.IsOpen=false;};
        _list.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){Choose();e.Handled=true;}else if(e.Key==Key.Escape){_popup.IsOpen=false;_entry.Focus();e.Handled=true;}};
        _list.LayoutUpdated+=(_,_)=>
        {
            // ListBox navigation starts from a realized, focused row. Opening
            // at an off-screen family needs a layout pass after ScrollIntoView.
            FocusBrowserRow();
        };
        _list.AddHandler(PointerReleasedEvent,(_,e)=>
        {
            if(e.InitialPressMouseButton!=MouseButton.Left||e.Source is not Control source)return;
            // Scrollbar/thumb releases must keep the browser open. Only a
            // font row represents a choice, including its text/template children.
            if(source is ListBoxItem||source.GetVisualAncestors().OfType<ListBoxItem>().Any())Choose();
        },Avalonia.Interactivity.RoutingStrategies.Bubble,handledEventsToo:true);
        ToolTip.SetTip(_entry,"Search installed families or type an exact font name. Missing families are preserved. ↓ opens suggestions.");
        _entry.PropertyChanged+=(_,e)=>{if(e.Property==AutoCompleteBox.TextProperty&&!_sync){SetCurrentValue(FontNameProperty,_entry.Text??"");if(_popup.IsOpen){if(_fonts.Length==0)PopulateBrowser();else _list.ItemsSource=_fonts.Where(f=>f.Contains(FontName,StringComparison.CurrentCultureIgnoreCase)).ToArray();}Refresh();ValueChanged?.Invoke(this,EventArgs.Empty);}};
        AttachedToVisualTree+=async (_,_)=>
        {
            try{_fonts=await Installed.Value;_entry.ItemsSource=_fonts;if(_popup.IsOpen)PopulateBrowser();Refresh();}
            catch(Exception exception){_status.Text="Font list unavailable: "+exception.Message;_status.IsVisible=true;}
        };
        DetachedFromVisualTree+=(_,_)=>_popup.IsOpen=false;
    }
    private void PopulateBrowser()
    {
        _list.ItemsSource=_fonts.Length>0?_fonts:new[]{string.IsNullOrWhiteSpace(FontName)?"Loading installed fonts…":FontName};
        _list.SelectedItem=_fonts.FirstOrDefault(f=>f.Equals(FontName,StringComparison.OrdinalIgnoreCase))??(_fonts.Length==0?_list.ItemsSource.Cast<string>().First():null);
        if(_list.SelectedItem is {} selected)_list.ScrollIntoView(selected);
    }
    public ListBox BrowserList=>_list;
    private void FocusBrowserRow()
    {
        if(!_browserFocusPending||!_popup.IsOpen)return;
        if(_list.ContainerFromIndex(Math.Max(0,_list.SelectedIndex)) is ListBoxItem row&&row.Focus())_browserFocusPending=false;
    }
    public void OpenBrowser(){_entry.IsDropDownOpen=false;PopulateBrowser();_browserFocusPending=true;_popup.IsOpen=true;Avalonia.Threading.Dispatcher.UIThread.Post(()=>{if(_popup.IsOpen){_browserFocusPending=true;_list.Focus();if(_list.SelectedItem is {} selected)_list.ScrollIntoView(selected);FocusBrowserRow();}});}
    public void CloseBrowser(){_browserFocusPending=false;_popup.IsOpen=false;}
    private void Choose(){if(_list.SelectedItem is not string family)return;SetCurrentValue(FontNameProperty,family);ValueChanged?.Invoke(this,EventArgs.Empty);_popup.IsOpen=false;_entry.Focus();}
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if(change.Property==FontNameProperty){_sync=true;_entry.Text=FontName;_sync=false;Refresh();}
    }
    private void Refresh()
    {
        _status.Text=_fonts.Length==0?"":FontAvailability.IsInstalled(FontName,_fonts)?"":"Unavailable family; exact name retained";
        _status.IsVisible=!string.IsNullOrEmpty(_status.Text);
    }
}
