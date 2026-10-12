using System.Diagnostics;
using Yoake.Core.Audio;
using Yoake.Core.Commands;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    public bool HasAudioExportOperation=>_activeId is {} id&&_documents.TryGetValue(id,out var state)&&state.AudioExport is not null;
    private void RegisterAudioExportCommands()
    {
        bool Applies(CommandContext context)=>_activeId is not null&&(context.DocumentId is null||context.DocumentId==_activeId);
        _registry.Register(new(new("audio/save/clip","Save selected-line audio clip…","Save one WAV spanning the selected subtitle rows","Audio"),async(invocation,parent)=>
        {
            parent.ThrowIfCancellationRequested();
            var id=_activeId!.Value;var state=_documents[id];var media=state.Media!.AudioProvider!;
            var interval=AudioClipInterval.Capture(state.Editor,SelectedEvents.ToArray());
            var path=invocation.Parameter as string??(Dialogs is null?null:await Dialogs.SaveAudioClipAsync(Path.GetFileNameWithoutExtension(SuggestedSubtitleFileName)+"-selected.wav"));
            if(path is null||_disposed||!_documents.TryGetValue(id,out var live)||!ReferenceEquals(live,state)||!ReferenceEquals(state.Media?.AudioProvider,media))return;
            if(state.AudioExport is not null)throw new InvalidOperationException("An audio clip export is already running for this document.");
            var cancellation=CancellationTokenSource.CreateLinkedTokenSource(parent);state.AudioExport=cancellation;
            var progress=new Progress<double>(value=>
            {if(_activeId==id&&ReferenceEquals(state.AudioExport,cancellation))MediaStatus=$"Saving selected audio: {value:P0}";});
            NotifyAudioExport();
            try
            {
                var last=0L;var full=Path.GetFullPath(path);
                if(string.Equals(full,state.Media?.VideoSourcePath,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))throw new ArgumentException("Choose a destination other than the opened video file.");
                var job=_jobs.Run("Save selected audio clip",token=>
                {
                    media.SaveAudioClip(full,interval,token,value=>
                    {var now=Stopwatch.GetTimestamp();if(value is 0 or 1||Stopwatch.GetElapsedTime(last,now).TotalMilliseconds>=100){last=now;((IProgress<double>)progress).Report(value);}});
                    return Task.CompletedTask;
                },cancellation.Token);
                await job.Completion;cancellation.Token.ThrowIfCancellationRequested();
                if(_activeId==id)MediaStatus="Saved selected audio: "+Path.GetFileName(full);
            }
            catch(OperationCanceledException)when(cancellation.IsCancellationRequested)
            {if(parent.IsCancellationRequested)throw;if(_activeId==id)MediaStatus="Audio clip export canceled.";}
            finally{if(ReferenceEquals(state.AudioExport,cancellation))state.AudioExport=null;cancellation.Dispose();if(_activeId==id)NotifyAudioExport();}
        },context=>Applies(context)&&_media?.HasAudio==true&&SelectedEvents.Count>0&&!HasAudioExportOperation&&_gesture is null&&_documents[_activeId!.Value].Loading is null));
        _registry.Register(new(new("audio/save/cancel","Cancel audio clip export","Cancel this document's audio export, preserving the previous destination","Audio"),(_,_)=>
        {_documents[_activeId!.Value].AudioExport?.Cancel();return ValueTask.CompletedTask;},context=>Applies(context)&&HasAudioExportOperation));
        foreach(var id in new[]{"audio/save/clip","audio/save/cancel"})Actions[id]=new RegistryCommand(_registry,id,CurrentContext,hotkeys:()=>HotkeyProfile);
    }
    private void NotifyAudioExport(){OnPropertyChanged(nameof(HasAudioExportOperation));_registry.NotifyStateChanged();}
}
