using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Yoake.Core.Hotkeys;
using Yoake.Core.Settings;
using Yoake.Core.Subtitles;
using Yoake.UI.Controls;
using Yoake.UI.Commands;
using Yoake.UI.ViewModels;

namespace Yoake.UI;

public sealed class OptionsWindow : Window
{
    private sealed record CommandChoice(string Id, string Label)
    {
        public override string ToString() => Label + " · " + Id;
    }
    private sealed record PreferenceRow(string Category, string Search, Control View, Action Reload, Action Reset);
    private readonly MainWindowViewModel _model;
    private AppSettings _baseline, _draft;
    private HotkeyProfile _hotkeys;
    private readonly List<PreferenceRow> _rows = [];
    private readonly Dictionary<(string Category,string Group),(Border Frame,StackPanel Rows)> _groups = new();
    private readonly NameScope _names = new();
    private readonly ListBox _navigation = new() { Name = "OptionsCategories", MinWidth = 155 };
    private readonly TextBox _search = new() { Name = "OptionsSearch", PlaceholderText = "Search all settings…" };
    private readonly TextBlock _status = new() { Name = "OptionsStatus", TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _form = new() { Spacing = 8, Margin = new Thickness(10, 8) };
    private readonly ComboBox _context = new() { Name = "HotkeyContext", MinWidth = 140 };
    private readonly TextBox _filter = new() { Name = "HotkeyFilter", PlaceholderText = "Filter commands…" };
    private readonly ListBox _commands = new() { Name = "HotkeyCommands" };
    private readonly ListBox _bindings = new() { Name = "EffectiveHotkeys", MaxHeight = 110, MinHeight = 55 };
    private readonly TextBox _gesture = new() { Name = "HotkeyGesture", IsVisible = false };
    private readonly TextBox _capture = new() { Name = "HotkeyCapture", PlaceholderText = "Press a shortcut here", IsReadOnly = true };
    private readonly TextBlock _hotkeyStatus = new() { Name = "HotkeyStatus", TextWrapping = TextWrapping.Wrap };
    private bool _loading, _applying,_closed;
    private CheckBox? _globalHotkeys;
    private HotkeyContext Context => (HotkeyContext)(_context.SelectedItem ?? HotkeyContext.Default);
    private CommandChoice? Choice => _commands.SelectedItem as CommandChoice;

    public OptionsWindow(MainWindowViewModel model)
    {
        _model = model; _baseline = _draft = model.CaptureOptions();
        _hotkeys = new(model.HotkeyProfile.Defaults, _draft.HotkeyOverrides);
        Name = "OptionsDialog"; Title = "Options"; Width = 820; Height = 580; MinWidth = 620; MinHeight = 360;
        ShowInTaskbar=false;
        this.Bind(BackgroundProperty,this.GetResourceObservable("AppBackgroundBrush"));
        _navigation.Bind(BackgroundProperty,this.GetResourceObservable("PanelBrush"));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        NameScope.SetNameScope(this, _names);
        Register(_navigation); Register(_search); Register(_status);
        var defaults = new AppSettings().Normalize();
        ChoiceSetting("Appearance", "Theme", "Use the system appearance, light or dark colors.", "ThemePreference", Enum.GetValues<ThemePreference>(), s => s.Theme, (s,v) => s with { Theme=v }, defaults);
        var font = new FontPicker { Name="UiFontFamily", HorizontalAlignment=HorizontalAlignment.Stretch };
        Add("Appearance", "Application UI font", "Search installed families or enter a name. Blank means System Default. Missing fonts fall back; subtitle styles and rendered ASS are independent.", font,
            () => font.FontName=_draft.UiFontFamily??"", () => _draft=_draft with { UiFontFamily=null });
        font.PropertyChanged += (_,change) => { if (change.Property==FontPicker.FontNameProperty&&!_loading) { _draft=_draft with { UiFontFamily=font.FontName }; PreviewFont(); } };
        Number("Appearance", "UI font size", "Interface size in device independent pixels (9–24).", "UiFontSize", 9,24, s=>(decimal)(s.UiFontSize??12), (s,v)=>s with { UiFontSize=(double)v }, defaults);
        var sample = new TextBlock { Text="Yoake — 日本語　မြန်မာစာ\nMixed scripts: á こんにちは မင်္ဂလာပါ", TextWrapping=TextWrapping.Wrap };
        Add("Appearance", "Live preview", "Font changes preview across the application. Cancel restores the last applied font.", sample, ()=>{}, ()=>{});
        var resetFont = new Button { Name="ResetUiFont", Content="System Default" };
        resetFont.Click += (_,_) => { _draft=_draft with { UiFontFamily=null, UiFontSize=null }; Reload(); };
        Add("Appearance", "Reset application font", "Restore the default interface family and size.", resetFont, ()=>{}, ()=>{});
        Toggle("General", "Show toolbar", "Keep the command toolbar visible.", "ToolbarVisible", s=>s.ToolbarVisible==true,(s,v)=>s with { ToolbarVisible=v }, defaults);
        Toggle("Editor", "Better View", "Show hard line breaks and compact motion data in the subtitle editor.", "BetterView", s=>s.BetterView==true,(s,v)=>s with { BetterView=v }, defaults);
        Toggle("Editor", "Show Original", "Display the original line text beside translation controls.", "ShowOriginal", s=>s.ShowOriginal,(s,v)=>s with { ShowOriginal=v }, defaults);
        ChoiceSetting("Subtitle grid", "Override tag display", "Show full tags, simplified tags or hide tags in the grid. Source is preserved.", "GridTagDisplay", Enum.GetValues<GridTagDisplayMode>(),s=>s.GridTagDisplay!.Value,(s,v)=>s with { GridTagDisplay=v }, defaults);
        Toggle("Audio", "Default mouse wheel to zoom", "Zoom with the wheel; Control reverses the wheel action.", "AudioWheelZoom", s=>s.AudioWheelZoom??false,(s,v)=>s with { AudioWheelZoom=v }, defaults);
        Toggle("Audio", "Lock scroll on cursor", "Follow the playback cursor near the display edges.", "AudioLockScroll", s=>s.AudioLockScroll??false,(s,v)=>s with { AudioLockScroll=v }, defaults);
        Toggle("Audio", "Auto-focus on mouse over", "Give audio keyboard focus when the pointer enters.", "AudioAutoFocus", s=>s.AudioAutoFocus??false,(s,v)=>s with { AudioAutoFocus=v }, defaults);
        Number("Audio", "Marker drag-start sensitivity (px)", "Distance in logical pixels at which a marker can be grabbed.", "AudioMarkerSensitivity",1,15,s=>s.AudioMarkerSensitivity??8,(s,v)=>s with { AudioMarkerSensitivity=(int)v }, defaults);
        Number("Audio", "Line boundary thickness (px)", "Thickness of start and end markers in logical pixels.", "AudioBoundaryThickness",1,5,s=>s.AudioBoundaryThickness??2,(s,v)=>s with { AudioBoundaryThickness=(int)v }, defaults);
        ChoiceSetting("Audio", "Show inactive lines", "Display no inactive lines, the previous line, the previous and next lines, or all lines.", "AudioInactiveLines",Enum.GetValues<AudioInactiveLineMode>(),s=>s.AudioInactiveLines??AudioInactiveLineMode.All,(s,v)=>s with { AudioInactiveLines=v }, defaults);
        Toggle("Audio", "Include commented inactive lines", "Include comment events in inactive line markers.", "AudioInactiveComments", s=>s.AudioInactiveComments??false,(s,v)=>s with { AudioInactiveComments=v }, defaults);
        Toggle("Audio", "Keyframes in dialogue mode", "Draw video keyframes during dialogue timing.", "AudioDialogueKeyframes", s=>s.AudioDialogueKeyframes??true,(s,v)=>s with { AudioDialogueKeyframes=v }, defaults);
        Toggle("Audio", "Keyframes in karaoke mode", "Draw video keyframes during karaoke timing.", "AudioKaraokeKeyframes", s=>s.AudioKaraokeKeyframes??true,(s,v)=>s with { AudioKaraokeKeyframes=v }, defaults);
        Toggle("Audio", "Cursor time", "Display the time under the mouse when not playing.", "AudioCursorTime", s=>s.AudioCursorTime??true,(s,v)=>s with { AudioCursorTime=v }, defaults);
        Toggle("Audio", "Video position", "Show the current video position when not playing.", "AudioVideoPosition", s=>s.AudioVideoPosition??true,(s,v)=>s with { AudioVideoPosition=v }, defaults);
        Toggle("Audio", "Seconds boundaries", "Draw full-second guides when there is enough space.", "AudioSecondsBoundaries", s=>s.AudioSecondsBoundaries??true,(s,v)=>s with { AudioSecondsBoundaries=v }, defaults);
        Number("Audio", "Karaoke label font size", "Font size of the syllable labels in the audio display.", "AudioLabelSize",9,48,s=>s.AudioLabelSize??9,(s,v)=>s with { AudioLabelSize=(int)v }, defaults);
        Toggle("Audio", "Snap markers by default", "Snap dialogue boundaries to inactive markers, keyframes and seconds. Shift reverses snapping when dragging one marker.", "AudioSnap", s=>s.AudioSnap??false,(s,v)=>s with { AudioSnap=v }, defaults);
        Number("Audio", "Maximum snap distance (px)", "Maximum distance from a candidate marker in logical pixels.", "AudioSnapDistance",0,25,s=>s.AudioSnapDistance??8,(s,v)=>s with { AudioSnapDistance=(int)v }, defaults);
        var audioFont=new FontPicker{Name="AudioLabelFont"};
        Add("Audio","Karaoke label font family","Independent font family for audio syllable labels.",audioFont,()=>audioFont.FontName=_draft.AudioLabelFont??"Verdana",()=>_draft=_draft with {AudioLabelFont=defaults.AudioLabelFont});
        audioFont.PropertyChanged+=(_,change)=>{if(change.Property==FontPicker.FontNameProperty&&!_loading)_draft=_draft with {AudioLabelFont=audioFont.FontName};};
        ChoiceSetting("Advanced audio", "Spectrum quality", "Controls FFT size and viewport resolution.", "SpectrumQuality",Enum.GetValues<SpectrumQualityMode>(),s=>s.SpectrumQuality??SpectrumQualityMode.Better,(s,v)=>s with { SpectrumQuality=v }, defaults);
        ChoiceSetting("Advanced audio", "Frequency mapping", "Controls frequency distribution vertically.", "SpectrumFrequency",Enum.GetValues<SpectrumFrequencyMode>(),s=>s.SpectrumFrequency??SpectrumFrequencyMode.Linear,(s,v)=>s with { SpectrumFrequency=v }, defaults);
        Number("Advanced audio", "Spectrum cache memory max (MiB)", "Maximum retained spectrum tile pixel data.", "SpectrumCacheMiB",2,1024,s=>s.SpectrumCacheMiB??128,(s,v)=>s with { SpectrumCacheMiB=(int)v }, defaults);
        Toggle("Video", "Show keyframes in slider", "Show markers without changing keyframe navigation.", "VideoSliderKeyframes", s=>s.VideoSliderKeyframes??true,(s,v)=>s with { VideoSliderKeyframes=v }, defaults);
        Toggle("Video", "Only show visual tools when mouse is over video", "Hide tool overlays when the pointer leaves; active gestures stay visible.", "VideoVisualAutohide", s=>s.VideoVisualAutohide??false,(s,v)=>s with { VideoVisualAutohide=v }, defaults);
        ChoiceSetting("Video", "Scrolling on video display", "Select the ordinary wheel action.", "VideoScroll",Enum.GetValues<VideoScrollAction>(),s=>s.VideoScroll??VideoScrollAction.Resize,(s,v)=>s with { VideoScroll=v }, defaults);
        ChoiceSetting("Video", "Ctrl+scrolling on video display", "Select the Control-wheel action.", "VideoCtrlScroll",Enum.GetValues<VideoScrollAction>(),s=>s.VideoCtrlScroll??VideoScrollAction.Zoom,(s,v)=>s with { VideoCtrlScroll=v }, defaults);
        ChoiceSetting("Video", "Shift+scrolling on video display", "Select the Shift-wheel action.", "VideoShiftScroll",Enum.GetValues<VideoScrollAction>(),s=>s.VideoShiftScroll??VideoScrollAction.Resize,(s,v)=>s with { VideoShiftScroll=v }, defaults);
        Toggle("Audio", "Spectrogram", "Display the spectrum instead of the waveform.", "AudioSpectrum", s=>s.AudioSpectrum==true,(s,v)=>s with { AudioSpectrum=v }, defaults);
        Toggle("Audio", "Autoscroll", "Follow the selected subtitle in the audio display.", "AudioAutoScroll", s=>s.AudioAutoScroll==true,(s,v)=>s with { AudioAutoScroll=v }, defaults);
        Toggle("Audio", "Link amplitude and volume", "Changing display intensity also changes playback volume; disables independent volume.", "AudioLinkVolume", s=>s.AudioVolumeLinked,(s,v)=>s with { AudioVolumeLinked=v }, defaults);
        Toggle("Audio", "Mute playback", "Mute audio output without changing the display.", "AudioMutePlayback", s=>s.PlaybackMuted,(s,v)=>s with { PlaybackMuted=v }, defaults);
        Toggle("Audio", "Toshiki K-Timing auto cut", "Automatically split text into timing slots when beginning a karaoke plan.", "KaraokeAutoCutOption", s=>s.KaraokeAutoCut??true,(s,v)=>s with { KaraokeAutoCut=v }, defaults);
        Toggle("Audio", "Auto-commit timing", "Commit audio marker adjustments automatically.", "AudioAutoCommit", s=>s.AudioAutoCommit==true,(s,v)=>s with { AudioAutoCommit=v }, defaults);
        Toggle("Audio", "Advance after commit", "Move to the next line after committing audio timing.", "AudioAutoNext", s=>s.AudioAutoNext==true,(s,v)=>s with { AudioAutoNext=v }, defaults);
        Number("Audio", "Default line duration", "Duration for a new audio timing range, in milliseconds.", "AudioDefaultDuration",10,600000,s=>s.AudioDefaultDuration!.Value,(s,v)=>s with { AudioDefaultDuration=(int)v }, defaults);
        Number("Audio", "Lead-in", "Time added before a line by the lead-in action, in milliseconds.", "AudioLeadIn",0,60000,s=>s.AudioLeadIn!.Value,(s,v)=>s with { AudioLeadIn=(int)v }, defaults);
        Number("Audio", "Lead-out", "Time added after a line by the lead-out action, in milliseconds.", "AudioLeadOut",0,60000,s=>s.AudioLeadOut!.Value,(s,v)=>s with { AudioLeadOut=(int)v }, defaults);
        Toggle("Video", "Seek to selected subtitle", "Jump to the first visible frame when selecting a line.", "VideoAutoSeekOption",s=>s.VideoAutoSeek==true,(s,v)=>s with { VideoAutoSeek=v }, defaults);
        Toggle("Video", "Overscan guides", "Show safe area guides over the video preview.", "VideoOverscan",s=>s.VideoOverscan,(s,v)=>s with { VideoOverscan=v }, defaults);
        Number("Video", "Fast jump (frames)", "Number of frames skipped by fast video navigation.", "VideoFastJumpStep",1,1000000,s=>s.VideoFastJumpStep!.Value,(s,v)=>s with { VideoFastJumpStep=(int)v }, defaults);
        Paths("Automation", "Autoload directories", "One path per line. Keeps the existing Automation directory structure. Scripts use the Aegisub 3.2.2 API. Applying changed paths reloads scripts.", "AutomationAutoload",s=>s.AutomationAutoloadDirectories??MainWindowViewModel.DefaultAutomationAutoloadDirectories,(s,v)=>s with { AutomationAutoloadDirectories=v });
        Paths("Automation", "Include directories", "One path per line; ?data and ?user aliases are supported.", "AutomationIncludes",s=>s.AutomationIncludeDirectories??MainWindowViewModel.DefaultAutomationIncludeDirectories,(s,v)=>s with { AutomationIncludeDirectories=v });
        Toggle("Keyboard shortcuts", "Global timing keypad overrides (Medusa)", "Allow global timing shortcuts outside the audio panel.", "GlobalTimingHotkeys",s=>s.GlobalTimingHotkeys,(s,v)=>s with { GlobalTimingHotkeys=v }, defaults);
        Toggle("Recovery", "Automatically save recovery snapshots", "Retain up to ten versions per document within a 512 MiB profile budget.", "AutosaveEnabled",s=>s.AutosaveEnabled==true,(s,v)=>s with { AutosaveEnabled=v }, defaults);
        Number("Recovery", "Autosave interval", "Seconds between recovery snapshots. Does not overwrite the original subtitle file.", "AutosaveInterval",1,86400,s=>s.AutosaveIntervalSeconds!.Value,(s,v)=>s with { AutosaveIntervalSeconds=(int)v }, defaults);
        TextSetting("Recovery", "Recovery directory", "Blank uses this profile's recovery directory. Relative paths are resolved from the profile.", "RecoveryDirectory",s=>s.RecoveryDirectory??"",(s,v)=>s with { RecoveryDirectory=v }, defaults);
        var shortcutSearch=new Button { Name="OptionsShortcutSearch",HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Left };
        ToolTip.SetTip(shortcutSearch,"Open matching commands in the keyboard shortcut editor. Pending settings stay in this draft.");Register(shortcutSearch);_form.Children.Add(shortcutSearch);
        shortcutSearch.Click+=(_,_)=>
        {
            var query=_search.Text??"";_navigation.SelectedItem="Keyboard shortcuts";_search.Text="";_filter.Text=query;
        };
        var shortcuts=BuildShortcuts();
        var scroll=new ScrollViewer { Name="OptionsContent", Content=_form, HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Register(scroll);
        var content=new Grid(); content.Children.Add(scroll); content.Children.Add(shortcuts);
        void Filter()
        {
            var query=(_search.Text??"").Trim(); var category=_navigation.SelectedItem as string;
            var terms=query.Split(' ',StringSplitOptions.RemoveEmptyEntries);
            var matches=0;
            foreach(var row in _rows)
            {
                row.View.IsVisible=query.Length==0 ? row.Category==category || category=="General" && row.Category is "Appearance" or "Editor" or "Subtitle grid" : terms.All(term=>row.Search.Contains(term,StringComparison.CurrentCultureIgnoreCase));
                if(row.View.IsVisible)matches++;
            }
            foreach(var group in _groups.Values)group.Frame.IsVisible=group.Rows.Children.Any(c=>c.IsVisible);
            var matchingShortcuts=query.Length==0?0:ShortcutChoices().Count(choice=>ShortcutMatches(choice,terms));
            shortcutSearch.IsVisible=matchingShortcuts>0;shortcutSearch.Content=new TextBlock { Text=$"Keyboard shortcuts · {matchingShortcuts} matching commands",TextWrapping=TextWrapping.Wrap };
            shortcuts.IsVisible=query.Length==0&&category=="Keyboard shortcuts";
            // The shortcut workspace owns its bounded list scrolling.
            scroll.IsVisible=!shortcuts.IsVisible;
            _status.Text=matches==0&&matchingShortcuts==0&&!shortcuts.IsVisible?"No matching settings.":query.Length>0?$"{matches} matching settings · {matchingShortcuts} matching shortcut commands":"Changes take effect only after Apply or OK.";
            scroll.Offset=default;
        }
        _navigation.ItemsSource=new[] { "General", "Appearance", "Editor", "Subtitle grid", "Audio", "Video", "Automation", "Keyboard shortcuts", "Recovery", "Advanced audio" };
        _navigation.SelectionChanged+=(_,_)=>{ if(!string.IsNullOrEmpty(_search.Text)) _search.Text=""; Filter(); };
        _search.TextChanged+=(_,_)=>Filter();
        var body=new Grid { ColumnDefinitions=new("Auto,*"), ColumnSpacing=6, Margin=new Thickness(8,0) }; body.Children.Add(_navigation); Grid.SetColumn(content,1); body.Children.Add(content);
        var footer=new Grid { ColumnDefinitions=new("*,Auto"), Margin=new Thickness(12,8), ColumnSpacing=12 };
        var restore=new Button { Name="RestoreOptionsDefaults", Content="Restore section defaults", HorizontalAlignment=HorizontalAlignment.Left };
        ToolTip.SetTip(restore,"Reset only the selected category (General resets General, not the other overview sections). Apply or OK saves the result; Cancel discards it."); Register(restore);
        restore.Click+=(_,_)=>
        {
            foreach(var row in _rows.Where(r=>r.Category==_navigation.SelectedItem as string))row.Reset();
            if(_navigation.SelectedItem as string=="Keyboard shortcuts") { _hotkeys.ResetAll(); _draft=_draft with { HotkeyOverrides=_hotkeys.Overrides }; RefreshBindings(); }
            Reload();
        };
        footer.Children.Add(restore);
        var actions=new StackPanel { Orientation=Orientation.Horizontal, Spacing=8 }; Grid.SetColumn(actions,1);footer.Children.Add(actions);
        foreach(var (label,name) in new[] { ("OK","OptionsOk"),("Cancel","OptionsCancel"),("Apply","OptionsApply") })
        {
            var button=new Button { Content=label, Name=name, MinWidth=72, IsCancel=label=="Cancel", IsDefault=label=="OK" }; Register(button);actions.Children.Add(button);
            button.Click+=async (_,_)=>{ if(label=="Cancel")Close(); else if(await SaveAsync()&&label=="OK")Close(); };
        }
        var shell=new Grid { RowDefinitions=new("Auto,*,Auto,Auto") };
        _search.Margin=new Thickness(12,10); At(shell,_search,0);At(shell,body,1);_status.Margin=new Thickness(12,6,12,0);At(shell,_status,2);At(shell,footer,3);Content=shell;
        AddHandler(KeyDownEvent,(_,e)=>
        {
            if(e.Key is Key.Enter or Key.Escape && this.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().Any(p=>!string.IsNullOrEmpty(p.PreeditText)))e.Handled=true;
            if(e.Key==Key.F&&e.KeyModifiers==Avalonia.Input.KeyModifiers.Control) { _search.Focus();e.Handled=true; }
        },RoutingStrategies.Tunnel);
        Opened+=(_,_)=>ScheduleWorkingAreaFit();ScalingChanged+=(_,_)=>ScheduleWorkingAreaFit();
        Closing+=(_,e)=> { if(_applying)e.Cancel=true; };
        Closed+=(_,_)=>{_closed=true;UiFontResources.Apply(_model.UiFontFamily,_model.UiFontSize);};
        Reload();_navigation.SelectedItem="General";Filter();_names.Complete();
    }

    private void ScheduleWorkingAreaFit()=>Avalonia.Threading.Dispatcher.UIThread.Post(()=>
    {
        if(!IsVisible)return;
        var screen=Screens.ScreenFromWindow(this)??Screens.Primary;if(screen is null)return;
        var area=screen.WorkingArea;var scale=RenderScaling;
        var frame=FrameSize??ClientSize;
        var chromeWidth=Math.Max(0,frame.Width-ClientSize.Width);var chromeHeight=Math.Max(0,frame.Height-ClientSize.Height);
        var availableWidth=Math.Max(1,area.Width/scale-chromeWidth-16);var availableHeight=Math.Max(1,area.Height/scale-chromeHeight-16);
        MinWidth=Math.Min(620,availableWidth);MinHeight=Math.Min(360,availableHeight);
        Width=Math.Clamp(Width,MinWidth,availableWidth);Height=Math.Clamp(Height,MinHeight,availableHeight);
        Position=new PixelPoint(Math.Clamp(Position.X,area.X,Math.Max(area.X,area.Right-(int)Math.Ceiling((Width+chromeWidth)*scale))),
            Math.Clamp(Position.Y,area.Y,Math.Max(area.Y,area.Bottom-(int)Math.Ceiling((Height+chromeHeight)*scale))));
    },Avalonia.Threading.DispatcherPriority.Background);

    private void Register(Control control) { if(control.Name is {} name)_names.Register(name,control); }
    private void Add(string category,string label,string description,Control control,Action reload,Action reset)
    {
        Register(control);ToolTip.SetTip(control,description);
        var group=category switch
        {
            "Appearance"=>label=="Theme"?"Appearance":"Interface font",
            "Audio"=>label switch
            {
                "Link amplitude and volume" or "Mute playback"=>"Playback",
                "Keyframes in dialogue mode" or "Keyframes in karaoke mode" or "Cursor time" or "Video position" or "Seconds boundaries" or "Spectrogram"=>"Display visual options",
                "Toshiki K-Timing auto cut" or "Karaoke label font size" or "Karaoke label font family"=>"Audio labels",
                "Default line duration" or "Lead-in" or "Lead-out"=>"Timing defaults (ms)",
                _=>"Options"
            },
            "Timing"=>label=="Toshiki K-Timing auto cut"?"Karaoke":label is "Auto-commit timing" or "Advance after commit"?"Timing behavior":"Timing defaults (ms)",
            "Video"=>label=="Fast jump (frames)"||label.Contains("scrolling",StringComparison.OrdinalIgnoreCase)?"Navigation":"Video display",
            "Advanced audio"=>"Spectrum",
            "Recovery"=>"Automatic recovery",
            "General"=>"General",
            "Editor"=>"Edit box",
            "Subtitle grid"=>"Subtitle grid",
            "Automation"=>"Script paths",
            _=>category
        };
        var key=(category,group);
        if(!_groups.TryGetValue(key,out var section))
        {
            var rows=new StackPanel{Spacing=5};
            var inner=new StackPanel{Spacing=6};
            inner.Children.Add(new TextBlock{Text=group,FontWeight=FontWeight.SemiBold,TextWrapping=TextWrapping.Wrap});
            inner.Children.Add(rows);
            var frame=new Border{BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(4),Padding=new Thickness(10,7),Child=inner};
            frame.Bind(Border.BackgroundProperty,frame.GetResourceObservable("PanelBrush"));
            frame.Bind(Border.BorderBrushProperty,frame.GetResourceObservable("DividerBrush"));
            section=(frame,rows);_groups.Add(key,section);_form.Children.Add(frame);
        }
        Control row;
        if(control is CheckBox)
        {
            // The native checkbox already carries its own label.
            control.HorizontalAlignment=HorizontalAlignment.Left;
            row=control;
        }
        else if(control is TextBox || control is TextBlock)
        {
            var stack=new StackPanel{Spacing=3};
            stack.Children.Add(new TextBlock{Text=label,TextWrapping=TextWrapping.Wrap});stack.Children.Add(control);row=stack;
        }
        else
        {
            var grid=new Grid{ColumnDefinitions=new("*,*"),ColumnSpacing=8};
            grid.Children.Add(new TextBlock{Text=label,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center});
            Grid.SetColumn(control,1);grid.Children.Add(control);row=grid;
        }
        ToolTip.SetTip(row,description);section.Rows.Children.Add(row);
        _rows.Add(new(category,category+" "+group+" "+label+" "+description,row,reload,reset));
    }

    private void Toggle(string category,string label,string description,string name,Func<AppSettings,bool> get,Func<AppSettings,bool,AppSettings> set,AppSettings defaults)
    {
        var control=new CheckBox { Name=name,Content=label };
        Add(category,label,description,control,()=>control.IsChecked=get(_draft),()=>_draft=set(_draft,get(defaults)));
        control.IsCheckedChanged+=(_,_)=>{if(!_loading) { _draft=set(_draft,control.IsChecked==true);if(name=="GlobalTimingHotkeys"&&_globalHotkeys is not null)_globalHotkeys.IsChecked=control.IsChecked; }};
    }
    private void Number(string category,string label,string description,string name,decimal min,decimal max,Func<AppSettings,decimal> get,Func<AppSettings,decimal,AppSettings> set,AppSettings defaults)
    {
        var control=new NumericUpDown { Name=name, Minimum=min,Maximum=max,Increment=1,FormatString="0",Width=140,HorizontalAlignment=HorizontalAlignment.Left };
        Add(category,label,description,control,()=>control.Value=get(_draft),()=>_draft=set(_draft,get(defaults)));
        control.ValueChanged+=(_,_)=>{if(!_loading&&control.Value is {} value) { _draft=set(_draft,value);if(name=="UiFontSize")PreviewFont(); }};
    }
    private TextBox TextSetting(string category,string label,string description,string name,Func<AppSettings,string> get,Func<AppSettings,string,AppSettings> set,AppSettings defaults)
    {
        var control=new TextBox { Name=name };
        Add(category,label,description,control,()=>control.Text=get(_draft),()=>_draft=set(_draft,get(defaults)));
        control.TextChanged+=(_,_)=>{if(!_loading)_draft=set(_draft,control.Text??"");};return control;
    }
    private void Paths(string category,string label,string description,string name,Func<AppSettings,string[]> get,Func<AppSettings,string[],AppSettings> set)
    {
        var control=TextSetting(category,label,description,name,s=>string.Join(Environment.NewLine,get(s)),(s,v)=>set(s,v.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)),new AppSettings());
        control.AcceptsReturn=true;control.TextWrapping=TextWrapping.Wrap;control.MinHeight=85;
    }
    private void ChoiceSetting<T>(string category,string label,string description,string name,T[] values,Func<AppSettings,T> get,Func<AppSettings,T,AppSettings> set,AppSettings defaults)
    {
        var control=new ComboBox { Name=name,ItemsSource=values,MinWidth=160,HorizontalAlignment=HorizontalAlignment.Left };
        Add(category,label,description,control,()=>control.SelectedItem=get(_draft),()=>_draft=set(_draft,get(defaults)));
        control.SelectionChanged+=(_,_)=>{if(!_loading&&control.SelectedItem is T value)_draft=set(_draft,value);};
    }
    private void Reload() { _loading=true;try { foreach(var row in _rows)row.Reload(); if(_globalHotkeys is not null)_globalHotkeys.IsChecked=_draft.GlobalTimingHotkeys; }finally { _loading=false; } PreviewFont(); }
    private void PreviewFont(){if(!_closed)UiFontResources.Apply(_draft.UiFontFamily,_draft.UiFontSize);}
    private async Task<bool> SaveAsync()
    {
        if(_applying)return false;
        var invalid=_rows.FirstOrDefault(row=>row.View.GetVisualDescendants().Prepend(row.View).OfType<NumericUpDown>().Any(control=>control.Value is null||control.GetValue(DataValidationErrors.HasErrorsProperty)));
        if(invalid is not null) { _status.Text="Enter a valid numeric value before applying settings.";return false; }
        _applying=true;IsEnabled=false;_status.Text="Applying settings…";
        try
        {
            var warning=await _model.ApplyOptionsAsync(_baseline,_draft);
            _baseline=_draft=_model.CaptureOptions();_hotkeys=new(_model.HotkeyProfile.Defaults,_draft.HotkeyOverrides);RefreshBindings();Reload();_status.Text=warning??"Settings saved.";return true;
        }
        catch(Exception error) { _status.Text="Could not apply settings: "+error.Message;return false; }
        finally { _applying=false;IsEnabled=true; }
    }
    private Control BuildShortcuts()
    {
        foreach(var control in new Control[] { _context,_filter,_commands,_bindings,_gesture,_capture,_hotkeyStatus })Register(control);
        var root=new Grid { Margin=new Thickness(14),RowDefinitions=new("Auto,Auto,*,Auto"),RowSpacing=8 };
        var heading=new StackPanel { Spacing=8, Orientation=Orientation.Horizontal };heading.Children.Add(new TextBlock { Text="Context",VerticalAlignment=VerticalAlignment.Center });heading.Children.Add(_context);root.Children.Add(heading);
        _context.ItemsSource=Enum.GetValues<HotkeyContext>();_context.SelectedItem=HotkeyContext.Default;
        At(root,_filter,1);var body=new Grid { ColumnDefinitions=new("*,*"),ColumnSpacing=10 };At(root,body,2);body.Children.Add(_commands);
        var edit=new StackPanel { Spacing=7 };Grid.SetColumn(edit,1);body.Children.Add(new ScrollViewer { Content=edit, [Grid.ColumnProperty]=1 });
        _globalHotkeys=new CheckBox { Name="GlobalTimingHotkeysEditor", Content="Global timing keypad overrides (Medusa)", IsChecked=_draft.GlobalTimingHotkeys };
        Register(_globalHotkeys);
        _globalHotkeys.IsCheckedChanged+=(_,_)=>{if(!_loading) { _draft=_draft with { GlobalTimingHotkeys=_globalHotkeys.IsChecked==true };if(_names.Find("GlobalTimingHotkeys") is CheckBox checkbox)checkbox.IsChecked=_globalHotkeys.IsChecked; }};
        edit.Children.Add(_globalHotkeys);
        edit.Children.Add(new TextBlock { Text="Current bindings" });edit.Children.Add(_bindings);edit.Children.Add(_gesture);edit.Children.Add(_capture);
        var buttons=new WrapPanel();edit.Children.Add(buttons);
        foreach(var (label,name,operation) in new[] { ("Add","AddHotkey","add"),("Change","ChangeHotkey","change"),("Remove","RemoveHotkey","remove"),("Clear command","ClearHotkeys","clear"),("Reset command","ResetCommandHotkeys","reset"),("Reset context","ResetContextHotkeys","context"),("Reset all","ResetAllHotkeys","all") })
        {
            var button=new Button { Name=name,Content=label,Margin=new Thickness(0,0,4,4) };Register(button);buttons.Children.Add(button);button.Click+=(_,_)=>Modify(operation);
        }
        edit.Children.Add(new TextBlock { Text="Changes are staged until Apply or OK. A conflicting key leaves the draft bindings unchanged.",TextWrapping=TextWrapping.Wrap });At(root,_hotkeyStatus,3);
        _context.SelectionChanged+=(_,_)=>RefreshCommands();_filter.TextChanged+=(_,_)=>RefreshCommands();_commands.SelectionChanged+=(_,_)=>RefreshBindings();
        _bindings.SelectionChanged+=(_,_)=>{if(_bindings.SelectedItem is string key)_capture.Text=_gesture.Text=key;};
        _capture.AddHandler(KeyDownEvent,(_,e)=>
        {
            e.Handled=true;
            if(e.Key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)return;
            _capture.Text=_gesture.Text=new HotkeyGesture(EditorKeyNames.FromEvent(e),EditorKeyNames.FromModifiers(e.KeyModifiers)).Format();
        },RoutingStrategies.Tunnel);
        RefreshCommands();return root;
    }
    private IEnumerable<CommandChoice> ShortcutChoices()=>_model.Registry.Commands.Select(c=>new CommandChoice(c.Metadata.Id,c.Metadata.Label))
        .Concat(_hotkeys.Resolver.Bindings.Select(b=>new CommandChoice(b.CommandId,b.CommandId))).DistinctBy(c=>c.Id);
    private static bool ShortcutMatches(CommandChoice choice,string[] terms)=>terms.All(term=>choice.Id.Contains(term,StringComparison.OrdinalIgnoreCase)||choice.Label.Contains(term,StringComparison.OrdinalIgnoreCase));
    private void RefreshCommands()
    {
        var selected=Choice?.Id;var terms=(_filter.Text??"").Split(' ',StringSplitOptions.RemoveEmptyEntries);
        var choices=ShortcutChoices().Where(choice=>ShortcutMatches(choice,terms)).OrderBy(c=>c.Label).ToArray();
        _commands.ItemsSource=choices;_commands.SelectedItem=choices.FirstOrDefault(c=>c.Id==selected)??choices.FirstOrDefault();RefreshBindings();
    }
    private void RefreshBindings()
    {
        var keys=Choice is {} choice?_hotkeys.EffectiveGestures(choice.Id,Context):[];
        _bindings.ItemsSource=keys;_bindings.SelectedItem=keys.FirstOrDefault();
    }
    private void Modify(string operation)
    {
        try
        {
            if(operation=="all")_hotkeys.ResetAll();else if(operation=="context")_hotkeys.ResetContext(Context);
            else
            {
                if(Choice is not {} choice)throw new ArgumentException("Choose a command.");
                if(operation=="reset")_hotkeys.ResetCommand(Context,choice.Id);
                else
                {
                    var keys=_hotkeys.EffectiveGestures(choice.Id,Context).ToList();
                    if(operation is "change" or "remove") { if(_bindings.SelectedItem is not string selected)throw new ArgumentException("Choose a binding."); keys.Remove(selected); }
                    if(operation=="clear")keys.Clear();else if(operation!="remove")keys.Add(_gesture.Text??"");
                    _hotkeys.Set(Context,choice.Id,keys);
                }
            }
            _draft=_draft with { HotkeyOverrides=_hotkeys.Overrides };RefreshBindings();_hotkeyStatus.Text="Shortcut changes staged.";
        }
        catch(Exception error) when(error is ArgumentException or InvalidOperationException) { _hotkeyStatus.Text=error.Message; }
    }
    private static void At(Grid parent,Control child,int row) { Grid.SetRow(child,row);parent.Children.Add(child); }
}
