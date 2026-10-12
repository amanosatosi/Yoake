using Yoake.Core.Media;
using Yoake.Core.Settings;

namespace Yoake.Core.Tests;

public sealed class VideoUtilitiesTests
{
    [Theory]
    [InlineData("2.35", 2.35)] [InlineData("16:9", 16d/9)] [InlineData("16/9", 16d/9)]
    [InlineData("853x480",853d/480)] [InlineData(" 853X480 ",853d/480)] [InlineData("0.5",0.5)] [InlineData("5",5)]
    public void CustomAspectAcceptsExactReleaseFormats(string text, double expected)
    { Assert.True(VideoAspect.TryCustom(text,out var aspect)); Assert.Equal(VideoAspectKind.Custom,aspect.Kind); Assert.Equal(expected,aspect.Custom,10); }
    [Theory]
    [InlineData("")] [InlineData("NaN")] [InlineData("Infinity")] [InlineData("16:0")] [InlineData("-16:-9")]
    [InlineData("16:9:1")] [InlineData("0.49")] [InlineData("5.01")] [InlineData("日本語")] [InlineData("1,5")]
    public void CustomAspectRejectsInvalidNonfiniteAndOutOfRangeInput(string text) => Assert.False(VideoAspect.TryCustom(text,out _));
    [Fact] public void PresetsAndLetterboxShareOneDpiIndependentScriptTransform()
    {
        Assert.Equal(4d/3,new VideoAspect(VideoAspectKind.Full).Ratio(2));
        Assert.Equal(16d/9,new VideoAspect(VideoAspectKind.Wide).Ratio(2));
        Assert.Equal(2.35,new VideoAspect(VideoAspectKind.Cinematic).Ratio(2));
        Assert.Equal(2,new VideoAspect().Ratio(2));
        foreach(var ratio in new[]{4d/3,16d/9,2.35,0.5,5})
        {
            var viewport=VideoPresentation.Fit(800,600,ratio);
            var center=viewport.ToScript(400,300,384,288); Assert.Equal(192,center.X,9);Assert.Equal(144,center.Y,9);
            var corner=viewport.ToScript(viewport.X,viewport.Y,384,288);Assert.Equal(0,corner.X,9);Assert.Equal(0,corner.Y,9);
            Assert.True(viewport.Contains(viewport.X,viewport.Y));Assert.False(viewport.Contains(viewport.X-1,viewport.Y));
            Assert.True(viewport.ToScript(viewport.X-1,viewport.Y,384,288).X<0);
            var physical=VideoPresentation.Fit(1600,1200,ratio).ToScript(800,600,384,288);Assert.Equal(center,physical);
            Assert.Equal("192,144",VideoPresentation.Coordinates(center));
        }
        Assert.Equal("12.346,-0.5",VideoPresentation.Coordinates(new(12.3456,-0.5)));
    }
    [Fact] public void OverscanGeometryUsesReleaseWideAndNarrowGuidelines()
    {
        Assert.Equal(new[]{(0.067,0.05),(0.033,0.035)},VideoPresentation.Overscan(4d/3));
        Assert.Equal(new[]{(0.1,0.05),(0.035,0.035)},VideoPresentation.Overscan(16d/9));
    }
    [Fact] public void FrameCaptureRejectsProviderFrameMapFrameAndPreviewReplacement()
    {
        var provider=new object();var map=new double[]{0,0.04,0.11};
        var capture=new FrameCaptureIdentity(provider,map,2,map[2],7);
        Assert.Equal(0.11,capture.Seconds);Assert.True(capture.Matches(provider,map,2,7));
        Assert.False(capture.Matches(new object(),map,2,7));Assert.False(capture.Matches(provider,map.ToArray(),2,7));
        Assert.False(capture.Matches(provider,map,1,7));Assert.False(capture.Matches(provider,map,2,8));
    }
    [Fact] public void SubtitleAlphaPreservesOpaqueEdgesSemitransparencyOverlapAndBlank()
    {
        byte[] rgba=[255,0,0,255, 32,16,8,64, 0,0,0,0, 128,0,0,128];
        byte[] bgra=[0,0,255,0, 8,16,32,0, 99,99,99,0, 0,0,128,0];
        SubtitleAlpha.AccumulateRow(rgba,bgra); Assert.Equal(new byte[]{255,64,0,128},bgra.Where((_,i)=>i%4==3));
        SubtitleAlpha.UnpremultiplyBgra(bgra);Assert.Equal(new byte[]{0,0,255,255},bgra[..4]);
        Assert.Equal(new byte[]{32,64,128,64},bgra[4..8]);Assert.Equal(new byte[4],bgra[8..12]);
        byte[] overlapping=[0,0,0,128];SubtitleAlpha.AccumulateRow([0,0,0,128],overlapping);Assert.Equal(192,overlapping[3]);
        SubtitleAlpha.AccumulateRow([0,0,0,0],overlapping);Assert.Equal(192,overlapping[3]);
    }
    [Fact] public void PreparedPngRejectsStaleCanceledOrFailedPublicationAndRetainsOldDestination()
    {
        var root=Path.Combine(Path.GetTempPath(),"Yoake-image-"+Guid.NewGuid());Directory.CreateDirectory(root);var path=Path.Combine(root,"日本語 မြန်မာ.png");File.WriteAllText(path,"original");
        try
        {
            Assert.Throws<IOException>(()=>PreparedFrameImage.Create(path,stream=>{stream.Write([1,2,3]);throw new IOException("encoder failed");},default));
            using(var prepared=PreparedFrameImage.Create(path,stream=>stream.Write([1,2,3]),default))Assert.Throws<OperationCanceledException>(()=>prepared.Commit(()=>false,default,true));
            using(var cts=new CancellationTokenSource())
            using(var prepared=PreparedFrameImage.Create(path,stream=>stream.Write([1,2,3]),default))
            {cts.Cancel();Assert.Throws<OperationCanceledException>(()=>prepared.Commit(()=>true,cts.Token,true));}
            Assert.Equal("original",File.ReadAllText(path));Assert.Single(Directory.GetFiles(root));
            using(var prepared=PreparedFrameImage.Create(path,stream=>stream.Write([1,2,3]),default))prepared.Commit(()=>true,default,true);
            Assert.Equal(new byte[]{1,2,3},File.ReadAllBytes(path));Assert.Single(Directory.GetFiles(root));
            var first=FrameImageFile.NextName(Path.Combine(root,"video.avi"),false,null,23,root);Assert.EndsWith("video_001_23.png",first);File.WriteAllText(first,"");
            Assert.EndsWith("video_002_23.png",FrameImageFile.NextName(Path.Combine(root,"video.avi"),false,null,23,root));
            Assert.Equal(Path.Combine(root,"dummy_001_23.png"),FrameImageFile.NextName("?dummy",true,Path.Combine(root,"script.ass"),23,root));
            Assert.Equal(Path.Combine(root,"dummy_001_23.png"),FrameImageFile.NextName("?dummy",true,null,23,root));
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact] public void DummyDefaultsAndOverscanPersistEvenWithOtherDamagedSettings()
    {
        var defaults=new DummyVideoOptions();Assert.Equal(640,defaults.Width);Assert.Equal(480,defaults.Height);Assert.Equal(40000,defaults.FrameCount);Assert.Equal(23.976,defaults.FramesPerSecond);Assert.Equal(new Yoake.Core.Subtitles.AssColor(47,163,254,0),defaults.Color);Assert.False(defaults.Checkerboard);
        var root=Path.Combine(Path.GetTempPath(),"Yoake-dummy-settings-"+Guid.NewGuid());var store=new SettingsStore(Path.Combine(root,"settings.json"));
        try
        {
            var custom=defaults with{Width=1280,Height=720,FrameCount=240,FramesPerSecond=24,Checkerboard=true};
            store.Save(new(){LastDummyVideo=custom,VideoOverscan=true});var json=File.ReadAllText(store.Path).Replace("\"schemaVersion\": 1","\"schemaVersion\": {}");File.WriteAllText(store.Path,json);
            var restored=store.Load();Assert.Equal(custom,restored.LastDummyVideo);Assert.True(restored.VideoOverscan);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
