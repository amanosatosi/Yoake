using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
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
    public string FontName {get=>GetValue(FontNameProperty);set=>SetValue(FontNameProperty,value);}
    public event EventHandler? ValueChanged;
    public FontPicker()
    {
        var panel=new StackPanel();panel.Children.Add(_entry);panel.Children.Add(_status);Content=panel;
        ToolTip.SetTip(_entry,"Search installed families or type an exact font name. Missing families are preserved. ↓ opens suggestions.");
        _entry.PropertyChanged+=(_,e)=>{if(e.Property==AutoCompleteBox.TextProperty&&!_sync){SetCurrentValue(FontNameProperty,_entry.Text??"");Refresh();ValueChanged?.Invoke(this,EventArgs.Empty);}};
        AttachedToVisualTree+=async (_,_)=>
        {
            try{_fonts=await Installed.Value;_entry.ItemsSource=_fonts;Refresh();}
            catch(Exception exception){_status.Text="Font list unavailable: "+exception.Message;_status.IsVisible=true;}
        };
    }
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
