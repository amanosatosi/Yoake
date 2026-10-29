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
    private long _textSelectionGeneration;
    private MainWindowViewModel? _model;
    private bool _videoRegionShown = true;
    private GridLength _videoWidth = new(6, GridUnitType.Star), _textWidth = new(7, GridUnitType.Star);
    private bool _videoSplitterOverride;
    private bool _compactSubtitleLayout;
    private GridLength _mediaWorkspaceHeight = new(3, GridUnitType.Star);
    private GridLength _subtitleWorkspaceHeight = new(300);
    private Key _fastActorModifier;
    private Guid? _fastActorModifierDocument;
    private AssEvent? _fastActorModifierLine;
    private static readonly FilePickerFileType SubtitleFiles=new("ASS/SSA subtitles"){Patterns=["*.ass","*.ssa"]};
    private static readonly FilePickerFileType MediaFiles=new("Video/audio"){Patterns=["*.mkv","*.mp4","*.webm","*.avi","*.mov","*.m2ts","*.ts","*.mp3","*.flac","*.wav","*.m4a","*.ogg","*.opus"]};
    public MainWindow()
    {
        InitializeComponent();
        StartupDiagnostics.Checkpoint("MainWindow XAML initialized");
        if(OperatingSystem.IsWindows()){WindowDecorations=Avalonia.Controls.WindowDecorations.Full;ExtendClientAreaToDecorationsHint=true;ExtendClientAreaTitleBarHeightHint=36;}
        StartupDiagnostics.Checkpoint("MainWindow native decorations configured");
        SubtitleRows.LayoutUpdated+=(_,_)=>ApplyRowWidths();
        SubtitleRows.PropertyChanged+=(_,e)=>{if(e.Property==ItemsControl.ItemsSourceProperty)Dispatcher.UIThread.Post(SyncSelection);};
        SubtitleRows.AddHandler(PointerPressedEvent,GridDoubleClick,RoutingStrategies.Bubble,handledEventsToo:true);
        DataContextChanged+=(_,_)=>AttachModel();
        AddHandler(KeyDownEvent,HandleKey,RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent,HandleFastActorKeyUp,RoutingStrategies.Tunnel);
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, HandleFileDragOver, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DropEvent, HandleFileDrop, RoutingStrategies.Tunnel);
        Opened+=(_,_)=>{UpdateChrome();UpdateEmbeddedVideoHost();};
        Loaded+=(_,_)=>SyncSelection();
        Opened+=StartAutomation;
        LayoutUpdated+=(_,_)=>UpdateChrome();
        ScalingChanged+=(_,_)=>Dispatcher.UIThread.Post(()=>{UpdateChrome();RefreshVideoScaling();});
        PropertyChanged+=(_,e)=>{if(e.Property==WindowStateProperty)Dispatcher.UIThread.Post(UpdateChrome);};
        TemporalTextColumn.SizeChanged+=(_,_)=>ClampAudioBounds();
        UpperWorkspace.SizeChanged+=(_,_)=>UpdateEmbeddedVideoHost();
        VideoRegion.SizeChanged+=(_,_)=>UpdateEmbeddedVideoHost();
        VideoSplitter.AddHandler(PointerPressedEvent,(_,_)=>_videoSplitterOverride=true,RoutingStrategies.Tunnel);
        VideoSplitter.AddHandler(PointerReleasedEvent,(_,_)=>{_videoSplitterOverride=true;UpdateEmbeddedVideoHost();},RoutingStrategies.Bubble);
        VideoSplitter.KeyUp+=(_,_)=>{_videoSplitterOverride=true;UpdateEmbeddedVideoHost();};
        FastActorToggle.Click+=(_,_)=>Dispatcher.UIThread.Post(()=>{if(_model?.FastActorEnabled==true)LineActor.Focus();UpdateFastActorPopup();});
        FastActorPlayback.ItemsSource=new[]{"Video","Audio","Off"};
        FastActorMode.ItemsSource=new[]{"Nanashi","Normal"};
        FastActorMode.SelectionChanged+=(_,_)=>{if(_model?.FastActorEnabled==true && FastActorMode.SelectedItem is string value && value!=_model.FastActorMode)_model.Actions["actor/fast/mode"].Execute(value);};
        FastActorPlayback.SelectionChanged+=(_,_)=>{if(_model?.FastActorEnabled==true && FastActorPlayback.SelectedItem is string value && value!=_model.FastActorPlayback)_model.Actions["actor/fast/playback"].Execute(value);};
        LineActor.AddHandler(GotFocusEvent,(_,_)=>Dispatcher.UIThread.Post(UpdateFastActorPopup),RoutingStrategies.Bubble);
        LineActor.AddHandler(LostFocusEvent,(_,_)=>{ClearFastActorModifier();Dispatcher.UIThread.Post(UpdateFastActorPopup);},RoutingStrategies.Bubble);
        FastActorList.AddHandler(GotFocusEvent,(_,_)=>Dispatcher.UIThread.Post(UpdateFastActorPopup),RoutingStrategies.Bubble);
        FastActorList.AddHandler(LostFocusEvent,(_,_)=>Dispatcher.UIThread.Post(UpdateFastActorPopup),RoutingStrategies.Bubble);
        FastActorPlayback.AddHandler(GotFocusEvent,(_,_)=>Dispatcher.UIThread.Post(UpdateFastActorPopup),RoutingStrategies.Bubble);
        FastActorPlayback.AddHandler(LostFocusEvent,(_,_)=>Dispatcher.UIThread.Post(UpdateFastActorPopup),RoutingStrategies.Bubble);
        FastActorMode.AddHandler(GotFocusEvent,(_,_)=>Dispatcher.UIThread.Post(UpdateFastActorPopup),RoutingStrategies.Bubble);
        FastActorMode.AddHandler(LostFocusEvent,(_,_)=>Dispatcher.UIThread.Post(UpdateFastActorPopup),RoutingStrategies.Bubble);
        FastActorList.SelectionChanged+=(_,_)=>
        {
            if(_model?.FastActorEnabled==true && FastActorList.SelectedIndex>=0 && FastActorList.SelectedIndex!=_model.FastActorSelectedIndex)
                _model.Actions["actor/fast/select"].Execute(FastActorList.SelectedIndex);
        };
        FastActorList.DoubleTapped+=(_,_)=>{if(_model?.FastActorEnabled==true && FastActorList.SelectedIndex>=0){_model.Actions["actor/fast/accept"].Execute(null);LineActor.Focus();}};
        FastActorList.AddHandler(KeyDownEvent,async(_,args)=>
        {
            if(_model?.FastActorEnabled!=true || args.KeyModifiers!=Avalonia.Input.KeyModifiers.None || args.Key is not (Key.Up or Key.Down or Key.Enter))return;
            args.Handled=true;await _model.Registry.InvokeAsync(args.Key==Key.Enter?"actor/fast/accept":"actor/fast/step",new(_model.ActiveDocumentId),args.Key==Key.Up?-1:1);LineActor.Focus();
        },RoutingStrategies.Tunnel);
        AudioSplitter.AddHandler(PointerReleasedEvent,(_,_)=>{if(_model is not null)_model.AudioDisplayHeight=TemporalTextColumn.RowDefinitions[0].ActualHeight;},RoutingStrategies.Tunnel);
        AudioSplitter.KeyUp+=(_,_)=>{if(_model is not null)_model.AudioDisplayHeight=TemporalTextColumn.RowDefinitions[0].ActualHeight;};
        TitleTabStrip.SizeChanged+=(_,_)=>UpdateTabLimit();
        SubtitleText.PropertyChanged+=(_,e)=>{if(_model is not null&&!SubtitleText.IsProjecting&&(e.Property==TextBox.CaretIndexProperty||e.Property==TextBox.SelectionStartProperty||e.Property==TextBox.SelectionEndProperty)){_textSelectionGeneration++;_model.TextCursor=SubtitleText.SourceIndex(SubtitleText.CaretIndex);_model.TextSelectionStart=SubtitleText.SourceIndex(SubtitleText.SelectionStart);_model.TextSelectionEnd=SubtitleText.SourceIndex(SubtitleText.SelectionEnd);}};
        Closing+=HandleClosing;
        Deactivated+=VideoHostDeactivated;
        Deactivated+=(_,_)=>ClearFastActorModifier();
        Closed+=(_,_)=>{MoveVideoHost(false);AudioDisplay.Model=null;_model?.Dispose();};
    }
    private static string[] DropPaths(DragEventArgs e)
    {
        // Only Explorer's local file URIs are meaningful here; never attempt
        // network/non-file URI loading or treat drag text as a file path.
        return (e.DataTransfer.TryGetFiles() ?? [])
            .Select(item => item.Path is { IsAbsoluteUri: true, IsFile: true } uri ? uri.LocalPath : null)
            .Where(path => path is not null)
            .Select(path => path!)
            .ToArray();
    }
    private void HandleFileDragOver(object? sender, DragEventArgs e)
    {
        var paths=DropPaths(e);
        e.DragEffects=paths.Length>0 && paths.All(FileDropPlanner.IsCandidate) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled=true;
    }
    private async void HandleFileDrop(object? sender, DragEventArgs e)
    {
        var paths=DropPaths(e);
        e.Handled=true;
        if(_model is not { } model)return;
        try
        {
            if(!FileDropPlanner.TryBuild(paths,out var files,out var error))
            {
                await ShowDropErrorAsync(error);return;
            }
            foreach(var file in files)
            {
                if(!ReferenceEquals(_model,model))return;
                var context=new CommandContext(model.ActiveDocumentId);
                var ok=file.Type switch
                {
                    DroppedFileType.Subtitle=>await model.Registry.InvokeAsync(CommandIds.SubtitleOpen,context,file.Path),
                    DroppedFileType.Video=>await OpenCombinedDropAsync(model,file.Path),
                    DroppedFileType.Audio=>await model.Registry.InvokeAsync("audio/open",context,file.Path),
                    DroppedFileType.Timecodes=>await model.Registry.InvokeAsync("timecode/open",context,file.Path),
                    DroppedFileType.Keyframes=>await model.Registry.InvokeAsync("keyframe/open",context,file.Path),
                    _=>false
                };
                if(!ok){await ShowDropErrorAsync("Could not open dropped file: "+Path.GetFileName(file.Path));return;}
            }
        }
        catch(Exception ex){await ShowDropErrorAsync(ex.Message);}
    }
    private static async Task<bool> OpenCombinedDropAsync(MainWindowViewModel model,string path)
        => await model.OpenMediaAsync(path);
    private async Task ShowDropErrorAsync(string message)
        => await AskAsync("File drop",message,["OK"]);

    private void AttachModel()
    {
        if(_model is not null){MoveVideoHost(false);_model.MotionBlockToggleRequested-=SubtitleText.ToggleMotionBlock;_model.VideoWindowZoomRequested-=ResizeDetachedVideo;_model.VideoSeekFocusRequested-=SeekFocusRequested;_model.FrameReady-=FrameReady;_model.TextFormattingApplied-=FormattingApplied;_model.SearchSelectionApplied-=SearchSelectionApplied;_model.PropertyChanged-=ModelChanged;_model.WindowCloseRequested-=CloseRequested;if(_model.SelectedEvents is INotifyCollectionChanged old)old.CollectionChanged-=SelectionChanged;}
        _model=DataContext as MainWindowViewModel;
        _automationMenuInitialized=false;
        RefreshRecentKeyframes();
        RefreshRecentMedia();
        RefreshRecentTimecodes();
        if(_model is not null){_model.Dialogs=this;_model.MotionBlockToggleRequested+=SubtitleText.ToggleMotionBlock;_model.VideoWindowZoomRequested+=ResizeDetachedVideo;_model.VideoSeekFocusRequested+=SeekFocusRequested;_model.FrameReady+=FrameReady;_model.TextFormattingApplied+=FormattingApplied;_model.SearchSelectionApplied+=SearchSelectionApplied;
            var widths=_model.GridColumnWidths; // Normalized at the model boundary.
            var count=Math.Min(widths.Count,ColumnHeader.ColumnDefinitions.Count);
            for(var i=0;i<count;i++)ColumnHeader.ColumnDefinitions[i].Width=new GridLength(widths[i]);
            UpdateWorkspaceView();UpdateEmbeddedVideoHost();
            _model.PropertyChanged+=ModelChanged;_model.WindowCloseRequested+=CloseRequested;if(_model.SelectedEvents is INotifyCollectionChanged collection)collection.CollectionChanged+=SelectionChanged;
            Dispatcher.UIThread.Post(SyncSelection);
            RefreshAutomationMenu(); StartupDiagnostics.Checkpoint($"MainWindow model attached; {count} column widths applied");}
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
        _textSelectionGeneration++;
        if(_model is null)return;var start=SubtitleText.DisplayIndex(_model.TextSelectionStart);var end=SubtitleText.DisplayIndex(_model.TextSelectionEnd);var cursor=SubtitleText.DisplayIndex(_model.TextCursor);SubtitleText.Focus();SubtitleText.CaretIndex=cursor;SubtitleText.SelectionStart=start;SubtitleText.SelectionEnd=end;
    }
    private void SearchSelectionApplied(object? sender,EventArgs args)
    {
        if(_model is not {} model)return;
        var document=model.ActiveDocumentId;var line=model.SelectedEvent;var start=model.TextSelectionStart;var end=model.TextSelectionEnd;
        var generation=++_textSelectionGeneration;
        Dispatcher.UIThread.Post(()=>
        {
            if(generation!=_textSelectionGeneration||!ReferenceEquals(_model,model)||model.ActiveDocumentId!=document||!ReferenceEquals(model.SelectedEvent,line))return;
            // Apply after the new draft binding, retaining focus in the modeless
            // search window or the control which issued Find Next.
            SubtitleText.CaretIndex=SubtitleText.DisplayIndex(end);SubtitleText.SelectionStart=SubtitleText.DisplayIndex(start);SubtitleText.SelectionEnd=SubtitleText.DisplayIndex(end);
        });
    }
    private WeakReference<Control>? _previousVideoFocus;
    private void SeekFocusRequested(object? sender,VideoSeekFocusEventArgs args)
    {
        if(args.Toggle&&VideoSeekBar.IsKeyboardFocusWithin)
        {
            if(_previousVideoFocus?.TryGetTarget(out var previous)==true&&previous.IsEffectivelyVisible&&previous.IsEffectivelyEnabled)
                previous.Focus();
            return;
        }
        if(args.Toggle&&FocusManager?.GetFocusedElement() is Control focused)
            _previousVideoFocus=new(focused);
        if(_model?.ShowVideo!=true&&DetachedVideoWindow is null)_model?.Actions[CommandIds.ViewVideo].Execute(null);
        VideoSeekBar.Focus();
    }
    public Task<VideoJumpRequest?> ShowVideoJumpAsync(MainWindowViewModel model)=>new VideoJumpWindow(model).ShowDialog<VideoJumpRequest?>(this);
    public async Task ShowMotionTrackingAsync(MainWindowViewModel model, CancellationToken token)
    {
        var dialog = new MotionTrackingWindow(model);
        using var cancel = token.Register(() => Dispatcher.UIThread.Post(() => { if (dialog.IsVisible) dialog.Close(); }));
        await dialog.ShowDialog(this);
    }
    public async Task ShowShiftTimesAsync(MainWindowViewModel model)=>await new ShiftTimesWindow(model).ShowDialog(this);
    public async Task ShowAudioTimingOptionsAsync(MainWindowViewModel model)=>await new AudioTimingOptionsWindow(model).ShowDialog(this);
    public async Task ShowSelectLinesAsync(MainWindowViewModel model)=>await new SelectLinesWindow(model).ShowDialog(this);
    public Task<AssColor?> ChooseColorAsync(AssColor color)=>new AssColorDialog(color,_model?.RecentColorsPath).ShowDialog<AssColor?>(this);
    public async Task<FontChoice?> ChooseFontAsync(string family,string size)
    {
        var dialog=new Window{Name="SubtitleFontDialog",Title="Subtitle font",Width=380,SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var panel=new StackPanel{Margin=new Thickness(12),Spacing=6};var picker=new FontPicker{Name="SubtitleFontPicker",FontName=family};panel.Children.Add(picker);
        var number=new NumericUpDown{Minimum=0.1m,Maximum=10000,Increment=1,Value=decimal.TryParse(size,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value)?value:60};
        panel.Children.Add(new TextBlock{Text="Size"});panel.Children.Add(number);var apply=new Button{Content="Apply font",IsDefault=true};apply.Click+=(_,_)=>dialog.Close(new FontChoice(picker.FontName,(number.Value??60).ToString(System.Globalization.CultureInfo.InvariantCulture)));panel.Children.Add(apply);var cancel=new Button{Content="Cancel",IsCancel=true};cancel.Click+=(_,_)=>dialog.Close();panel.Children.Add(cancel);dialog.Content=panel;return await dialog.ShowDialog<FontChoice?>(this);
    }
    private void UpdateAudioBounds()
    {
        if(_model is null)return;
        if(!_model.ShowAudio)return;
        TemporalTextColumn.RowDefinitions[0].Height=new GridLength(_model.AudioDisplayHeight);ClampAudioBounds();
    }
    private void UpdateWorkspaceView()
    {
        if (_model is null) return;
        // Aegisub sizes the editor to its content when neither embedded video
        // nor audio is shown, giving the rest of the window to subtitle rows.
        // Preserve each mode's splitter position across transitions.
        if (UpperWorkspace.Parent is Grid workspace)
        {
            var compact = !_model.ShowEmbeddedVideo && !_model.ShowAudio;
            var upper = workspace.RowDefinitions[2];
            if (compact != _compactSubtitleLayout)
            {
                if (compact) { _mediaWorkspaceHeight=upper.Height; upper.MinHeight=280; upper.Height=_subtitleWorkspaceHeight; }
                else { _subtitleWorkspaceHeight=upper.Height; upper.MinHeight=444; upper.Height=_mediaWorkspaceHeight; }
                _compactSubtitleLayout=compact;
            }
        }
        var columns = UpperWorkspace.ColumnDefinitions;
        if (_videoRegionShown != _model.ShowEmbeddedVideo)
        {
            if (!_model.ShowEmbeddedVideo)
            {
                _videoWidth = columns[0].Width;
                _textWidth = columns[2].Width;
            }
            columns[0].MinWidth = _model.ShowEmbeddedVideo ? 340 : 0;
            columns[0].Width = _model.ShowEmbeddedVideo ? _videoWidth : new GridLength(0);
            columns[1].Width = new GridLength(_model.ShowEmbeddedVideo ? 4 : 0);
            columns[2].Width = _model.ShowEmbeddedVideo ? _textWidth : new GridLength(1, GridUnitType.Star);
            _videoRegionShown = _model.ShowEmbeddedVideo;
            _videoSplitterOverride = false;
        }
        var rows = TemporalTextColumn.RowDefinitions;
        rows[0].MinHeight = _model.ShowAudio ? 160 : 0;
        rows[0].Height = new GridLength(_model.ShowAudio ? _model.AudioDisplayHeight : 0);
        rows[1].Height = new GridLength(_model.ShowAudio ? 4 : 0);
        rows[2].Height = new GridLength(1, GridUnitType.Star);
        ClampAudioBounds();
        UpdateEmbeddedVideoHost();
    }
    private void ClampAudioBounds()
    {
        if (_model?.ShowAudio != true) return;
        var available=TemporalTextColumn.Bounds.Height;if(available<=0)return;
        var maximum=Math.Max(160,available-TemporalTextColumn.RowDefinitions[2].MinHeight-4);
        var row=TemporalTextColumn.RowDefinitions[0];
        var height=row.Height.IsAbsolute?row.Height.Value:row.ActualHeight;
        var clamped=Math.Clamp(height,160,maximum);if(Math.Abs(height-clamped)>0.1)row.Height=new GridLength(clamped);
    }
    private void FrameReady(object? sender,EventArgs e)=>VideoImage.InvalidateVisual();
    private void RefreshRecentTimecodes()
    {
        RecentTimecodesMenu.Items.Clear();
        var entries=_model?.RecentTimecodeItems??[];RecentTimecodesMenu.IsEnabled=entries.Count>0;
        foreach(var entry in entries)
        {
            var separator=entry.Path.LastIndexOfAny(['\\','/']);
            var label=separator>=0?entry.Path[(separator+1)..]+" · "+entry.Path[..separator]:entry.Path;
            var item=new MenuItem{Header=new TextBlock{Text=label,TextTrimming=Avalonia.Media.TextTrimming.CharacterEllipsis,MaxWidth=420},Command=entry.Command,InputGesture=entry.Command.InputGesture};
            ToolTip.SetTip(item,entry.Path);
            entry.Command.PropertyChanged+=(_,args)=>{if(args.PropertyName==nameof(Yoake.UI.Commands.RegistryCommand.InputGesture))item.InputGesture=entry.Command.InputGesture;};
            RecentTimecodesMenu.Items.Add(item);
        }
    }
    private void RefreshRecentKeyframes()
    {
        // Presentation only. Every entry invokes keyframe/open with its fixed
        // path through the shared semantic command adapter.
        RecentKeyframesMenu.Items.Clear();
        var entries=_model?.RecentKeyframeItems??[];RecentKeyframesMenu.IsEnabled=entries.Count>0;
        foreach(var entry in entries)
        {
            var separator=entry.Path.LastIndexOfAny(['\\','/']);
            var label=separator>=0?entry.Path[(separator+1)..]+" · "+entry.Path[..separator]:entry.Path;
            var item=new MenuItem{Header=new TextBlock{Text=label,TextTrimming=Avalonia.Media.TextTrimming.CharacterEllipsis,MaxWidth=420},Command=entry.Command,InputGesture=entry.Command.InputGesture};
            ToolTip.SetTip(item,entry.Path);
            entry.Command.PropertyChanged+=(_,args)=>{if(args.PropertyName==nameof(Yoake.UI.Commands.RegistryCommand.InputGesture))item.InputGesture=entry.Command.InputGesture;};
            RecentKeyframesMenu.Items.Add(item);
        }
    }
    private void RefreshRecentMedia()
    {
        foreach(var (menu,entries) in new[]{(RecentVideoMenu,_model?.RecentVideoItems??[]),(RecentAudioMenu,_model?.RecentAudioItems??[])})
        {
            menu.Items.Clear();menu.IsEnabled=entries.Count>0;
            foreach(var entry in entries)
            {
                var item=new MenuItem{Header=new TextBlock{Text=Path.GetFileName(entry.Path)+" · "+Path.GetDirectoryName(entry.Path),TextTrimming=Avalonia.Media.TextTrimming.CharacterEllipsis,MaxWidth=420},Command=entry.Command,InputGesture=entry.Command.InputGesture};
                ToolTip.SetTip(item,entry.Path);
                entry.Command.PropertyChanged+=(_,args)=>{if(args.PropertyName==nameof(Yoake.UI.Commands.RegistryCommand.InputGesture))item.InputGesture=entry.Command.InputGesture;};
                menu.Items.Add(item);
            }
        }
    }
    private void ModelChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName==nameof(MainWindowViewModel.FastActorEnabled))Dispatcher.UIThread.Post(UpdateFastActorPopup);
        if(e.PropertyName is nameof(MainWindowViewModel.RecentVideoItems) or nameof(MainWindowViewModel.RecentAudioItems))Dispatcher.UIThread.Post(RefreshRecentMedia);
        if(e.PropertyName==nameof(MainWindowViewModel.AutomationMacros))RefreshAutomationMenu();
        if(e.PropertyName==nameof(MainWindowViewModel.RecentKeyframeItems))Dispatcher.UIThread.Post(RefreshRecentKeyframes);
        if(e.PropertyName==nameof(MainWindowViewModel.RecentTimecodeItems))Dispatcher.UIThread.Post(RefreshRecentTimecodes);
        if(e.PropertyName==nameof(MainWindowViewModel.AudioDisplayHeight))UpdateAudioBounds();
        if(e.PropertyName==nameof(MainWindowViewModel.ViewMode)){VisualOverlay.CancelInput();_model?.NotifyVideoPresentation();UpdateWorkspaceView();}
        if(e.PropertyName is nameof(MainWindowViewModel.IsVideoDetached) or nameof(MainWindowViewModel.VideoSourcePath))SynchronizeVideoHost();
        if(e.PropertyName is nameof(MainWindowViewModel.VideoAspectRatio) or nameof(MainWindowViewModel.VideoSourcePath))UpdateEmbeddedVideoHost();
        if(e.PropertyName==nameof(MainWindowViewModel.SelectedEvent)){SubtitleText.ResetMotionExpansions();Dispatcher.UIThread.Post(()=>{if(_model?.SelectedEvent is {} line){SubtitleRows.ScrollIntoView(line);SyncSelection();}});}
    }
    private void SelectionChanged(object? sender,NotifyCollectionChangedEventArgs e) { if(!_selectionSync)SyncSelection(); }
    private void SyncSelection()
    {
        if(_model is null||_selectionSync||SubtitleRows.SelectedItems is null||!ReferenceEquals(SubtitleRows.ItemsSource,_model.Events))return;
        var current=SubtitleRows.SelectedItems.OfType<AssEvent>().ToArray();
        if(current.Length==_model.SelectedEvents.Count&&current.ToHashSet().SetEquals(_model.SelectedEvents))return;
        _selectionSync=true;
        try{var owned=_model.Events.ToHashSet();SubtitleRows.SelectedItems=_model.SelectedEvents.Where(owned.Contains).ToList();}
        finally{_selectionSync=false;}
    }
    private void GridSelectionChanged(object? sender,SelectionChangedEventArgs e)
    {
        if(_model is null||_model.IsSynchronizingSelection||_selectionSync||SubtitleRows.SelectedItems is null||!ReferenceEquals(SubtitleRows.ItemsSource,_model.Events))return;
        _selectionSync=true;
        try
        {
            // Changing the active row can update the model selection and trigger
            // binding/layout callbacks. Preserve the grid's complete selection
            // before that transition rather than reading a potentially changed list.
            var selected=SubtitleRows.SelectedItems.OfType<AssEvent>().ToArray();
            var next=e.AddedItems.OfType<AssEvent>().LastOrDefault()??e.RemovedItems.OfType<AssEvent>().LastOrDefault()??selected.FirstOrDefault();
            _model.SelectedEvent=next;
            if(_model.SelectedEvent!=next){Dispatcher.UIThread.Post(SyncSelection);return;}
            _model.SetSelectedEvents(selected);
        }
        finally{_selectionSync=false;}
    }
    private async void GridDoubleClick(object? sender,PointerPressedEventArgs e)
    {
        if(e.ClickCount!=2||!e.GetCurrentPoint(SubtitleRows).Properties.IsLeftButtonPressed||_model is not {} model||e.Source is not Visual source)return;
        var item=source as ListBoxItem ?? source.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
        if(item?.DataContext is not AssEvent line||!model.Events.Contains(line))return;
        model.SelectedEvent=line;
        if(!ReferenceEquals(model.SelectedEvent,line))return;
        await model.Registry.InvokeAsync(CommandIds.TimingJumpStart,new(model.ActiveDocumentId));
    }
    private async void HandleKey(object? sender,KeyEventArgs e)
    {
        TrackFastActorModifier(e);
        if(_model is null||e.Handled)return;
        var control=e.Source as Control;var context=HotkeyContext.Default;
        var ancestors=control is null?Array.Empty<Control>():control.GetVisualAncestors().OfType<Control>().Prepend(control).ToArray();
        if(e.Key is Key.Enter or Key.Escape && ancestors.Any(input=>input is VideoZoomControl))return;
        // Captured edit gestures and point/box selections own cancellation.
        // An idle overlay routes Escape normally through the Video context.
        if(e.Key==Key.Escape&&control is VisualOverlayControl {HasTransientInput:true})return;
        if(e.Key==Key.Escape&&_model.IsKaraokeMode&&_model.HasGesture&&ancestors.Contains(AudioRegion))return;
        foreach(var input in ancestors)
        {
            if(input is TextBox textBox && textBox.GetVisualDescendants().OfType<TextPresenter>().Any(p=>!string.IsNullOrEmpty(p.PreeditText)))return;
            if(e.Key==Key.Enter&&(input is AutoCompleteBox {IsDropDownOpen:true}||input is ComboBox {IsDropDownOpen:true}))return;
        }
        if(_model.FastActorEnabled && (LineActor.IsKeyboardFocusWithin||FastActorList.IsKeyboardFocusWithin) && e.KeyModifiers==Avalonia.Input.KeyModifiers.None && e.Key is Key.Up or Key.Down or Key.Enter)
        {
            if(LineActor.GetVisualDescendants().OfType<TextPresenter>().Any(p=>!string.IsNullOrEmpty(p.PreeditText)))return;
            LineActor.IsDropDownOpen=false;e.Handled=true;
            await _model.Registry.InvokeAsync(e.Key==Key.Enter?"actor/fast/accept":"actor/fast/step",new(_model.ActiveDocumentId),e.Key==Key.Up?-1:1);
            LineActor.Focus();return;
        }
        // A focused semantic button owns ordinary Enter. Treating it as the
        // editor's commit/next shortcut prevents keyboard activation of the
        // Original actions (and other authoring toolbar controls).
        if(e.Key==Key.Enter&&e.KeyModifiers==Avalonia.Input.KeyModifiers.None&&ancestors.Any(input=>input is Button))return;
        foreach(var current in ancestors)
        {
            if(current==SubtitleRows){context=HotkeyContext.SubtitleGrid;break;}
            if(current==EventEditorRegion){context=HotkeyContext.SubtitleEdit;break;}
            if(current==AudioRegion||current==AudioHost){context=HotkeyContext.Audio;break;}
            if(current==VideoRegion){context=HotkeyContext.Video;break;}
        }
        // Focus is authoritative across template/presenter boundaries.
        if(SubtitleText.IsKeyboardFocusWithin)context=HotkeyContext.SubtitleEdit;
        if(SubtitleText.IsKeyboardFocusWithin&&SubtitleText.GetVisualDescendants().OfType<TextPresenter>().Any(p=>!string.IsNullOrEmpty(p.PreeditText)))return;
        var modifiers=Yoake.Core.Hotkeys.KeyModifiers.None;
        if(e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))modifiers|=Yoake.Core.Hotkeys.KeyModifiers.Control;
        if(e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift))modifiers|=Yoake.Core.Hotkeys.KeyModifiers.Shift;
        if(e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt))modifiers|=Yoake.Core.Hotkeys.KeyModifiers.Alt;
        if(e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Meta))modifiers|=Yoake.Core.Hotkeys.KeyModifiers.Meta;
        // Enter in metadata commits, without advancing while a dropdown is open.
        var gesture=new HotkeyGesture(Yoake.UI.Commands.EditorKeyNames.FromEvent(e),modifiers);
        var binding=_model.Hotkeys.Resolve(gesture,[HotkeyContext.Default,context],_model.GlobalTimingHotkeys);
        if(binding is not null&&!HotkeyInputPolicy.MayRoute(binding,gesture,ancestors.Any(input=>input is TextBox or AutoCompleteBox)))return;
        var commandId=e.Key==Key.Escape&&_model.HasGesture?CommandIds.EditCancel:binding?.CommandId;
        if(commandId is null)return;
        if(commandId==CommandIds.TextHardNewline&&!SubtitleText.IsKeyboardFocusWithin)return;
        if(commandId==CommandIds.EditCommitNext && context==HotkeyContext.SubtitleEdit && !SubtitleText.IsKeyboardFocusWithin)commandId=CommandIds.EditCommit;
        _model.TextCursor=SubtitleText.SourceIndex(SubtitleText.CaretIndex);
        _model.TextSelectionStart=SubtitleText.SourceIndex(SubtitleText.SelectionStart);_model.TextSelectionEnd=SubtitleText.SourceIndex(SubtitleText.SelectionEnd);
        e.Handled=true;
        await _model.Registry.InvokeAsync(commandId,new(_model.ActiveDocumentId,FocusContext:context.ToString()));
    }
    private void ClearFastActorModifier(){_fastActorModifier=Key.None;_fastActorModifierDocument=null;_fastActorModifierLine=null;}
    private bool ActorHasPreedit()=>LineActor.GetVisualDescendants().OfType<TextPresenter>().Any(p=>!string.IsNullOrEmpty(p.PreeditText));
    private void TrackFastActorModifier(KeyEventArgs e)
    {
        if(_model?.FastActorEnabled!=true||_model.FastActorMode!="Nanashi"||!LineActor.IsKeyboardFocusWithin||ActorHasPreedit()) {ClearFastActorModifier();return;}
        var control=e.Key is Key.LeftCtrl or Key.RightCtrl;
        var alt=e.Key is Key.LeftAlt or Key.RightAlt;
        var expected=control?Avalonia.Input.KeyModifiers.Control:alt?Avalonia.Input.KeyModifiers.Alt:Avalonia.Input.KeyModifiers.None;
        if(expected==Avalonia.Input.KeyModifiers.None || (e.KeyModifiers & ~expected)!=0){ClearFastActorModifier();return;}
        if(_fastActorModifier!=Key.None && _fastActorModifier!=e.Key){ClearFastActorModifier();return;}
        _fastActorModifier=e.Key;_fastActorModifierDocument=_model.ActiveDocumentId;_fastActorModifierLine=_model.SelectedEvent;
    }
    private async void HandleFastActorKeyUp(object? sender,KeyEventArgs e)
    {
        var key=_fastActorModifier;var document=_fastActorModifierDocument;var line=_fastActorModifierLine;
        ClearFastActorModifier();
        if(key==Key.None||e.Key!=key||_model?.FastActorEnabled!=true||_model.FastActorMode!="Nanashi"||!LineActor.IsKeyboardFocusWithin||ActorHasPreedit()||_model.ActiveDocumentId!=document||!ReferenceEquals(_model.SelectedEvent,line))return;
        var expected=key is Key.LeftCtrl or Key.RightCtrl?Avalonia.Input.KeyModifiers.Control:Avalonia.Input.KeyModifiers.Alt;
        if((e.KeyModifiers & ~expected)!=0)return;
        e.Handled=true;
        await _model.Registry.InvokeAsync(expected==Avalonia.Input.KeyModifiers.Control?"actor/fast/accept":"actor/fast/previous",new(document));
        LineActor.Focus();
    }
    private void UpdateFastActorPopup()
    {
        var enabled=_model?.FastActorEnabled==true;
        LineActor.MinimumPrefixLength=enabled?int.MaxValue:1;
        if(enabled)LineActor.IsDropDownOpen=false;
        FastActorPopup.IsOpen=enabled&&(LineActor.IsKeyboardFocusWithin||FastActorList.IsKeyboardFocusWithin||FastActorPlayback.IsKeyboardFocusWithin||FastActorPlayback.IsDropDownOpen||FastActorMode.IsKeyboardFocusWithin||FastActorMode.IsDropDownOpen);
    }
    private async void HandleClosing(object? sender,WindowClosingEventArgs e)
    {
        if(_closingAllowed||_model is null)return;e.Cancel=true;if(_closingPending)return;_closingPending=true;
        try{await _model.Registry.InvokeAsync(CommandIds.AppExit,new(_model.ActiveDocumentId));}
        catch(Exception exception){_model.Registry.ReportFailure("window/close",exception);}
        finally{_closingPending=false;}
    }
    private void CloseRequested(object? sender,EventArgs args)
    {
        if(!Dispatcher.UIThread.CheckAccess()){Dispatcher.UIThread.Post(()=>CloseRequested(sender,args));return;}
        _closingAllowed=true;Close();
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
        // The title strip is taller than Fluent's default 30-DIP caption
        // buttons. Match the existing framework buttons to the strip so the
        // top of a maximized window remains clickable, without replacing
        // their caption roles or native window behavior.
        if(buttons is Panel panel && panel.Bounds.Height>0)
            foreach(var button in panel.Children.OfType<Button>())
                if(Math.Abs(button.Height-panel.Bounds.Height)>0.1 || double.IsNaN(button.Height))button.Height=panel.Bounds.Height;
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
    public Task<string?> OpenVideoAsync()=>OpenMediaTrackPickerAsync(true);
    public Task<string?> OpenAudioAsync()=>OpenMediaTrackPickerAsync(false);
    private async Task<string?> OpenMediaTrackPickerAsync(bool video)
    {
        var types=new FilePickerFileType(video?"Video":"Audio"){Patterns=video?["*.avi","*.mkv","*.mp4","*.mov","*.webm","*.wmv","*.mpeg","*.mpg","*.m2ts","*.ts","*.y4m"]:["*.wav","*.flac","*.mp3","*.aac","*.m4a","*.opus","*.ogg","*.mkv","*.mp4","*.avi"]};
        var files=await StorageProvider.OpenFilePickerAsync(new(){Title=video?"Open video":"Open audio",AllowMultiple=false,FileTypeFilter=[types,FilePickerFileTypes.All]});return files.Count>0?LocalPath(files[0]):null;
    }
    public async Task<string?> OpenKeyframesAsync()
    {
        var types=new FilePickerFileType("Keyframe lists and encoder logs"){Patterns=["*.txt","*.pass","*.stats","*.log"]};
        var files=await StorageProvider.OpenFilePickerAsync(new(){Title="Open keyframes",AllowMultiple=false,FileTypeFilter=[types,FilePickerFileTypes.All]});return files.Count>0?LocalPath(files[0]):null;
    }
    public async Task<string?> OpenTimecodesAsync()
    {
        var types=new FilePickerFileType("Timecodes v1/v2"){Patterns=["*.txt","*.tc","*.timecodes"]};
        var files=await StorageProvider.OpenFilePickerAsync(new(){Title="Open timecodes",AllowMultiple=false,FileTypeFilter=[types,FilePickerFileTypes.All]});return files.Count>0?LocalPath(files[0]):null;
    }
    public async Task<string?> SaveTimecodesAsync(string suggestedName)
    {
        var file=await StorageProvider.SaveFilePickerAsync(new(){Title="Save timecodes",SuggestedFileName=suggestedName,DefaultExtension="txt",ShowOverwritePrompt=true,FileTypeChoices=[new FilePickerFileType("Timecodes v2"){Patterns=["*.txt"]}]});return file is null?null:LocalPath(file);
    }
    public async Task<string?> SaveKeyframesAsync(string suggestedName)
    {
        var file=await StorageProvider.SaveFilePickerAsync(new(){Title="Save keyframes",SuggestedFileName=suggestedName,DefaultExtension="txt",ShowOverwritePrompt=true,FileTypeChoices=[new FilePickerFileType("Keyframe list"){Patterns=["*.txt"]}]});return file is null?null:LocalPath(file);
    }
    public async Task<string?> SaveSubtitleAsync(string suggestedName)
    {
        var file=await StorageProvider.SaveFilePickerAsync(new(){Title="Save subtitle",SuggestedFileName=suggestedName,DefaultExtension="ass",ShowOverwritePrompt=true,FileTypeChoices=[SubtitleFiles]});return file is null?null:LocalPath(file);
    }
    public async Task<string?> SaveAudioClipAsync(string suggestedName)
    {
        var file=await StorageProvider.SaveFilePickerAsync(new(){Title="Save selected-line audio clip",SuggestedFileName=suggestedName,DefaultExtension="wav",ShowOverwritePrompt=true,FileTypeChoices=[new FilePickerFileType("PCM audio"){Patterns=["*.wav"]}]});return file is null?null:LocalPath(file);
    }
    public async Task<UnsavedChoice> ConfirmUnsavedAsync(string title)
    {
        var result=await AskAsync("Unsaved changes",$"Save changes to {title}?",["Save","Discard","Cancel"]);return result==0?UnsavedChoice.Save:result==1?UnsavedChoice.Discard:UnsavedChoice.Cancel;
    }
    public async Task<bool> ConfirmRevertAsync()=>await AskAsync("Reload subtitle","Discard edits and reload the file from disk?",["Reload","Cancel"])==0;
    public async Task ShowRecoveryAsync(MainWindowViewModel model,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();var dialog=new RecoveryWindow(model);
        using var registration=token.Register(()=>Avalonia.Threading.Dispatcher.UIThread.Post(()=>{if(dialog.IsVisible)dialog.Close();}));
        await dialog.ShowDialog(this);
    }
    public async Task ShowAttachmentsAsync(MainWindowViewModel model,Guid id,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();var dialog=new AttachmentsWindow(model,id);
        using var registration=token.Register(()=>Dispatcher.UIThread.Post(()=>{if(dialog.IsVisible)dialog.Close();}));
        await dialog.ShowDialog(this);
    }
    public async Task ShowResampleAsync(MainWindowViewModel model,Guid id,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();var dialog=new ResampleWindow(model,id);
        using var registration=token.Register(()=>Dispatcher.UIThread.Post(()=>{if(dialog.IsVisible)dialog.Close();}));
        await dialog.ShowDialog(this);
    }
    public async Task ShowFontCollectorAsync(MainWindowViewModel model,Guid id,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();var dialog=new FontCollectorWindow(model,id);
        using var registration=token.Register(()=>Dispatcher.UIThread.Post(()=>{if(dialog.IsVisible)dialog.Close();}));
        await dialog.ShowDialog(this);
    }
    public async Task ShowAssDrawOptionsAsync(MainWindowViewModel model)=>await new OptionsWindow(model,true).ShowDialog(this);
    public async Task<bool> ShowAssDrawMissingAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();var dialog=new Window{Name="AssDrawMissingDialog",Title="ASSDraw3",Width=440,SizeToContent=SizeToContent.Height,CanResize=false,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var names=new NameScope();NameScope.SetNameScope(dialog,names);var panel=new StackPanel{Margin=new Thickness(12),Spacing=12};dialog.Content=panel;
        var text=new TextBlock{Name="AssDrawMissingMessage",Text=Yoake.Core.Tools.AssDrawLauncher.MissingMessage};panel.Children.Add(text);names.Register(text.Name!,text);
        var buttons=new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=8,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Right};panel.Children.Add(buttons);
        // Enter should open configuration, while Escape dismisses the dialog.
        // Giving Close both IsDefault and IsCancel caused Enter to dismiss the
        // dialog instead of proceeding to the External tools options window.
        var configure=new Button{Name="AssDrawConfigure",Content="Configure…",IsDefault=true};
        var close=new Button{Name="AssDrawMissingClose",Content="Close",IsCancel=true};
        foreach(var button in new[]{configure,close}){buttons.Children.Add(button);names.Register(button.Name!,button);}configure.Click+=(sender,args)=>dialog.Close(true);close.Click+=(sender,args)=>dialog.Close(false);names.Complete();
        using var registration=token.Register(()=>Dispatcher.UIThread.Post(()=>{if(dialog.IsVisible)dialog.Close(false);}));return await dialog.ShowDialog<bool>(this);
    }
    public async Task<bool> ConfirmAttachmentOverwriteAsync(string path,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();var dialog=new Window{Name="AttachmentOverwriteDialog",Title="Replace extracted file?",Width=480,SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var names=new NameScope();NameScope.SetNameScope(dialog,names);var panel=new StackPanel{Margin=new Thickness(12),Spacing=8};dialog.Content=panel;
        panel.Children.Add(new TextBlock{Text="Replace the existing file?\n"+path,TextWrapping=Avalonia.Media.TextWrapping.Wrap});
        var row=new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=8};panel.Children.Add(row);
        var cancel=new Button{Name="AttachmentOverwriteCancel",Content="Keep existing",IsDefault=true,IsCancel=true};var replace=new Button{Name="AttachmentOverwriteConfirm",Content="Replace"};row.Children.Add(cancel);row.Children.Add(replace);
        names.Register(cancel.Name!,cancel);names.Register(replace.Name!,replace);names.Complete();cancel.Click+=(_,_)=>dialog.Close(false);replace.Click+=(_,_)=>dialog.Close(true);
        using var registration=token.Register(()=>Dispatcher.UIThread.Post(()=>{if(dialog.IsVisible)dialog.Close(false);}));
        return await dialog.ShowDialog<bool>(this);
    }
    public async Task<bool> ConfirmRecoveryDiscardAsync(string name,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var answer=await AskAsync("Discard recovery version",$"Permanently discard {name}? This may be the only recoverable copy.",["Cancel","Discard"]);
        token.ThrowIfCancellationRequested();return answer==1;
    }
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
    public async Task WriteClipboardAsync(string text){var clipboard=Clipboard??throw new InvalidOperationException("Clipboard is unavailable.");var data=new DataTransfer();data.Add(DataTransferItem.CreateText(text));await clipboard.SetDataAsync(data);await clipboard.FlushAsync();}
    public async Task WriteFrameClipboardAsync(Yoake.Native.DecodedVideoFrame image, Func<bool> isCurrent, CancellationToken token)
    {
        var clipboard = Clipboard ?? throw new InvalidOperationException("Image clipboard is unavailable.");
        var bitmap = await Task.Run(() => MainWindowViewModel.CreateBitmap(image), token);
        var data = new FrameClipboardTransfer(bitmap, isCurrent, token); var transferred=false;
        try
        {
            token.ThrowIfCancellationRequested();
            if (!isCurrent()) throw new OperationCanceledException("The video frame changed before copying.");
            await clipboard.SetDataAsync(data); transferred=true;
            token.ThrowIfCancellationRequested();
            if (!isCurrent()) throw new OperationCanceledException("The video frame changed during clipboard retries.");
            await clipboard.FlushAsync();
            token.ThrowIfCancellationRequested();
            if (!isCurrent()) throw new OperationCanceledException("The video frame changed before clipboard rendering.");
            data.Accept();
        }
        finally { if (!transferred) data.Dispose(); }
        // Successful SetData transfers ownership to Avalonia. In particular, a
        // failed Flush must leave a usable delayed-rendering bitmap until the
        // platform disposes the transfer, rather than a disposed image payload.
    }
    public Task<Yoake.Core.Media.DummyVideoOptions?> ShowDummyVideoAsync(Yoake.Core.Media.DummyVideoOptions initial) => new DummyVideoWindow(initial,e=>_model?.Registry.ReportFailure("video/dummy-color",e)).ShowDialog<Yoake.Core.Media.DummyVideoOptions?>(this);
    public Task<string?> ShowCustomAspectAsync(double current) => new VideoAspectWindow(current).ShowDialog<string?>(this);
    public async Task ShowVideoDetailsAsync(VideoDetails details) => await new VideoDetailsWindow(details).ShowDialog(this);
    public async Task<PasteOverFields?> ShowPasteOverAsync(PasteOverFields initial,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();var dialog=new PasteOverWindow(initial);
        using var cancel=token.Register(()=>Dispatcher.UIThread.Post(()=>{if(dialog.IsVisible)dialog.Close(null);}));
        return await dialog.ShowDialog<PasteOverFields?>(this);
    }
    public Task ShowStylesAsync(SubtitleEditor editor)=>new StylesWindow(editor,_model?.StyleLibraryPath, _model?.SelectedEvent,_model?.StyleSplitWeights,weights=>{if(_model is {} model)_=model.Registry.InvokeAsync("styles/layout",new(),weights);}).ShowDialog(this);
    public Task ShowScriptInfoAsync(SubtitleEditor editor)=>new ScriptInfoWindow(editor).ShowDialog(this);
    public Task ShowFindAsync(MainWindowViewModel model)=>ShowFindAsync(model,false);
    public Task ShowFindAsync(MainWindowViewModel model,bool focusReplacement)
    {
        var existing=OwnedWindows.OfType<FindWindow>().FirstOrDefault(window=>window.DocumentId==model.ActiveDocumentId);
        if(existing is not null)existing.FocusSearch(focusReplacement);
        else new FindWindow(model,focusReplacement).Show(this);
        return Task.CompletedTask;
    }
}
