using Yoake.Core.Media;

namespace Yoake.Core.Tests;

public sealed class TimecodeFileTests
{
    private static ExternalTimecodes Read(string text)=>TimecodeFile.Read(new StringReader(text));
    [Fact] public void VideoOperationsUseNativeFrameCountSeparatelyFromExternalMetadataLength()
    {
        var editor=new Yoake.Core.Subtitles.SubtitleEditor(Yoake.Core.Subtitles.AssDocument.CreateEmpty());var line=editor.Insert(null,false);
        editor.SetTiming(line,0,1000);editor.SetField(line,"Text","{\\future(x)}日本語 é","Text");var source=editor.Document.Serialize();
        var timing=Read("Assume 25").Timing;
        var split=editor.SplitAtFrame([line],timing,8,true,10);Assert.Equal(1,split.Count);editor.Undo.Undo();Assert.Equal(source,editor.Document.Serialize());
        var cursor=editor.SplitAtCursor(line,line.Text.Length-2,Yoake.Core.Subtitles.CursorSplitTiming.VideoFrame,timing,8,10);Assert.NotNull(cursor);editor.Undo.Undo();Assert.Equal(source,editor.Document.Serialize());
        editor.ApplyTiming(editor.CaptureTiming([line]).SnapScene(timing,8,[0,5],videoFrameCount:10));Assert.Equal(180,line.StartMilliseconds);Assert.Equal(380,line.EndMilliseconds);
        editor.Undo.Undo();Assert.Equal(source,editor.Document.Serialize());
        Assert.Throws<ArgumentOutOfRangeException>(()=>editor.SplitAtFrame([line],timing,10,true,10));
        Assert.Throws<ArgumentException>(()=>editor.CaptureTiming([line]).SnapScene(timing,10,[0,5],videoFrameCount:10));
    }
    [Fact] public void ProviderExportAndVideoMapsAreImmutableAndFrozenAutomationKeepsItsStartConversion()
    {
        var provider=TimecodeFile.FromVideo([1,1.04,1.1],25);Assert.Equal(new long[]{0,40,100},provider.Milliseconds);
        var external=Read("Assume 25\n0,1,50");var video=external.ForVideoMap(6);
        Assert.Equal(new[]{0d,.02,.04,.08,.12,.16},video.FrameTimes);Assert.Equal(.2,video.DurationSeconds);
        Assert.Throws<NotSupportedException>(()=>((IList<double>)video.FrameTimes)[0]=9);
        var automation=new Yoake.Core.Automation.AutomationTimecodeView(external);
        Assert.Equal(30,automation.MillisecondsFromFrame(2));Assert.Equal(60,automation.MillisecondsFromFrame(3));Assert.Equal(3,automation.FrameFromMilliseconds(60));
    }
    [Fact] public void V1UsesAssumedRateBeyondOverrideRangesInsteadOfStoredAverage()
    {
        var map=Read("# timecode format v1\nAssume 25\n0,1,50\n");
        Assert.Equal(new long[]{0,20,40},map.Milliseconds);Assert.Equal(80,map.Timing.MillisecondsAtFrame(3));Assert.Equal(120,map.Timing.MillisecondsAtFrame(4));
        Assert.Equal(30,map.Timing.MillisecondsAtFrame(2,FrameTimeKind.Start));Assert.Equal(60,map.Timing.MillisecondsAtFrame(2,FrameTimeKind.End));
        Assert.Equal(new[]{0d,.02,.04,.08,.12},map.ForVideo(5));
        var averaged=Read("# timecode format v2\n0\n20\n40");Assert.Equal(60,averaged.Timing.MillisecondsAtFrame(3));
    }
    [Fact] public void V1AllowsSingleFrameOverridesOutOfOrderRangesAndHeaderlessAssumeWithFractionalExtrapolation()
    {
        var map=Read("Assume 25\n4,4,50\n0,1,50");Assert.Equal(new long[]{0,20,40,80,120,140},map.Milliseconds);
        Assert.Equal(180,map.Timing.MillisecondsAtFrame(6));
        var fractional=Read("# timecode format v1\nAssume 29.97003\n0,0,23.976");
        Assert.Equal(new long[]{0,42},fractional.Milliseconds);Assert.Equal(75,fractional.Timing.MillisecondsAtFrame(2));
        var cfr=Read("Assume 25\n# no overrides");Assert.Equal(new long[]{0},cfr.Milliseconds);Assert.Equal(120,cfr.Timing.MillisecondsAtFrame(3));
    }
    [Fact] public void V2TruncatesValidDecimalTimestampsNormalizesOriginAndRetainsDuplicateFrames()
    {
        var map=Read("\uFEFF# timecode format v2\n100.9\n# comment 9999\n140.99\n140.1\n200.2\n");
        Assert.Equal(new long[]{0,40,40,100},map.Milliseconds);Assert.Equal(133,map.Timing.MillisecondsAtFrame(4));
        Assert.Equal(2,map.Timing.FrameAtMilliseconds(40));Assert.Equal(3,map.Timing.FrameAtMilliseconds(41,FrameTimeKind.Start));
        Assert.Throws<NotSupportedException>(()=>((IList<long>)map.Milliseconds)[0]=9);
    }
    [Theory]
    [InlineData("")][InlineData("garbage")][InlineData("Assume NaN")][InlineData("Assume Infinity")][InlineData("Assume 0")][InlineData("Assume 1001")]
    [InlineData("Assume 1e-20")][InlineData("Assume 25\n0,2,50\n2,4,25")][InlineData("Assume 25\n-1,2,25")][InlineData("Assume 25\n3,2,25")]
    [InlineData("Assume 25\n0,9999999,25")][InlineData("Assume 25\n0.5,2,25")][InlineData("Assume 25\n0,2,25,extra")]
    [InlineData("Assume 25\n0,2,Infinity")][InlineData("Assume 25\n0,0,1e-9")]
    [InlineData("# timecode format v2\n0")][InlineData("# timecode format v2\n0\n0")][InlineData("# timecode format v2\n30\n10")]
    [InlineData("# timecode format v2\n0\n40garbage")][InlineData("# timecode format v2\n0\nNaN")]
    public void InvalidMetadataIsRejectedPredictablyBeforePublication(string text)=>Assert.Throws<ArgumentException>(()=>Read(text));
    [Fact] public void V2ExportExtendsShortMapsForVideoButPreservesExtraFileEntriesAndCancellationIsAtomic()
    {
        var root=Path.Combine(Path.GetTempPath(),"Yoake-timecodes-"+Guid.NewGuid());Directory.CreateDirectory(root);var path=Path.Combine(root,"日本語 မြန်မာ.tc.txt");
        try
        {
            var map=Read("Assume 25\n0,1,50");TimecodeFile.Save(path,map,6);var exported=TimecodeFile.Load(path);
            Assert.Equal(new long[]{0,20,40,80,120,160},exported.Milliseconds);
            TimecodeFile.Save(path,exported,2);Assert.Equal(6,TimecodeFile.Load(path).Milliseconds.Count);var source=File.ReadAllText(path);
            using var cancellation=new CancellationTokenSource();cancellation.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(()=>TimecodeFile.Load(path,cancellation.Token));Assert.ThrowsAny<OperationCanceledException>(()=>TimecodeFile.Save(path,map,6,cancellation.Token));
            Assert.Throws<ArgumentException>(()=>TimecodeFile.Save(path,map,-1));Assert.Equal(source,File.ReadAllText(path));Assert.Single(Directory.GetFiles(root));
        }
        finally{Directory.Delete(root,true);}
    }
}
