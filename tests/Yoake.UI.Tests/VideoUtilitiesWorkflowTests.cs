using Xunit;
using Yoake.Core.Media;
using Yoake.Native;
using Yoake.Core.Settings;

namespace Yoake.UI.Tests;

public sealed partial class EditorWorkflowTests
{
    [Fact] public async Task DummyOwnsDeterministicSourceFramesAndIndependentReplacementClose()
    {
        var options=new DummyVideoOptions{Width=24,Height=16,FrameCount=10,FramesPerSecond=25,Color=new(50,100,150,0)};
        using var provider=new DummyVideoProvider(options);Assert.Equal(0.4,provider.VideoDurationSeconds);Assert.Equal(0.12,provider.FrameTimes[3]);
        var first=provider.GetFrame(0);Assert.Equal(24,first.Width);Assert.Equal(16,first.Height);Assert.Equal(new byte[]{150,100,50,255},first.Pixels[..4]);
        first.Pixels[0]=0;Assert.Equal(150,provider.GetFrame(0).Pixels[0]);Assert.Equal(provider.GetFrame(0).Pixels,provider.GetFrameAtTime(0.399).Pixels);
        Assert.Equal(0.16,provider.AdjacentFrameTime(0.12,1));Assert.Throws<ArgumentOutOfRangeException>(()=>provider.GetFrame(10));
        using var pattern=new DummyVideoProvider(options with{Checkerboard=true});var pixels=pattern.GetFrame(0).Pixels;Assert.NotEqual(pixels[..4],pixels[(8*4)..(8*4+4)]);Assert.Equal(pixels[..4],pixels[((8*24+8)*4)..((8*24+8)*4+4)]);
        using var media=DocumentMedia.FromVideo(provider);using var replacement=DocumentMedia.FromVideo(pattern);using var replaced=media.WithVideo(replacement);
        replacement.Dispose();media.Dispose();Assert.False(provider.HasVideo);Assert.True(pattern.HasVideo);
        using var closed=replaced.WithVideo(null);replaced.Dispose();Assert.False(pattern.HasVideo);Assert.False(closed.HasVideo);
        Assert.Throws<ArgumentException>(()=>new DummyVideoProvider(options with{FramesPerSecond=double.NaN}));
        Assert.Throws<ArgumentException>(()=>new DummyVideoProvider(options with{Width=0}));
        using var canceled=new CancellationTokenSource();canceled.Cancel();Assert.Throws<OperationCanceledException>(()=>new DummyVideoProvider(options,canceled.Token));
        await Task.CompletedTask;
    }
    [Theory]
    [InlineData(FrameImageKind.Raw)] [InlineData(FrameImageKind.Full)] [InlineData(FrameImageKind.Subtitles)]
    public void OneExtractionPipelineCapturesExactVfrFrameTimeAndSourceDimensions(FrameImageKind kind)
    {
        using var provider=new DummyVideoProvider(new(){Width=24,Height=16,FrameCount=3,FramesPerSecond=25});
        var external=TimecodeFile.Read(new StringReader("# timecode format v2\n0\n40\n110\n"));provider.SetExternalTimecodes(external.ForVideoMap(3));
        var identity=new FrameCaptureIdentity(provider,provider.FrameTimes,2,provider.FrameTimes[2],7);
        var renders=0;
        var image=FrameExtraction.Extract(provider,identity,kind,(frame,time)=>
        {
            renders++;Assert.Equal(0.11,time);Assert.Equal(24,frame.Width);Assert.Equal(16,frame.Height);
            Assert.Equal(kind==FrameImageKind.Subtitles?0:255,frame.Pixels[3]);frame.Pixels[0]=23;frame.Pixels[3]=128;return frame;
        },default);
        Assert.Equal(kind==FrameImageKind.Raw?0:1,renders);Assert.Equal(kind==FrameImageKind.Raw?255:128,image.Pixels[3]);
        Assert.Equal(24*16*4,image.Pixels.Length);Assert.Equal(0.11,identity.Seconds);
    }
    [Fact] public async Task DummyAspectOverscanDetailsCoordinatesAndRawCopyUseCommandsWithoutSourceEdits()
    {
        await TimingRows();await Command("time/lead/out");_model.Draft!.Layer="unfinished";
        var draft=_model.Draft;var source=_model.ActiveEditor!.Document.Serialize();var undo=_model.ActiveEditor.Undo.NextUndoName;var pending=_model.PrimaryAudioRange;var selected=_model.SelectedEvent;
        Assert.True(await _model.OpenDummyVideoAsync(new(){Width=64,Height=48,FrameCount=20,FramesPerSecond=25}));
        var map=_model.FrameTimes;var raw=(await _model.ExtractCurrentVideoFrameAsync(FrameImageKind.Raw)).Image.Pixels.ToArray();
        foreach(var preset in new[]{"default","full","wide","cinematic"})
        {
            await Command("video/aspect/"+preset);Assert.True(_model.Actions["video/aspect/"+preset].IsChecked);
            Assert.Single(new[]{"default","full","wide","cinematic","custom"}.Where(p=>_model.Actions["video/aspect/"+p].IsChecked));
        }
        await Command("video/aspect/custom","853x480");Assert.Equal(853d/480,_model.VideoAspectRatio);
        Assert.False(await _model.Registry.InvokeAsync("video/aspect/custom",new(),"16:0"));Assert.Equal(853d/480,_model.VideoAspectRatio);
        await Command("video/show_overscan");Assert.True(_model.ShowVideoOverscan);Assert.True(_model.Actions["video/show_overscan"].IsChecked);
        Assert.True(new SettingsStore(Path.Combine(_root,"settings.json")).Load().VideoOverscan);
        Assert.Equal(raw,(await _model.ExtractCurrentVideoFrameAsync(FrameImageKind.Raw)).Image.Pixels);
        Assert.Same(map,_model.FrameTimes);Assert.Equal(source,_model.ActiveEditor.Document.Serialize());Assert.Equal(undo,_model.ActiveEditor.Undo.NextUndoName);Assert.Same(draft,_model.Draft);Assert.Same(selected,_model.SelectedEvent);Assert.Equal(pending,_model.PrimaryAudioRange);
        var details=_model.CurrentVideoDetails!;Assert.Equal(64,details.Width);Assert.Equal(48,details.Height);Assert.Equal(20,details.FrameCount);Assert.Equal(25,details.FramesPerSecond);Assert.Contains("dummy",details.Decoder);Assert.Equal(0.8,details.DurationSeconds);
        await Command("video/details");Assert.Equal(details,_dialogs.VideoDetails);
        _model.ReportVideoPointer(new(12.3456,-0.5));await Command("video/copy_coordinates");Assert.Equal("12.346,-0.5",_dialogs.ClipboardText);
        await Command("video/frame/copy/raw");Assert.Equal(64,_dialogs.FrameImage!.Width);
        _dialogs.FailFrameClipboard=true;Assert.False(await _model.Registry.InvokeAsync("video/frame/copy/raw",new()));Assert.Equal(source,_model.ActiveEditor.Document.Serialize());Assert.Same(draft,_model.Draft);
        Assert.Single(_model.RecentVideoItems);Assert.Equal(_model.VideoSourcePath,_model.RecentVideoItems[0].Path);Assert.Equal(64,_model.LastDummyVideo.Width);
        foreach(var id in new[]{"video/aspect/default","video/frame/copy/raw","video/details","video/open/dummy","video/show_overscan"})Assert.False(await _model.Registry.InvokeAsync(id,new(Guid.NewGuid())));
        await Command("video/close");Assert.Null(_model.CurrentVideoDetails);Assert.False(_model.Actions["video/frame/copy/raw"].CanExecute(null));
        await Command("recent/video",_model.RecentVideoItems[0].Path);Assert.Equal(64,_model.CurrentVideoDetails!.Width);
        foreach(var id in new[]{"video/reset_pan","video/zoom/50","video/zoom/100","video/zoom/200","video/zoom/in","video/zoom/out","video/detach"})Assert.True(_model.Registry.TryGet(id,out _));
    }
}
