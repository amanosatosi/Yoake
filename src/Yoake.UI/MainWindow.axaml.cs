using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Controls.Presenters;
using System.Collections.Specialized;
using System.ComponentModel;
using Yoake.Core.Commands;
using Yoake.Core.Hotkeys;
using Yoake.Core.Logging;
using Yoake.Core.Subtitles;
using Yoake.UI.Services;
using Yoake.UI.ViewModels;
using Yoake.UI.Controls;

namespace Yoake.UI;

public partial class MainWindow : Window, IEditorDialogs
{
    private bool _closingAllowed, _closingPending, _selectionSync;
    private MainWindowViewModel? _model;
    private static readonly FilePickerFileType SubtitleFiles=new("ASS/SSA subtitles"){Patterns=["*.ass","*.ssa"]};
    private static readonly FilePickerFileType MediaFiles=new("Video/audio"){Patterns=["*.mkv","*.mp4","*.webm","*.avi","*.mov","*.m2ts","*.ts","*.mp3","*.flac","*.wav","*.m4a","*.ogg","*.opus"]};
    public MainWindow()
    {
        InitializeComponent();
        StartupDiagnostics.Checkpoint("MainWindow XAML initialized");
        if(OperatingSystem.IsWindows()){WindowDecorations=Avalonia.Controls.WindowDecorations.Full;ExtendClientAreaToDecorationsHint=true;ExtendClientAreaTitleBarHeightHint=36;}
        StartupDiagnostics.Checkpoint("MainWindow native decorations configured");
        SubtitleRows.LayoutUpdated+=(_,_)=>ApplyRowWidths();
        DataContextChanged+=(_,_)=>AttachModel();
        AddHandler(KeyDownEvent,HandleKey,RoutingStrategies.Tunnel);
        Opened+=(_,_)=>UpdateChrome();
        LayoutUpdated+=(_,_)=>UpdateChrome();
        ScalingChanged+=(_,_)=>Dispatcher.UIThread.Post(UpdateChrome);
        PropertyChanged+=(_,e)=>{if(e.Property==WindowStateProperty)Dispatcher.UIThread.Post(UpdateChrome);};
        TemporalTextColumn.SizeChanged+=(_,_)=>ClampAudioBounds();
        AudioSplitter.AddHandler(PointerReleasedEvent,(_,_)=>{if(_model is not null)_model.AudioDisplayHeight=TemporalTextColumn.RowDefinitions[0].ActualHeight;},RoutingStrategies.Tunnel);
        AudioSplitter.KeyUp+=(_,_)=>{if(_model is not null)_model.AudioDisplayHeight=TemporalTextColumn.RowDefinitions[0].ActualHeight;};
        TitleTabStrip.SizeChanged+=(_,_)=>UpdateTabLimit();
        SubtitleText.PropertyChanged+=(_,e)=>{if(_model is not null&&(e.Property==TextBox.CaretIndexProperty||e.Property==TextBox.SelectionStartProperty||e.Property==TextBox.SelectionEndProperty)){_model.TextCursor=SubtitleText.CaretIndex;_model.TextSelectionStart=SubtitleText.SelectionStart;_model.TextSelectionEnd=SubtitleText.SelectionEnd;}};
        Closing+=HandleClosing;
        Closed+=(_,_)=>_model?.Dispose();
    }
    private void AttachModel()
    {
        if(_model is not null){_model.FrameReady-=FrameReady;_model.TextFormattingApplied-=FormattingApplied;_model.PropertyChanged-=ModelChanged;if(_model.SelectedEvents is INotifyCollectionChanged old)old.CollectionChanged-=SelectionChanged;}
        _model=DataContext as MainWindowViewModel;
        if(_model is not null){_model.Dialogs=this;_model.FrameReady+=FrameReady;_model.TextFormattingApplied+=FormattingApplied;
            var widths=_model.GridColumnWidths; // Normalized at the model boundary.
            var count=Math.Min(widths.Count,ColumnHeader.ColumnDefinitions.Count);
            for(var i=0;i<count;i++)ColumnHeader.ColumnDefinitions[i].Width=new GridLength(widths[i]);
            UpdateAudioBounds();
            _model.PropertyChanged+=ModelChanged;if(_model.SelectedEvents is INotifyCollectionChanged collection)collection.CollectionChanged+=SelectionChanged;
            StartupDiagnostics.Checkpoint($"MainWindow model attached; {count} column widths applied");}
    }
    private int _resizeColumn=-1;
    private double _resizeStart, _resizeWidth;
    private void HeaderPressed(object? sender,PointerPressedEventArgs e)
    {
        if(!e.GetCurrentPoint(ColumnHeader).Properties.IsLeftButtonPressed)return;
        var x=e.GetPosition(ColumnHeader).X;double boundary=0;
        for(var i=0;i<Math.Min(7,ColumnHeader.ColumnDefinitions.Count);i++)
        {
            boundary+=ColumnHeader.ColumnDefinitions[i].ActualWidth+4;
            if(Math.Abs(x-boundary)>7)continue;
            _resizeColumn=i;_resizeStart=e.GetPosition(this).X;_resizeWidth=ColumnHeader.ColumnDefinitions[i].ActualWidth;e.Pointer.Capture(ColumnHeader);e.Handled=true;break;
        }
    }
    private void HeaderMoved(object? sender,PointerEventArgs e)
    {
        if(_resizeColumn<0)return;
        ColumnHeader.ColumnDefinitions[_resizeColumn].Width=new GridLength(Math.Clamp(_resizeWidth+e.GetPosition(this).X-_resizeStart,24,600));ApplyRowWidths();e.Handled=true;
    }
    private async void HeaderReleased(object? sender,PointerReleasedEventArgs e)
    {
        if(_resizeColumn<0)return;_resizeColumn=-1;e.Pointer.Capture(null);
        if(_model is not null)await _model.Registry.InvokeAsync(CommandIds.GridColumnWidths,new(),ColumnHeader.ColumnDefinitions.Take(7).Select(c=>c.Width.Value).ToArray());
    }
    private void ApplyRowWidths()
    {
        foreach(var row in SubtitleRows.GetVisualDescendants().OfType<Grid>().Where(g=>g.Tag as string=="SubtitleRow"))
            for(var i=0;i<Math.Min(7,Math.Min(row.ColumnDefinitions.Count,ColumnHeader.ColumnDefinitions.Count));i++)if(row.ColumnDefinitions[i].Width!=ColumnHeader.ColumnDefinitions[i].Width)row.ColumnDefinitions[i].Width=ColumnHeader.ColumnDefinitions[i].Width;
    }
    private void FormattingApplied(object? sender,EventArgs args)
    {
        if(_model is null)return;var start=_model.TextSelectionStart;var end=_model.TextSelectionEnd;var cursor=_model.TextCursor;SubtitleText.Focus();SubtitleText.CaretIndex=cursor;SubtitleText.SelectionStart=start;SubtitleText.SelectionEnd=end;
    }
    public Task<AssColor?> ChooseColorAsync(AssColor color)=>new AssColorDialog(color,_model?.RecentColorsPath).ShowDialog<AssColor?>(this);
    public async Task<FontChoice?> ChooseFontAsync(string family,string size)
    {
        var dialog=new Window{Title="Subtitle font",Width=380,SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var panel=new StackPanel{Margin=new Thickness(12),Spacing=6};var picker=new FontPicker{FontName=family};panel.Children.Add(picker);
        var number=new NumericUpDown{Minimum=0.1m,Maximum=10000,Increment=1,Value=decimal.TryParse(size,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value)?value:60};
        panel.Children.Add(new TextBlock{Text="Size"});panel.Children.Add(number);var apply=new Button{Content="Apply font",IsDefault=true};apply.Click+=(_,_)=>dialog.Close(new FontChoice(picker.FontName,(number.Value??60).ToString(System.Globalization.CultureInfo.InvariantCulture)));panel.Children.Add(apply);var cancel=new Button{Content="Cancel",IsCancel=true};cancel.Click+=(_,_)=>dialog.Close();panel.Children.Add(cancel);dialog.Content=panel;return await dialog.ShowDialog<FontChoice?>(this);
    }
    private void UpdateAudioBounds()
    {
        if(_model is null)return;
        TemporalTextColumn.RowDefinitions[0].Height=new GridLength(_model.AudioDisplayHeight);ClampAudioBounds();
    }
    private void ClampAudioBounds()
    {
        var available=TemporalTextColumn.Bounds.Height;if(available<=0)return;
        var maximum=Math.Max(130,available-TemporalTextColumn.RowDefinitions[2].MinHeight-4);
        var row=TemporalTextColumn.RowDefinitions[0];
        var height=row.Height.IsAbsolute?row.Height.Value:row.ActualHeight;
        var clamped=Math.Clamp(height,130,maximum);if(Math.Abs(height-clamped)>0.1)row.Height=new GridLength(clamped);
    }
    private void FrameReady(object? sender,EventArgs e)=>VideoImage.InvalidateVisual();
    private void ModelChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName==nameof(MainWindowViewModel.AudioDisplayHeight))UpdateAudioBounds();
        if(e.PropertyName==nameof(MainWindowViewModel.SelectedEvent))Dispatcher.UIThread.Post(()=>{if(_model?.SelectedEvent is {} line){SubtitleRows.ScrollIntoView(line);SyncSelection();}});
    }
    private void SelectionChanged(object? sender,NotifyCollectionChangedEventArgs e) { if(!_selectionSync)SyncSelection(); }
    private void SyncSelection()
    {
        if(_model is null||_selectionSync||SubtitleRows.SelectedItems is null)return;
        if(SubtitleRows.SelectedItems.OfType<AssEvent>().SequenceEqual(_model.SelectedEvents))return;
        _selectionSync=true;
        try{SubtitleRows.SelectedItems=_model.SelectedEvents.Where(_model.Events.Contains).ToList();}
        finally{_selectionSync=false;}
    }
    private void GridSelectionChanged(object? sender,SelectionChangedEventArgs e)
    {
        if(_model is null||_model.IsSynchronizingSelection||_selectionSync||SubtitleRows.SelectedItems is null)return;
        _selectionSync=true;
        try
        {
            var next=e.AddedItems.OfType<AssEvent>().LastOrDefault()??SubtitleRows.SelectedItems.OfType<AssEvent>().FirstOrDefault();
            _model.SelectedEvent=next;
            if(_model.SelectedEvent!=next){Dispatcher.UIThread.Post(SyncSelection);return;}
            _model.SetSelectedEvents(SubtitleRows.SelectedItems.OfType<AssEvent>().ToArray());
        }
        finally{_selectionSync=false;}
    }
    private async void HandleKey(object? sender,KeyEventArgs e)
    {
        if(_model is null||e.Handled)return;
        var control=e.Source as Control;var context=HotkeyContext.Default;
        for(var input=control;input is not null;input=input.Parent as Control)
        {
            if(input is TextBox textBox && textBox.GetVisualDescendants().OfType<TextPresenter>().Any(p=>!string.IsNullOrEmpty(p.PreeditText)))return;
            if(e.Key==Key.Enter&&(input is AutoCompleteBox {IsDropDownOpen:true}||input is ComboBox {IsDropDownOpen:true}))return;
        }
        for(var current=control;current is not null;current=current.Parent as Control)
        {
            if(current==SubtitleRows){context=HotkeyContext.SubtitleGrid;break;}
            if(current==EventEditorRegion){context=HotkeyContext.SubtitleEdit;break;}
            if(current==AudioRegion){context=HotkeyContext.Audio;break;}
            if(current==VideoRegion){context=HotkeyContext.Video;break;}
        }
        // Shift+Enter and IME input remain with the native text editor.
        if(e.Key==Key.Enter && e.KeyModifiers==Avalonia.Input.KeyModifiers.Shift)return;
        var modifiers=Yoake.Core.Hotkeys.KeyModifiers.None;
        if(e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))modifiers|=Yoake.Core.Hotkeys.KeyModifiers.Control;
        if(e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift))modifiers|=Yoake.Core.Hotkeys.KeyModifiers.Shift;
        if(e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt))modifiers|=Yoake.Core.Hotkeys.KeyModifiers.Alt;
        if(e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Meta))modifiers|=Yoake.Core.Hotkeys.KeyModifiers.Meta;
        // Enter in metadata commits, without advancing while a dropdown is open.
        var binding=_model.Hotkeys.Resolve(new(e.Key.ToString(),modifiers),[HotkeyContext.Default,context]);
        if(binding is null)return;
        var commandId=binding.CommandId;
        if(commandId==CommandIds.EditCommitNext && context==HotkeyContext.SubtitleEdit && !SubtitleText.IsKeyboardFocusWithin)commandId=CommandIds.EditCommit;
        _model.TextCursor=SubtitleText.CaretIndex;
        e.Handled=true;
        await _model.Registry.InvokeAsync(commandId,new(FocusContext:context.ToString()));
    }
    private async void HandleClosing(object? sender,WindowClosingEventArgs e)
    {
        if(_closingAllowed||_model is null)return;e.Cancel=true;if(_closingPending)return;_closingPending=true;
        try{if(await _model.CloseWindowAsync()){_closingAllowed=true;Close();}}
        catch(Exception exception){_model.Registry.ReportFailure("window/close",exception);}
        finally{_closingPending=false;}
    }
    public bool RedundantCaptionHidden {get;private set;}
    private void UpdateChrome()
    {
        if(!OperatingSystem.IsWindows())return;
        // Avalonia 12's extended client area uses a decoration overlay. Its
        // title text paints ABOVE our tabs; hiding Win32 NC text alone cannot
        // fix that. Keep the framework caption buttons and their platform roles.
        var root=this.GetVisualAncestors().LastOrDefault()??this;
        var parts=root.GetVisualDescendants().OfType<Control>().ToArray();
        if(parts.FirstOrDefault(c=>c.Name=="PART_TitleTextPanel") is {} title)
        {title.IsVisible=false;RedundantCaptionHidden=true;}
        var buttons=parts.FirstOrDefault(c=>c.Name=="PART_OverlayPanel");
        double inset=0;
        if(buttons is {IsVisible:true}&&buttons.Bounds.Width>0&&buttons.TranslatePoint(default,this) is {} top)
            inset=Math.Max(0,ClientSize.Width-top.X);
        else if(TryGetPlatformHandle() is {} handle)
            inset=Yoake.Native.WindowsChrome.CaptionWidth(handle.Handle,RenderScaling);
        var padding=new Thickness(0,0,inset,0);
        if(TitleTabStrip.Padding!=padding)TitleTabStrip.Padding=padding;
        UpdateTabLimit();
    }
    private void UpdateTabLimit()=>TabScroll.MaxWidth=Math.Max(0,TitleTabStrip.Bounds.Width-TitleTabStrip.Padding.Right-80);
    private void AudioModeChanged(object? sender,SelectionChangedEventArgs e){if(AudioDisplay is not null&&sender is ComboBox box)AudioDisplay.Spectrogram=box.SelectedIndex==1;}
    private async void RecentFileClick(object? sender,RoutedEventArgs e){if(_model is not null&&sender is Control{DataContext:string path})await _model.Registry.InvokeAsync(CommandIds.SubtitleOpen,new(),path);}
    private static string? LocalPath(IStorageItem item)=>item.Path is{IsAbsoluteUri:true,IsFile:true} uri?uri.LocalPath:null;
    public async Task<string?> OpenSubtitleAsync()
    {
        var files=await StorageProvider.OpenFilePickerAsync(new(){Title="Open subtitle",AllowMultiple=false,FileTypeFilter=[SubtitleFiles,FilePickerFileTypes.All]});return files.Count>0?LocalPath(files[0]):null;
    }
    public async Task<string?> OpenMediaAsync()
    {
        var files=await StorageProvider.OpenFilePickerAsync(new(){Title="Open video/audio",AllowMultiple=false,FileTypeFilter=[MediaFiles,FilePickerFileTypes.All]});return files.Count>0?LocalPath(files[0]):null;
    }
    public async Task<string?> SaveSubtitleAsync(string suggestedName)
    {
        var file=await StorageProvider.SaveFilePickerAsync(new(){Title="Save subtitle",SuggestedFileName=suggestedName,DefaultExtension="ass",ShowOverwritePrompt=true,FileTypeChoices=[SubtitleFiles]});return file is null?null:LocalPath(file);
    }
    public async Task<UnsavedChoice> ConfirmUnsavedAsync(string title)
    {
        var result=await AskAsync("Unsaved changes",$"Save changes to {title}?",["Save","Discard","Cancel"]);return result==0?UnsavedChoice.Save:result==1?UnsavedChoice.Discard:UnsavedChoice.Cancel;
    }
    public async Task<bool> ConfirmRevertAsync()=>await AskAsync("Reload subtitle","Discard edits and reload the file from disk?",["Reload","Cancel"])==0;
    private async Task<int> AskAsync(string title,string message,string[] choices)
    {
        var dialog=new Window{Title=title,Width=440,SizeToContent=SizeToContent.Height,CanResize=false,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var panel=new StackPanel{Margin=new Thickness(18),Spacing=14};panel.Children.Add(new TextBlock{Text=message,TextWrapping=Avalonia.Media.TextWrapping.Wrap});
        var buttons=new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=8,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Right};
        for(var i=0;i<choices.Length;i++){var index=i;var button=new Button{Content=choices[i],IsDefault=i==0,IsCancel=i==choices.Length-1};button.Click+=(_,_)=>dialog.Close(index);buttons.Children.Add(button);}
        panel.Children.Add(buttons);dialog.Content=panel;return await dialog.ShowDialog<int?>(this) ?? -1;
    }
    public async Task<string?> ReadClipboardAsync()
    {
        if(Clipboard is null)return null;
        using var data=await Clipboard.TryGetDataAsync();
        return data is null?null:await data.TryGetTextAsync();
    }
    public async Task WriteClipboardAsync(string text){if(Clipboard is not null){var data=new DataTransfer();data.Add(DataTransferItem.CreateText(text));await Clipboard.SetDataAsync(data);await Clipboard.FlushAsync();}}
    public Task ShowStylesAsync(SubtitleEditor editor)=>new StylesWindow(editor,_model?.StyleLibraryPath, _model?.SelectedEvent,_model?.StyleSplitWeights,weights=>{if(_model is {} model)_=model.Registry.InvokeAsync("styles/layout",new(),weights);}).ShowDialog(this);
    public Task ShowScriptInfoAsync(SubtitleEditor editor)=>new ScriptInfoWindow(editor).ShowDialog(this);
    public Task ShowFindAsync(MainWindowViewModel model)=>new FindWindow(model).ShowDialog(this);
}
