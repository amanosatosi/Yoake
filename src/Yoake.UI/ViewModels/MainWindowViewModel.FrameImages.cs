using Avalonia.Media.Imaging;
using Yoake.Core.Commands;
using Yoake.Core.Media;
using Yoake.Native;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed record VideoFrameCapture(Guid DocumentId, FrameCaptureIdentity Identity, DecodedVideoFrame Image);

public sealed partial class MainWindowViewModel
{
    public bool IsCurrentCapture(VideoFrameCapture capture) => !_disposed && _activeId == capture.DocumentId &&
        capture.Identity.Matches(_media?.VideoProvider, FrameTimes, CurrentFrame, PreviewRevision);

    public async Task<VideoFrameCapture> ExtractCurrentVideoFrameAsync(FrameImageKind kind, CancellationToken token = default)
    {
        VideoFrameCapture? result = null;
        await RunFrameExtractionAsync(kind, (capture, _) => { result = capture; return Task.CompletedTask; }, token);
        return result ?? throw new OperationCanceledException("The video frame changed during extraction.");
    }

    private async Task RunFrameExtractionAsync(FrameImageKind kind, Func<VideoFrameCapture, CancellationToken, Task> publish, CancellationToken parent)
    {
        parent.ThrowIfCancellationRequested();
        if (_disposed || _activeId is not { } id || _media?.VideoProvider is not { } provider) throw new InvalidOperationException("Open video first.");
        var state = _documents[id];
        var identity = new FrameCaptureIdentity(provider, FrameTimes, CurrentFrame, FrameTimes[CurrentFrame], PreviewRevision);
        var source = PreviewSource();
        state.FrameExtraction?.Cancel();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(parent);
        state.FrameExtraction = cancellation;
        // Keep the source resource alive while decoding, even if its document
        // replaces/closes video. Publication still requires the captured identity.
        using var lease = state.Media!.WithAudio(null);
        try
        {
            var image = await RunAnalysisAsync("Extract video frame", token => FrameExtraction.Extract(provider, identity, kind,
                (frame, seconds) => RenderFrameSubtitles(frame, seconds, source, identity.PreviewRevision, kind), token), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            var capture = new VideoFrameCapture(id, identity, image);
            if (!IsCurrentCapture(capture) || !_documents.TryGetValue(id, out var live) || !ReferenceEquals(state, live))
                throw new OperationCanceledException("The video frame changed during extraction.");
            await publish(capture, cancellation.Token);
        }
        finally { if (ReferenceEquals(state.FrameExtraction, cancellation)) state.FrameExtraction = null; }
    }

    private void RegisterFrameImageCommands()
    {
        foreach (var save in new[] { false, true })
        foreach (var kind in new[] { FrameImageKind.Full, FrameImageKind.Raw, FrameImageKind.Subtitles })
        {
            var id = "video/frame/" + (save ? "save" : "copy") + (kind == FrameImageKind.Raw ? "/raw" : kind == FrameImageKind.Subtitles ? "/subs" : "");
            var label = (save ? "Save PNG snapshot" : "Copy frame image") + (kind == FrameImageKind.Raw ? " (no subtitles)" : kind == FrameImageKind.Subtitles ? " (subtitles only)" : "");
            _registry.Register(new(new(id, label, "Extract current source-resolution frame", "Video"), async (invocation, token) =>
            {
                var owner=_activeId;
                try
                {
                    await RunFrameExtractionAsync(kind, async (capture, ct) =>
                    {
                        if (!save)
                        {
                            await (Dialogs ?? throw new InvalidOperationException("Clipboard is unavailable.")).WriteFrameClipboardAsync(capture.Image, () => IsCurrentCapture(capture), ct);
                            return;
                        }
                        var provider = (IVideoProvider)capture.Identity.Provider;
                        var explicitPath = invocation.Parameter as string;
                        var path = explicitPath ?? FrameImageFile.NextName(provider.SourcePath, provider.IsDummy, ActiveSubtitlePath, capture.Identity.Frame, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                        var full = Path.GetFullPath(path);
                        if (new[] { VideoSourcePath, AudioSourcePath, ActiveSubtitlePath }.Where(p => p is not null && !p.StartsWith('?')).Any(p => SameMediaPath(full, p!)))
                            throw new ArgumentException("Choose a destination other than an opened source file.");
                        using var prepared = await RunAnalysisAsync("Encode video PNG", _ => PreparedFrameImage.Create(full, stream =>
                        { using var bitmap = CreateBitmap(capture.Image); bitmap.Save(stream); }, ct), ct);
                        prepared.Commit(() => IsCurrentCapture(capture), ct, overwrite: explicitPath is not null);
                        MediaStatus = "Saved snapshot: " + full;
                    }, token);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { if(_activeId==owner)MediaStatus = "Frame extraction canceled: video, frame or preview changed."; }
            }, context => _activeId is not null && (context.DocumentId is null || context.DocumentId == _activeId) && _media?.HasVideo == true && (save || Dialogs is not null)));
            Actions[id] = new RegistryCommand(_registry, id, CurrentContext, hotkeys: () => HotkeyProfile);
        }
    }

    private DecodedVideoFrame RenderFrameSubtitles(DecodedVideoFrame frame, double seconds, string? source, long revision, FrameImageKind kind)
    {
        if (string.IsNullOrEmpty(source)) return frame;
        lock (_subtitleRendererGate)
        {
            if (_subtitleRenderer is null)
            { _subtitleRenderer = new MangetsuSubtitleRenderer(source, frame.Width, frame.Height); _subtitleRendererRevision = revision; }
            else if (_subtitleRendererRevision != revision)
            { _subtitleRenderer.UpdateTrack(source); _subtitleRendererRevision = revision; }
            if (kind == FrameImageKind.Subtitles) return _subtitleRenderer.RenderSubtitles(frame.Width, frame.Height, seconds);
            _subtitleRenderer.Composite(frame, seconds); return frame;
        }
    }
}
