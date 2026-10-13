using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Interactivity;
using Yoake.Core.Commands;
using Yoake.Core.Hotkeys;
using Yoake.UI.Commands;
using Avalonia.Threading;
using Yoake.Core.Subtitles;
using Yoake.UI.ViewModels;

namespace Yoake.UI;

public sealed class AttachmentsWindow : Window
{
    private readonly MainWindowViewModel _model;
    private readonly Guid _document;
    private readonly SubtitleEditor _editor;
    private readonly CancellationTokenSource _lifetime=new();
    private CancellationTokenSource? _listing;
    private readonly ListBox _list=new(){Name="AttachmentList",SelectionMode=SelectionMode.Multiple};
    private readonly TextBlock _status=new(){Name="AttachmentStatus",TextWrapping=Avalonia.Media.TextWrapping.Wrap};
    private readonly Dictionary<string,Button> _buttons=[];
    private AssAttachment[] _entries=[];
    private bool _busy,_closed;
    private long _listingRevision=-1,_listedRevision=-1;
    public AttachmentsWindow(MainWindowViewModel model,Guid document)
    {
        _model=model;_document=document;_editor=model.ActiveEditor??throw new InvalidOperationException("No subtitle document.");Name="AttachmentsDialog";Title="ASS Attachments";Width=800;Height=440;MinWidth=650;MinHeight=330;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var names=new NameScope();NameScope.SetNameScope(this,names);
        var root=new Grid{Margin=new Thickness(12),RowDefinitions=new("Auto,*,Auto,Auto"),RowSpacing=8};Content=root;
        root.Children.Add(new TextBlock{Text="Embedded filename · type · decoded size — malformed source remains untouched."});
        Grid.SetRow(_list,1);root.Children.Add(_list);Grid.SetRow(_status,2);root.Children.Add(_status);
        var row=new WrapPanel{Orientation=Orientation.Horizontal};Grid.SetRow(row,3);root.Children.Add(row);
        void Button(string name,string label,Func<Task> action)
        {
            var button=new Button{Name=name,Content=label,Margin=new Thickness(0,0,6,0)};names.Register(name,button);row.Children.Add(button);_buttons[name]=button;
            button.Click+=async(_,_)=>{try{await action();}catch(OperationCanceledException){ }catch(Exception error){_status.Text=error.Message;_model.Registry.ReportFailure("subtitle/attachment",error);}};
        }
        Button("AttachmentImport","Import…",async()=>
        {
            var request=await new AttachmentPathWindow(true).ShowDialog<AttachmentImportRequest?>(this);
            if(request is not null)await ActAsync("attachments/import",request);
        });
        Button("AttachmentExtract","Extract…",()=>ExtractAsync(Selected()));
        Button("AttachmentExtractAll","Extract All…",()=>ExtractAsync(_entries));
        Button("AttachmentRemove","Remove",()=>ActAsync("attachments/remove",Selected()));
        Button("AttachmentUndo","Undo",()=>ActAsync("attachments/undo",null));Button("AttachmentRedo","Redo",()=>ActAsync("attachments/redo",null));
        Button("AttachmentCancelOperation","Cancel operation",async()=>{await _model.Registry.InvokeAsync("attachments/cancel",new(_document));});
        Button("AttachmentClose","Close",()=>{Close();return Task.CompletedTask;});
        _buttons["AttachmentClose"].IsCancel=true;
        foreach(var control in new Control[]{_list,_status})names.Register(control.Name!,control);names.Complete();
        _list.SelectionChanged+=(_,_)=>Availability();
        _model.Registry.StateChanged+=StateChanged;
        Opened+=async(_,_)=>await RefreshAsync();
        Closed+=(_,_)=>{_closed=true;_listing?.Cancel();_lifetime.Cancel();_model.Registry.StateChanged-=StateChanged;_listing?.Dispose();_lifetime.Dispose();};
        Availability();
        AddHandler(InputElement.KeyDownEvent,async(_,args)=>
        {
            if(_busy||!OwnsDocument)return;
            var binding=_model.Hotkeys.Resolve(new(EditorKeyNames.FromEvent(args),EditorKeyNames.FromModifiers(args.KeyModifiers)),[HotkeyContext.Default]);
            var command=binding?.CommandId switch{CommandIds.EditUndo=>"attachments/undo",CommandIds.EditRedo=>"attachments/redo",_=>null};
            if(command is null||!_model.Registry.CanExecute(command,new(_document)))return;
            args.Handled=true;await ActAsync(command,null);
        },RoutingStrategies.Tunnel);
    }
    private AssAttachment[] Selected()=>_list.SelectedItems?.OfType<AssAttachment>().ToArray()??[];
    private void StateChanged(object? sender,EventArgs args)
    {
        if(!Dispatcher.UIThread.CheckAccess()){Dispatcher.UIThread.Post(()=>StateChanged(sender,args));return;}
        if(_closed)return;Availability();
        if(!OwnsDocument){_listing?.Cancel();_status.Text="The active document changed. Close this manager and reopen it in the intended tab.";}
        else if(!_busy&&!_model.AttachmentBusy&&_editor.Document.Revision!=_listedRevision&&_editor.Document.Revision!=_listingRevision)_=RefreshAsync();
    }
    private void Availability()
    {
        var owner=OwnsDocument;var ready=owner&&!_busy&&!_model.AttachmentBusy;
        _buttons["AttachmentImport"].IsEnabled=ready;
        _buttons["AttachmentRemove"].IsEnabled=ready&&Selected().Length>0;
        _buttons["AttachmentExtract"].IsEnabled=ready&&Selected().Length>0&&Selected().All(a=>a.CanExtract);
        _buttons["AttachmentExtractAll"].IsEnabled=ready&&_entries.Length>0&&_entries.All(a=>a.CanExtract);
        _buttons["AttachmentUndo"].IsEnabled=ready&&_model.Registry.CanExecute("attachments/undo",new(_document));
        _buttons["AttachmentRedo"].IsEnabled=ready&&_model.Registry.CanExecute("attachments/redo",new(_document));
        _buttons["AttachmentCancelOperation"].IsEnabled=owner&&_model.AttachmentBusy;
        if(_model.AttachmentBusy)_status.Text=_model.AttachmentStatus;
        else if(Selected() is {Length:1} selected)_status.Text=selected[0].ToString();
    }
    private async Task RefreshAsync()
    {
        if(!OwnsDocument)return;
        _listing?.Cancel();_listing?.Dispose();_listing=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);var cancellation=_listing;var revision=_editor.Document.Revision;_listingRevision=revision;
        try
        {
            var entries=await _model.ListAttachmentsAsync(_document,cancellation.Token);
            if(_closed||cancellation.IsCancellationRequested||!OwnsDocument||_editor.Document.Revision!=revision||!ReferenceEquals(_listing,cancellation))return;
            _listedRevision=revision;
            _entries=entries;_list.ItemsSource=entries;_list.SelectedIndex=entries.Length>0?0:-1;
            if(entries.Length==0)_status.Text="No attachments in this document.";
        }
        catch(OperationCanceledException){ }
        catch(Exception error){_status.Text=error.Message;_model.Registry.ReportFailure("subtitle/attachment",error);}
        Availability();
    }
    private async Task ActAsync(string command,object? parameter)
    {
        if(_busy||!OwnsDocument)return;_busy=true;Availability();string? error=null;
        try
        {
            var success=await _model.Registry.InvokeAsync(command,new(_document),parameter,_lifetime.Token);
            if(success)await RefreshAsync();else error=_model.SubtitleStatus;
        }
        finally{_busy=false;Availability();if(error is not null)_status.Text=error;}
    }
    private async Task ExtractAsync(AssAttachment[] selected)
    {
        var directory=await new AttachmentPathWindow(false).ShowDialog<string?>(this);
        if(directory is not null)await ActAsync("attachments/extract",new AttachmentExtractionRequest(selected,directory));
    }
    private bool OwnsDocument=>!_closed&&_model.ActiveDocumentId==_document&&ReferenceEquals(_model.ActiveEditor,_editor);
}
