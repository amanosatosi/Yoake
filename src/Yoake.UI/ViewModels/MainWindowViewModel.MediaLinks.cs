using Yoake.Core.Media;

namespace Yoake.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static IEnumerable<string> ProjectAssetReferences(Yoake.Core.Subtitles.AssDocument document,string? subtitlePath)
    {
        var links = SubtitleMediaLinks.Read(document,subtitlePath ?? "Untitled.ass");
        return new[] { links.Video,links.Audio,links.Timecodes,links.Keyframes }
            .Where(path=>path is not null && !path.StartsWith("?dummy:",StringComparison.OrdinalIgnoreCase)).Select(path=>path!);
    }
    private async Task OpenAssociatedMediaAsync(string subtitlePath,Guid owner,CancellationToken token)
    {
        if (!_documents.TryGetValue(owner,out var state)) return;
        var links = SubtitleMediaLinks.Read(state.Editor.Document,subtitlePath);
        bool Current() => !_disposed && _activeId == owner && _documents.TryGetValue(owner,out var live) && ReferenceEquals(live,state);
        async Task OpenAudio(string path)
        {
            if (!Current()) return;
            var opened = await OpenMediaTrackAsync(path,false,token,audioTrackIndex:links.AudioStream);
            token.ThrowIfCancellationRequested();
            if (!opened && links.AudioStream is not null && Current())
            {
                // A remux can change stream numbering. Keep the project usable
                // when the recorded stream disappears, and report the fallback.
                if (await OpenMediaTrackAsync(path,false,token)) SubtitleStatus = "The saved audio stream could not be opened; using the first available stream.";
            }
        }
        if (!Current()) return;
        OpeningSubtitleText = $"Opening associated media for {Path.GetFileName(subtitlePath)}…";
        OnPropertyChanged(nameof(OpeningSubtitleText));
        foreach (var (command,path) in new[] { ("timecode/open",links.Timecodes),("keyframe/open",links.Keyframes) })
        {
            if (!Current()) return;
            token.ThrowIfCancellationRequested();
            if (path is not null && File.Exists(path)) await _registry.InvokeAsync(command,new(owner),path,token);
        }
        if (!Current()) return;
        if (links.Video is {} video && (video.StartsWith("?dummy:",StringComparison.OrdinalIgnoreCase) || File.Exists(video)))
        {
            var combined = !video.StartsWith("?dummy:",StringComparison.OrdinalIgnoreCase) &&
                (!links.AudioSpecified || links.Audio is {} combinedAudio && SameMediaPath(video,combinedAudio));
            if (combined)
            {
                var opened = await OpenMediaAsync(video,links.AudioStream);
                token.ThrowIfCancellationRequested();
                if (!opened && links.AudioStream is not null && Current() && await OpenMediaAsync(video))
                    SubtitleStatus = "The saved audio stream could not be opened; using the first available stream.";
            }
            else
            {
                await OpenMediaTrackAsync(video,true,token,openEmbeddedAudio:false);
                token.ThrowIfCancellationRequested();
                if (Current() && links.Audio is {} separateAudio && File.Exists(separateAudio)) await OpenAudio(separateAudio);
            }
        }
        else if (links.Audio is {} audio && File.Exists(audio)) await OpenAudio(audio);
        else if (!links.VideoSpecified && !links.AudioSpecified && FindSiblingMedia(subtitlePath) is {} inferred)
            await OpenMediaAsync(inferred);
    }
}
