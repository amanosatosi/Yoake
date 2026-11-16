using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yoake.Core.Settings;
using Yoake.Core.Logging;
using Yoake.UI;
using Yoake.UI.Controls;
using Yoake.UI.ViewModels;

namespace Yoake.App;

// Runs against the shipping controls in a native window, then repeats in a
// second process using the same profile to verify actual restart persistence.
internal sealed class OptionsUiAcceptanceVerification(IClassicDesktopStyleApplicationLifetime desktop,
    MainWindow window, MainWindowViewModel model, StartupOptions options)
{
    private bool _shortcutSearchChecked;
    private readonly DispatcherTimer _timer=new() { Interval=TimeSpan.FromMilliseconds(180) };
    private OptionsWindow? _dialog;
    private int _stage;private bool _ticking;
    private double? _initialSize;
    private string _source="";
    private T Get<T>(string name) where T:Control => _dialog!.FindControl<T>(name) ?? throw new InvalidOperationException("Missing Options control: "+name);
    public void Start() { _timer.Tick+=Tick;window.Opened+=(_,_)=>_timer.Start(); }
    private async void Tick(object? sender,EventArgs args)
    {
        if(_ticking)return;_ticking=true;options.MarkStartupVerified();
        try
        {
            switch(_stage++)
            {
                case 0:
                    _initialSize=model.UiFontSize;
                    if(File.Exists(Path.Combine(options.ProfileDirectory!,"options-tested")))Require(_initialSize==15,"Font size must persist across process restart.");
                    _source=model.ActiveEditor!.Document.Serialize();
                    Open();return;
                case 1:
                    if(!_shortcutSearchChecked){Get<TextBox>("OptionsSearch").Text="assistant translation";_stage=11;return;}
                    Require(_dialog!.TryGetPlatformHandle() is { Handle:not 0 },"Options needs a real native window.");
                    Get<ListBox>("OptionsCategories").SelectedItem="Appearance";
                    Get<NumericUpDown>("UiFontSize").Value=24;Get<FontPicker>("UiFontFamily").OpenBrowser();return;
                case 2:
                    if(Get<FontPicker>("UiFontFamily").InstalledFamilies.Count==0){_stage--;return;}
                    Require(Get<FontPicker>("UiFontFamily").BrowserList.Items.Count>0,"The installed-font browser must populate real families.");
                    Get<FontPicker>("UiFontFamily").CloseBrowser();
                    Require((double)Application.Current!.Resources["YoakeUiFontSize"]! == 24,"Font preview must update application resources.");
                    Require(model.UiFontSize==_initialSize,"Preview must not publish settings.");
                    Require(window.FindControl<MenuItem>("EditMenu")!.FontSize==24,"Menus must receive live typography.");
                    Require(window.FindControl<MenuItem>("EditMenu")!.Bounds.Height>=26,"The menu row must reflow with enlarged fonts.");
                    var gridRow=window.FindControl<ListBox>("SubtitleRows")!.ContainerFromIndex(0) as Control;
                    Require(gridRow is { Bounds.Height:>=40 },"Subtitle grid containers must reflow with enlarged fonts.");
                    Require(window.FindControl<TextBox>("LineStart")!.Bounds.Width>=180,"Timing fields must expand with UI typography.");
                    _dialog!.Width=700;_dialog.Height=400;return;
                case 3:
                    foreach(var name in new[] { "RestoreOptionsDefaults","OptionsApply","OptionsOk","OptionsCancel" })
                    {
                        var button=Get<Button>(name);var origin=button.TranslatePoint(default,_dialog!);
                        Require(button.IsEffectivelyVisible&&button.Bounds.Width>0&&origin is {} point&&point.X>=0&&point.Y>=0&&point.X+button.Bounds.Width<=_dialog!.ClientSize.Width+.5&&point.Y+button.Bounds.Height<=_dialog.ClientSize.Height+.5,
                            "Every footer action must fit inside the compact client area at 24pt: "+name+" · "+origin+" · "+button.Bounds+" · "+_dialog!.ClientSize+" · root "+(_dialog.Content as Control)?.Bounds+" · children "+string.Join(";",(_dialog.Content as Grid)!.Children.Select(c=>c.GetType().Name+" "+c.Bounds+" desired "+c.DesiredSize))+" · default font "+Avalonia.Media.FontManager.Current.DefaultFontFamily.Name);
                    }
                    Require(_dialog!.ClientSize.Height<=400&&Get<ScrollViewer>("OptionsContent").Viewport.Height>=80,"Compact Options must leave a usable scrolling preference viewport.");
                    Press(Get<Button>("OptionsCancel"));return;
                case 4:
                    var appliedFamily=Application.Current!.Resources["YoakeUiFontFamily"];
                    Get<FontPicker>("UiFontFamily").FontName="Yoake discarded late preview 日本語";Get<NumericUpDown>("UiFontSize").Value=24;
                    Require(ReferenceEquals(appliedFamily,Application.Current.Resources["YoakeUiFontFamily"]),"Closed Options controls must not overwrite the restored family through a late update.");
                    Require((double)Application.Current!.Resources["YoakeUiFontSize"]! == (_initialSize??12),"Cancel restores the applied font.");
                    Require(model.UiFontSize==_initialSize,"Cancel must not save the font.");
                    Open();return;
                case 5:
                    Get<TextBox>("OptionsSearch").Text="lead-out";return;
                case 6:
                    Require(Get<NumericUpDown>("AudioLeadOut").IsEffectivelyVisible,"Search must find individual settings across categories.");
                    Require(!Get<NumericUpDown>("UiFontSize").IsEffectivelyVisible,"Search must filter unrelated settings.");
                    Get<TextBox>("OptionsSearch").Text="";Get<ListBox>("OptionsCategories").SelectedItem="Keyboard shortcuts";
                    Get<CheckBox>("GlobalTimingHotkeysEditor").IsChecked=true;
                    Get<TextBox>("OptionsSearch").Text="global";
                    Require(Get<CheckBox>("GlobalTimingHotkeys").IsChecked==true,"The searchable setting must share the shortcut workspace draft.");
                    Get<TextBox>("OptionsSearch").Text="";Get<ListBox>("OptionsCategories").SelectedItem="Recovery";
                    Get<NumericUpDown>("AutosaveInterval").Value=90;
                    Get<ListBox>("OptionsCategories").SelectedItem="Appearance";
                    Get<NumericUpDown>("UiFontSize").Value=null;Press(Get<Button>("OptionsApply"));
                    Require(model.UiFontSize==_initialSize,"An incomplete numeric setting must not partially save any category.");
                    Get<NumericUpDown>("UiFontSize").Value=15;Get<ComboBox>("ThemePreference").SelectedItem=ThemePreference.Dark;
                    Get<FontPicker>("UiFontFamily").FontName="Yoake missing test font 日本語";
                    Press(Get<Button>("OptionsApply"));return;
                case 7:
                    Require(model.UiFontSize==15&&model.AutosaveIntervalSeconds==90&&model.GlobalTimingHotkeys,"Apply must publish all categories together.");
                    Require(Application.Current!.ActualThemeVariant==Avalonia.Styling.ThemeVariant.Dark,"Applying dark appearance must update the real native window theme.");
                    Require(model.UiFontFamily=="Yoake missing test font 日本語","Missing families must retain the requested name with fallback rendering.");
                    var saved=new SettingsStore(Path.Combine(options.ProfileDirectory!,"settings.json")).Load();
                    Require(saved.UiFontSize==15&&saved.AutosaveIntervalSeconds==90,"Apply must persist all categories.");
                    Press(Get<Button>("RestoreOptionsDefaults"));return;
                case 8:
                    Require(Get<NumericUpDown>("UiFontSize").Value==12,"Restore category defaults resets appearance draft.");
                    Get<ListBox>("OptionsCategories").SelectedItem="Recovery";
                    Require(Get<NumericUpDown>("AutosaveInterval").Value==90,"Restore defaults must preserve other categories.");
                    Get<TextBox>("RecoveryDirectory").Focus();return;
                case 9:
                    var presenter=Get<TextBox>("RecoveryDirectory").GetVisualDescendants().OfType<TextPresenter>().Single();
                    presenter.PreeditText="မြန်မာ";Key(Get<TextBox>("RecoveryDirectory"),Avalonia.Input.Key.Escape);
                    Require(_dialog!.IsVisible&&presenter.PreeditText=="မြန်မာ","IME preedit Escape must not close Options.");
                    presenter.PreeditText=null;Press(Get<Button>("OptionsCancel"));return;
                case 10:
                    Require(model.UiFontSize==15&&model.AutosaveIntervalSeconds==90,"Cancel after Apply keeps the applied values and discards later resets.");
                    Require(_source==model.ActiveEditor!.Document.Serialize(),"Settings must preserve exact subtitle source.");
                    File.WriteAllText(Path.Combine(options.ProfileDirectory!,"options-tested"),"passed");
                    _timer.Stop();options.FinishVerification("PASS: native Options draft, cross-setting and shortcut-command search, compact 700×400/24pt layout, monitor working-area sizing, Unicode font preview, fallback, Apply/Cancel, scoped defaults, IME preedit and restart persistence.\n");
                    StartupDiagnostics.Complete();desktop.Shutdown(0);return;
                case 11:
                    Require(Get<Button>("OptionsShortcutSearch").IsEffectivelyVisible,"Global search must discover individual shortcut commands by their words.");Press(Get<Button>("OptionsShortcutSearch"));return;
                case 12:
                    Require(Get<ListBox>("OptionsCategories").SelectedItem as string=="Keyboard shortcuts"&&Get<TextBox>("HotkeyFilter").Text=="assistant translation","Shortcut search must navigate without losing the query.");
                    Require(Get<ListBox>("HotkeyCommands").Items.Cast<object>().Any(item=>item.ToString()!.EndsWith(" · tool/translation_assistant",StringComparison.Ordinal)),"Multi-word shortcut filtering must find the actual Translation Assistant command.");
                    _shortcutSearchChecked=true;_stage=1;return;
            }
        }
        catch(Exception error) { _timer.Stop();Program.Fatal("Options acceptance stage "+_stage,error);desktop.Shutdown(1); }
        finally { _ticking=false; }
        await Task.CompletedTask;
    }
    private void Open() { _dialog=new(model);_dialog.Show(window); }
    private static void Press(Control control) { Require(control.Focus(),"Options action must be focusable.");Key(control,Avalonia.Input.Key.Enter); }
    private static void Key(Control control,Key key) => control.RaiseEvent(new KeyEventArgs { RoutedEvent=InputElement.KeyDownEvent,Key=key,Source=control });
    private static void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
}
