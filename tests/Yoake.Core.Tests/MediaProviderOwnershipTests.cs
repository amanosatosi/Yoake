using Yoake.Core.Media;
using Yoake.Core.Settings;

namespace Yoake.Core.Tests;

public sealed class MediaProviderOwnershipTests
{
    private sealed class Provider : IDisposable
    {
        public int Disposals;
        public void Dispose() => Interlocked.Increment(ref Disposals);
    }
    [Fact] public void VideoReplacementAndCloseRetainTheExactAudioResource()
    {
        var video = new Provider(); var audio = new Provider(); var nextVideo = new Provider();
        var original = MediaProviderOwnership<Provider>.Own(video, audio);
        var candidate = MediaProviderOwnership<Provider>.Own(nextVideo, null);
        var replaced = original.ReplaceVideo(candidate);
        original.Dispose(); original.Dispose(); candidate.Dispose();
        Assert.Equal(1, video.Disposals); Assert.Equal(0, audio.Disposals); Assert.Equal(0, nextVideo.Disposals);
        Assert.Same(audio, replaced.Audio); Assert.Same(nextVideo, replaced.Video);
        var closed = replaced.ReplaceVideo(null); replaced.Dispose();
        Assert.Null(closed.Video); Assert.Same(audio, closed.Audio);
        Assert.Equal(1, nextVideo.Disposals); Assert.Equal(0, audio.Disposals);
        closed.Dispose(); closed.Dispose(); Assert.Equal(1, audio.Disposals);
    }
    [Fact] public void AudioReplacementRetainsVideoAndFailedReplacementLeavesOwnershipIntact()
    {
        var video = new Provider(); var audio = new Provider(); var nextAudio = new Provider();
        using var original = MediaProviderOwnership<Provider>.Own(video, audio);
        using var candidate = MediaProviderOwnership<Provider>.Own(null, nextAudio);
        using var replaced = original.ReplaceAudio(candidate);
        candidate.Dispose(); original.Dispose();
        Assert.Same(video, replaced.Video); Assert.Same(nextAudio, replaced.Audio); Assert.Equal(1, audio.Disposals);
        Assert.Throws<ObjectDisposedException>(() => replaced.ReplaceVideo(candidate));
        Assert.Same(video, replaced.Video); Assert.Same(nextAudio, replaced.Audio);
        Assert.Equal(0, video.Disposals); Assert.Equal(0, nextAudio.Disposals);
        replaced.Dispose(); Assert.Equal(1, video.Disposals); Assert.Equal(1, nextAudio.Disposals);
    }
    [Fact] public void AudioCloseDisposesExactlyOnceRetainsVideoAndAllowsNewAudio()
    {
        var video = new Provider(); var audio = new Provider(); var nextAudio = new Provider();
        var original = MediaProviderOwnership<Provider>.Own(video, audio);
        var closed = original.ReplaceAudio(null); original.Dispose(); original.Dispose();
        Assert.Equal(1, audio.Disposals); Assert.Equal(0, video.Disposals);
        Assert.Same(video, closed.Video); Assert.Null(closed.Audio);
        var closedAgain = closed.ReplaceAudio(null); closed.Dispose();
        using var candidate = MediaProviderOwnership<Provider>.Own(null, nextAudio);
        var reopened = closedAgain.ReplaceAudio(candidate); closedAgain.Dispose(); candidate.Dispose();
        Assert.Same(video, reopened.Video); Assert.Same(nextAudio, reopened.Audio);
        Assert.Equal(0, video.Disposals); Assert.Equal(0, nextAudio.Disposals);
        reopened.Dispose(); reopened.Dispose();
        Assert.Equal(1, video.Disposals); Assert.Equal(1, audio.Disposals); Assert.Equal(1, nextAudio.Disposals);
    }
    [Fact] public async Task ConcurrentSnapshotReleaseDisposesEachProviderExactlyOnce()
    {
        var video = new Provider(); var audio = new Provider();
        using var root = MediaProviderOwnership<Provider>.Own(video, audio);
        var snapshots = Enumerable.Range(0, 32).Select(_ => root.ReplaceVideo(root)).ToArray();
        root.Dispose();
        await Task.WhenAll(snapshots.Select(snapshot => Task.Run(() => { snapshot.Dispose(); snapshot.Dispose(); })));
        Assert.Equal(1, video.Disposals); Assert.Equal(1, audio.Disposals);
        Assert.Throws<ObjectDisposedException>(() => root.ReplaceAudio(null));
        Assert.Throws<ObjectDisposedException>(() => root.Video);
    }
    [Fact] public void ACombinedResourceCannotMasqueradeAsIndependentOwnership()
    {
        var provider = new Provider();
        Assert.Throws<ArgumentException>(() => MediaProviderOwnership<Provider>.Own(provider, provider));
        Assert.Equal(0, provider.Disposals); provider.Dispose();
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void IndependentUnicodeMediaRecentsNormalizePersistAndSurviveDamagedOtherSettings(bool damaged)
    {
        var directory = Path.Combine(Path.GetTempPath(), "yoake-media-recents-" + Guid.NewGuid());
        var store = new SettingsStore(Path.Combine(directory, "settings.json"));
        try
        {
            var videos = Enumerable.Range(0, 15).Select(i => $"C:/日本語 မြန်မာ/video-{i}.mkv").ToArray();
            store.Save(new AppSettings { RecentVideo = new[] { "", videos[0], videos[0].ToUpperInvariant() }.Concat(videos).ToArray(), RecentAudio = ["C:/音声/audio.wav"] });
            if (damaged) File.WriteAllText(store.Path, File.ReadAllText(store.Path).Replace("\"schemaVersion\": 1", "\"schemaVersion\": {}"));
            var loaded = store.Load();
            Assert.Equal(videos.Take(12), loaded.RecentVideo); Assert.Equal(new[] { "C:/音声/audio.wav" }, loaded.RecentAudio);
            Assert.Empty(loaded.RecentFiles); Assert.Empty(loaded.RecentKeyframes!);
            store.Save(loaded); Assert.Equal(loaded.RecentVideo, store.Load().RecentVideo);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
