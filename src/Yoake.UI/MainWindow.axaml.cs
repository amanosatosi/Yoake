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
using Yoake.Core.Subtitles;
using Yoake.UI.Services;
using Yoake.UI.ViewModels;

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
        if(OperatingSystem.IsWindows()){WindowDecorations=Avalonia.Controls.WindowDecorations.Full;ExtendClientAreaToDecorationsHint=true;ExtendClientAreaTitleBarHeightHint=36;TitleTabStrip.Padding=new Thickness(0,0,140,0);}
        DataContextChanged+=(_,_)=>AttachModel();
        AddHandler(KeyDownEvent,HandleKey,RoutingStrategies.Tunnel);
        SubtitleText.PropertyChanged+=(_,e)=>{if(_model is not null&&e.Property==TextBox.CaretIndexProperty)_model.TextCursor=SubtitleText.CaretIndex;};
        Closing+=HandleClosing;
        Closed+=(_,_)=>_model?.Dispose();
    }
    private void AttachModel()
    {
        if(_model is not null){_model.FrameReady-=FrameReady;_model.PropertyChanged-=ModelChanged;if(_model.SelectedEvents is INotifyCollectionChanged old)old.CollectionChanged-=SelectionChanged;}
        _model=DataContext as MainWindowViewModel;
        if(_model is not null){_model.Dialogs=this;_model.FrameReady+=FrameReady;_model.PropertyChanged+=ModelChanged;if(_model.SelectedEvents is INotifyCollectionChanged collection)collection.CollectionChanged+=SelectionChanged;}
    }
    private void FrameReady(object? sender,EventArgs e)=>VideoImage.InvalidateVisual();
    private void ModelChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName==nameof(MainWindowViewModel.SelectedEvent))Dispatcher.UIThread.Post(()=>{if(_model?.SelectedEvent is {} line){SubtitleRows.ScrollIntoView(line);SyncSelection();}});
    }
    private void SelectionChanged(object? sender,NotifyCollectionChangedEventArgs e) { if(!_selectionSync)SyncSelection(); }
    private void SyncSelection()
    {
        if(_model is null||_selectionSync||SubtitleRows.SelectedItems is null)return;
        _selectionSync=true;
        try{SubtitleRows.SelectedItems.Clear();foreach(var line in _model.SelectedEvents)if(_model.Events.Contains(line))SubtitleRows.SelectedItems.Add(line);}
        finally{_selectionSync=false;}
    }
    private void GridSelectionChanged(object? sender,SelectionChangedEventArgs e)
    {
        if(_model is null||_selectionSync||SubtitleRows.SelectedItems is null)return;
        _selectionSync=true;
        try{_model.SelectedEvents.Clear();foreach(var line in SubtitleRows.SelectedItems.OfType<AssEvent>())_model.SelectedEvents.Add(line);}
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
    private async void VideoSeekReleased(object? sender,PointerReleasedEventArgs e)
    {
        if(_model is null)return;
        try{await _model.SeekPlaybackAsync(_model.CurrentTimeSeconds);}catch(Exception exception){_model.Registry.ReportFailure(CommandIds.VideoSeek,exception);}
    }
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
    public Task ShowStylesAsync(SubtitleEditor editor)=>new StylesWindow(editor).ShowDialog(this);
    public Task ShowScriptInfoAsync(SubtitleEditor editor)=>new ScriptInfoWindow(editor).ShowDialog(this);
    public Task ShowFindAsync(MainWindowViewModel model)=>new FindWindow(model).ShowDialog(this);
}
