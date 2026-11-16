using Yoake.Core.Media;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed record OpenEmbeddedSubtitlesRequest(string Path,ulong? TrackNumber=null,bool LoadMedia=true);
public sealed partial class MainWindowViewModel
{
    private void RegisterEmbeddedSubtitleCommands()
    {
        _registry.Register(new(new("subtitle/open/video","Open subtitles from video…","Extract an ASS or UTF-8 Matroska track into a separate unsaved document","Subtitle"),async (invocation,token)=>
        {
            var request=invocation.Parameter as OpenEmbeddedSubtitlesRequest;
            if(request is null)
            {
                var path=VideoSourcePath;
                if(path is null||!File.Exists(path))path=Dialogs is {} dialogs?await dialogs.OpenVideoAsync():null;
                if(path is null)return;request=new(path);
            }
            using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(token);
            _subtitleOpenCancellation=cancellation;OpeningSubtitleText=$"Reading subtitle tracks in {Path.GetFileName(request.Path)}…";
            OnPropertyChanged(nameof(OpeningSubtitleText));OpeningSubtitle=true;
            var published=false;var mediaWarning="";
            try
            {
                var tracks=await Task.Run(()=>MatroskaSubtitles.ReadTracks(request.Path,cancellation.Token),cancellation.Token);
                if(!tracks.Any(t=>t.Supported))throw new NotSupportedException("No supported subtitle tracks were found. Use an uncompressed Matroska ASS or UTF-8 track.");
                var number=request.TrackNumber;
                if(number is null)number=tracks.Length==1&&tracks[0].Supported?tracks[0].Number:Dialogs is {} chooser?await chooser.ChooseSubtitleTrackAsync(tracks,cancellation.Token):null;
                cancellation.Token.ThrowIfCancellationRequested();if(number is null)return;
                OpeningSubtitleText="Extracting embedded subtitles…";OnPropertyChanged(nameof(OpeningSubtitleText));
                var document=await Task.Run(()=>MatroskaSubtitles.Extract(request.Path,number.Value,cancellation.Token),cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();if(_disposed)return;
                // Opening another source must not commit or discard the current
                // tab's draft, media, timing plans, or undo/redo history.
                var session=_workspace.CreateUntitled();Attach(session,document);
                _documents[session.Id].UnsavedSource=true;_documents[session.Id].ImportedContainerPath=Path.GetFullPath(request.Path);
                session.Title=Path.GetFileNameWithoutExtension(request.Path)+$" — track {number}.ass";
                SynchronizeActiveDocument();session.IsDirty=true;published=true;
                if(request.LoadMedia)
                {
                    OpeningSubtitleText="Opening episode video and audio…";OnPropertyChanged(nameof(OpeningSubtitleText));
                    _subtitleOpenMediaDocument=session.Id;
                    if(!await OpenMediaAsync(request.Path))mediaWarning=" Video/audio could not be opened; see the media status.";
                    cancellation.Token.ThrowIfCancellationRequested();
                }
                SubtitleStatus=$"Extracted {document.Events.Count} events. Save As to choose an ASS destination."+mediaWarning;
            }
            catch(OperationCanceledException) when(cancellation.IsCancellationRequested){SubtitleStatus=published?"Subtitles were extracted; media opening was canceled. Save As to retain the imported document.":"Subtitle extraction canceled.";}
            finally
            {
                _subtitleOpenMediaDocument=null;
                if(ReferenceEquals(_subtitleOpenCancellation,cancellation))_subtitleOpenCancellation=null;
                OpeningSubtitle=false;
            }
        },_=>!OpeningSubtitle));
        Actions["subtitle/open/video"]=new RegistryCommand(_registry,"subtitle/open/video",CurrentContext,hotkeys:()=>HotkeyProfile);
    }
}
