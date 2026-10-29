using Avalonia.Threading;
using Yoake.Core.Subtitles;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed record AttachmentImportRequest(AssAttachmentKind Kind,string[] Paths);
public sealed record AttachmentExtractionRequest(AssAttachment[] Attachments,string Directory);

public sealed partial class MainWindowViewModel
{
    private bool _attachmentDialogOpen,_publishingAttachments;
    public string AttachmentStatus=>_activeId is {} id&&_documents.TryGetValue(id,out var state)?state.AttachmentStatus:"";
    public bool AttachmentBusy=>_activeId is {} id&&_documents.TryGetValue(id,out var state)&&state.AttachmentWork is not null;
    private void RegisterAttachmentCommands()
    {
        bool Owner(Yoake.Core.Commands.CommandContext context)=>!_disposed&&_activeId is {} id&&_documents.ContainsKey(id)&&(context.DocumentId is null||context.DocumentId==id);
        void Register(string id,string label,Func<Yoake.Core.Commands.CommandInvocation,CancellationToken,ValueTask> action,Func<bool>? available=null)
        {
            _registry.Register(new(new(id,label,label,"Subtitle"),action,c=>Owner(c)&&!HasGesture&&(available?.Invoke()??true)));
        }
        Register("subtitle/attachment","Attachments…",async (invocation,token)=>
        {
            if(_activeId is not {} id||Dialogs is null)return;var state=_documents[id];
            _attachmentDialogOpen=true;state.MediaDraftPauses++;_editBurstDelay?.Cancel();_registry.NotifyStateChanged();
            if(_media?.HasVideo==true&&IsPlaying)StopPlayback();
            try{await Dialogs.ShowAttachmentsAsync(this,id,token);}
            finally
            {
                CancelAttachmentWork(id);_attachmentDialogOpen=false;state.MediaDraftPauses--;
                if(!_disposed&&_activeId==id&&Draft?.IsChanged==true){_editBurstDelay?.Cancel();_editBurstDelay?.Dispose();_editBurstDelay=new();_=FinalizeBurstAsync(_editBurstDelay.Token);}
                _registry.NotifyStateChanged();
            }
        },()=>!_attachmentDialogOpen);
        Register("attachments/import","Import attachments",async (invocation,token)=>
        {
            if(invocation.Parameter is not AttachmentImportRequest request||request.Paths.Length is <1 or >128)throw new ArgumentException("Choose between one and 128 files.");
            var id=_activeId!.Value;var state=_documents[id];var revision=state.Editor.Document.Revision;
            using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(token);state.AttachmentWork=cancellation;
            try
            {
                state.AttachmentStatus="Reading and encoding attachments…";NotifyAttachments();
                var prepared=await Task.Run(async()=>
                {
                    var result=new List<PreparedAssAttachment>();long total=0;
                    foreach(var path in request.Paths)
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
                        if(stream.Length>AssAttachmentCodec.MaximumDecodedBytes)throw new InvalidDataException("Attachment exceeds the 32 MiB limit.");
                        total+=stream.Length;if(total>64L*1024*1024)throw new InvalidDataException("Attachment batch exceeds the 64 MiB limit.");
                        var bytes=new byte[checked((int)stream.Length)];await stream.ReadExactlyAsync(bytes,cancellation.Token);
                        if(stream.ReadByte()!=-1)throw new IOException("Attachment changed while being read.");
                        result.Add(PreparedAssAttachment.Create(request.Kind,Path.GetFileName(path),bytes,cancellation.Token));
                    }
                    return result.ToArray();
                },cancellation.Token);
                RequireAttachmentOwner(id,state,revision,cancellation.Token);
                _publishingAttachments=true;
                try{state.Editor.ImportAttachments(prepared,revision,cancellation.Token);}
                finally{_publishingAttachments=false;}
                state.AttachmentStatus=$"Imported {prepared.Length} attachment(s).";
            }
            finally{state.AttachmentWork=null;NotifyAttachments();}
        },()=>!AttachmentBusy);
        Register("attachments/remove","Remove attachments",(invocation,token)=>
        {
            if(invocation.Parameter is not AssAttachment[] selected)throw new ArgumentException("Choose attachments to remove.");
            var state=_documents[_activeId!.Value];_publishingAttachments=true;
            try{state.Editor.RemoveAttachments(selected,token);}finally{_publishingAttachments=false;}
            state.AttachmentStatus=$"Removed {selected.Length} attachment(s).";NotifyAttachments();return ValueTask.CompletedTask;
        },()=>!AttachmentBusy);
        Register("attachments/extract","Extract attachments",async(invocation,token)=>
        {
            if(invocation.Parameter is not AttachmentExtractionRequest request||request.Attachments.Length==0)throw new ArgumentException("Choose attachments to extract.");
            var id=_activeId!.Value;var state=_documents[id];var revision=state.Editor.Document.Revision;
            var destinations=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var attachment in request.Attachments)
            {
                if(!attachment.BelongsTo(state.Editor.Document))throw new InvalidOperationException("Attachment selection belongs to another document revision.");
                AttachmentFileNames.RequireSafe(attachment.ExtractionName);
                if(!attachment.CanExtract)throw new InvalidDataException(attachment.Error??"Attachment cannot be extracted.");
                if(!destinations.Add(attachment.ExtractionName))throw new InvalidDataException("Duplicate extraction filename: "+attachment.ExtractionName+". Select and extract these versions separately.");
            }
            using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(token);state.AttachmentWork=cancellation;var generation=state.AttachmentLease.Renew();
            try
            {
                foreach(var attachment in request.Attachments)
                {
                    RequireAttachmentOwner(id,state,revision,cancellation.Token);
                    var destination=Path.Combine(Path.GetFullPath(request.Directory),attachment.ExtractionName);
                    var overwrite=File.Exists(destination);
                    if(overwrite&&(Dialogs is null||!await Dialogs.ConfirmAttachmentOverwriteAsync(destination,cancellation.Token)))continue;
                    RequireAttachmentOwner(id,state,revision,cancellation.Token);
                    state.AttachmentStatus="Extracting "+attachment.Name;NotifyAttachments();
                    await AssAttachmentExtraction.WriteAsync(attachment,request.Directory,overwrite,state.AttachmentLease,generation,cancellation.Token);
                }
                state.AttachmentStatus="Extraction finished. Subtitle source and history are unchanged.";
            }
            finally{state.AttachmentWork=null;NotifyAttachments();}
        },()=>!AttachmentBusy);
        foreach(var redo in new[]{false,true})
        {
            var id=redo?"attachments/redo":"attachments/undo";
            Register(id,redo?"Redo attachment change":"Undo attachment change",(_,token)=>
            {
                token.ThrowIfCancellationRequested();_publishingAttachments=true;
                try{if(redo)_undo.Redo();else _undo.Undo();}finally{_publishingAttachments=false;}
                NotifyAttachments();return ValueTask.CompletedTask;
            },()=>!AttachmentBusy&&(redo?_undo.CanRedo:_undo.CanUndo)&&IsAttachmentHistory(redo?_undo.NextRedoName:_undo.NextUndoName));
        }
        Register("attachments/cancel","Cancel attachment operation",(_,_)=>{if(_activeId is {} id)CancelAttachmentWork(id);return ValueTask.CompletedTask;},()=>AttachmentBusy);
        Actions["subtitle/attachment"]=new RegistryCommand(_registry,"subtitle/attachment",CurrentContext,hotkeys:()=>HotkeyProfile);
    }
    private static bool IsAttachmentHistory(string? name)=>name is "Import attachments" or "Remove attachments";
    public Task<AssAttachment[]> ListAttachmentsAsync(Guid id,CancellationToken token=default)
    {
        if(!_documents.TryGetValue(id,out var state))throw new InvalidOperationException("The document was closed.");
        var snapshot=state.Editor.Document.CaptureSnapshot();return Task.Run(()=>snapshot.GetAttachments(token),token);
    }
    private void RequireAttachmentOwner(Guid id,DocumentState state,long revision,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(_disposed||_activeId!=id||!_documents.TryGetValue(id,out var current)||!ReferenceEquals(current,state)||state.Editor.Document.Revision!=revision)
            throw new OperationCanceledException("The attachment document changed.");
    }
    private void CancelAttachmentWork(Guid id)
    {
        if(_documents.TryGetValue(id,out var state)){state.AttachmentLease.Invalidate();state.AttachmentWork?.Cancel();}
    }
    private void NotifyAttachments(){OnPropertyChanged(nameof(AttachmentStatus));OnPropertyChanged(nameof(AttachmentBusy));_registry.NotifyStateChanged();}
}
