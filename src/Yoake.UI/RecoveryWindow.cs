using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Yoake.Core.Subtitles;
using Yoake.UI.ViewModels;

namespace Yoake.UI;

public sealed class RecoveryWindow : Window
{
    private readonly MainWindowViewModel _model;
    private readonly CancellationTokenSource _loading = new();
    private readonly ListBox _files = new(){Name="RecoveryFiles"};
    private readonly ListBox _versions = new(){Name="RecoveryVersions"};
    private readonly TextBlock _status = new(){Name="RecoveryStatus",TextWrapping=Avalonia.Media.TextWrapping.Wrap};
    private readonly Button _open = new(){Name="RecoveryOpen",Content="Recover in new tab",IsDefault=true};
    private readonly Button _discard = new(){Name="RecoveryDiscard",Content="Discard version…"};
    private RecoveryEntry[] _entries=[];
    private bool _busy;
    public RecoveryWindow(MainWindowViewModel model)
    {
        _model=model;Name="RecoveryDialog";Title="Recover Subtitles";Width=850;Height=450;MinWidth=650;MinHeight=340;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var names=new NameScope();NameScope.SetNameScope(this,names);
        var root=new Grid{Margin=new Thickness(12),RowDefinitions=new("Auto,*,Auto,Auto"),RowSpacing=8};Content=root;
        root.Children.Add(new TextBlock{Text="Autosaved documents and versions — recovery always opens an unsaved tab."});
        var body=new Grid{ColumnDefinitions=new("*,*"),ColumnSpacing=8};Grid.SetRow(body,1);root.Children.Add(body);
        body.Children.Add(_files);Grid.SetColumn(_versions,1);body.Children.Add(_versions);
        Grid.SetRow(_status,2);root.Children.Add(_status);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,HorizontalAlignment=HorizontalAlignment.Right};Grid.SetRow(buttons,3);root.Children.Add(buttons);
        buttons.Children.Add(_discard);buttons.Children.Add(_open);
        var cancel=new Button{Name="RecoveryCancel",Content="Cancel",IsCancel=true};buttons.Children.Add(cancel);cancel.Click+=(_,_)=>Close();
        foreach(var control in new Control[]{_files,_versions,_status,_open,_discard,cancel})names.Register(control.Name!,control);names.Complete();
        _files.SelectionChanged+=(_,_)=>SelectFile();_versions.SelectionChanged+=(_,_)=>UpdateSelection();
        _open.Click+=async(_,_)=>await ActAsync("recovery/open-version");
        _discard.Click+=async(_,_)=>await ActAsync("recovery/discard-version");
        Opened+=async(_,_)=>await LoadAsync();Closed+=(_,_)=>{_loading.Cancel();_loading.Dispose();};UpdateSelection();
    }
    private async Task LoadAsync()
    {
        try
        {
            _entries=await _model.ListRecoveryAsync(_loading.Token);
            _files.ItemsSource=_entries.Select(e=>e.Group).Distinct().ToArray();_files.SelectedIndex=_entries.Length>0?0:-1;
            if(_entries.Length==0)_status.Text="No recovery versions in "+_model.RecoveryDirectory;
        }
        catch(OperationCanceledException){ }
        catch(Exception error){_status.Text=error.Message;_model.Registry.ReportFailure("subtitle/open/autosave",error);}
    }
    private void SelectFile()
    {
        _versions.ItemsSource=_entries.Where(e=>e.Group==_files.SelectedItem as string).ToArray();_versions.SelectedIndex=_versions.ItemCount>0?0:-1;UpdateSelection();
    }
    private void UpdateSelection()
    {
        var entry=_versions.SelectedItem as RecoveryEntry;_open.IsEnabled=!_busy&&entry?.CanRecover==true;_discard.IsEnabled=!_busy&&entry is not null;
        if(entry is {} selected)_status.Text=selected.Metadata is {} m?$"{m.OriginalPath??"Never saved"}\nDocument {m.DocumentId} · {selected.SavedFileComparison}\n{selected.Error??"Validated committed ASS and separate editor draft."}":selected.Error;
    }
    private async Task ActAsync(string command)
    {
        if(_busy||_versions.SelectedItem is not RecoveryEntry entry)return;_busy=true;UpdateSelection();
        try
        {
            var success=await _model.Registry.InvokeAsync(command,new(),entry.Path,_loading.Token);
            if(success&&command=="recovery/open-version"){Close();return;}
            if(success)await LoadAsync();else _status.Text=_model.SubtitleStatus;
        }
        finally{_busy=false;UpdateSelection();}
    }
}
