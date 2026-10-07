using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
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
    private readonly Button _browse=new(){Name="BrowseFonts",Content="▾",Width=26,Padding=new Thickness(2),MinHeight=26};
    private readonly ListBox _list=new(){Name="InstalledFonts",MaxHeight=300,MinWidth=260};
    private readonly Popup _popup=new(){Placement=PlacementMode.Bottom,IsLightDismissEnabled=true};
    public IReadOnlyList<string> InstalledFamilies=>_fonts;
    public bool IsBrowserOpen=>_popup.IsOpen;
    public string FontName {get=>GetValue(FontNameProperty);set=>SetValue(FontNameProperty,value);}
    public event EventHandler? ValueChanged;
    public FontPicker()
    {
        var panel=new StackPanel();var row=new Grid{ColumnDefinitions=new("*,Auto")};row.Children.Add(_entry);Grid.SetColumn(_browse,1);row.Children.Add(_browse);panel.Children.Add(row);panel.Children.Add(_status);Content=panel;
        _popup.PlacementTarget=this;var browser=new Border{Child=_list,BorderThickness=new Thickness(1),BorderBrush=Brushes.Gray};_popup.Child=browser;
        browser.Bind(Border.BackgroundProperty,this.GetResourceObservable("AppBackgroundBrush"));
        _browse.Click+=(_,_)=>OpenBrowser();ToolTip.SetTip(_browse,"Browse all installed font families (Alt+Down)");
        _entry.KeyDown+=(_,e)=>{if(e.Key==Key.Down){OpenBrowser();_list.Focus();e.Handled=true;}else if(e.Key==Key.Escape)_popup.IsOpen=false;};
        _list.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){Choose();e.Handled=true;}else if(e.Key==Key.Escape){_popup.IsOpen=false;_entry.Focus();e.Handled=true;}};
        _list.DoubleTapped+=(_,_)=>Choose();
        _list.PointerReleased+=(_,_)=>{if(_list.SelectedItem is string)Choose();};
        ToolTip.SetTip(_entry,"Search installed families or type an exact font name. Missing families are preserved. ↓ opens suggestions.");
        _entry.PropertyChanged+=(_,e)=>{if(e.Property==AutoCompleteBox.TextProperty&&!_sync){SetCurrentValue(FontNameProperty,_entry.Text??"");if(_popup.IsOpen)_list.ItemsSource=_fonts.Where(f=>f.Contains(FontName,StringComparison.CurrentCultureIgnoreCase)).ToArray();Refresh();ValueChanged?.Invoke(this,EventArgs.Empty);}};
        AttachedToVisualTree+=async (_,_)=>
        {
            try{_fonts=await Installed.Value;_entry.ItemsSource=_fonts;Refresh();}
            catch(Exception exception){_status.Text="Font list unavailable: "+exception.Message;_status.IsVisible=true;}
        };
        DetachedFromVisualTree+=(_,_)=>_popup.IsOpen=false;
    }
    public void OpenBrowser(){_entry.IsDropDownOpen=false;_list.ItemsSource=_fonts;_list.SelectedItem=_fonts.FirstOrDefault(f=>f.Equals(FontName,StringComparison.OrdinalIgnoreCase));_popup.IsOpen=true;}
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
