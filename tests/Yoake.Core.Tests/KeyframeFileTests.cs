using Yoake.Core.Media;

namespace Yoake.Core.Tests;

public sealed class KeyframeFileTests
{
    [Theory]
    [InlineData("# XviD 2pass stat file", "i 100\np 50\nb 40\ni 60",new[]{0,3})]
    [InlineData("# ffmpeg 2-pass log file, using xvid codec", "i\np\ni",new[]{0,2})]
    [InlineData("# avconv 2-pass log file, using xvid codec", "i\nb\np\ni",new[]{0,3})]
    [InlineData("##map version 1", "not a frame\n0 I 1\n1 P 1\n2 B 1\n3 I 1",new[]{0,3})]
    [InlineData("#options: x264", "in:0 type:I\nin:1 type:P\nin:2 type:B\nin:3 type:i",new[]{0,3})]
    [InlineData("# WWXD log file, using qpfile format", "# note\n0 I -1\n7 P -1\n12 I -1",new[]{0,12})]
    public void ExactReleaseEncoderFormatsUseTheirActualFrameNumberSemantics(string header,string content,int[] expected)
        =>Assert.Equal(expected,KeyframeFile.Read(new StringReader(header+"\n"+content)));
    [Fact] public void AegisubFormatIgnoresFpsAndNormalizesIndicesWithoutChangingTimestampData()
    {
        var frames=KeyframeFile.Read(new StringReader("\uFEFF# keyframe format v1\r\nfps 23.976\r\n70\r\n0\r\n5\r\n5\r\n"));
        Assert.Equal(new[]{0,5,70},frames);using var output=new StringWriter();KeyframeFile.Write(output,frames);
        Assert.StartsWith("# keyframe format v1",output.ToString());Assert.Contains("fps 0",output.ToString());Assert.Equal(frames,KeyframeFile.Read(new StringReader(output.ToString())));
        Assert.Throws<NotSupportedException>(()=>((IList<int>)frames)[0]=10);
    }
    [Theory][InlineData("")][InlineData("unknown")][InlineData("# keyframe format v1\nfps bad\n0")]
    [InlineData("# keyframe format v1\nfps 0\n-1")][InlineData("# keyframe format v1\nfps 0\n12x")]
    [InlineData("# WWXD log file, using qpfile format\nnot qpfile")]
    public void InvalidInputHasPredictableValidation(string content)=>Assert.Throws<ArgumentException>(()=>KeyframeFile.Read(new StringReader(content)));
    [Fact] public void UnicodePathsRoundTripAndCancelledOrInvalidSaveCannotReplaceExistingFile()
    {
        var root=Path.Combine(Path.GetTempPath(),"Yoake-keyframes-"+Guid.NewGuid());Directory.CreateDirectory(root);var path=Path.Combine(root,"日本語 မြန်မာ.key.txt");
        try
        {
            KeyframeFile.Save(path,new[]{0,5,70});var source=File.ReadAllText(path);Assert.Equal(new[]{0,5,70},KeyframeFile.Load(path));
            Assert.Throws<ArgumentException>(()=>KeyframeFile.Save(path,new[]{5,3}));Assert.Equal(source,File.ReadAllText(path));
            using var cancellation=new CancellationTokenSource();cancellation.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(()=>KeyframeFile.Load(path,cancellation.Token));Assert.ThrowsAny<OperationCanceledException>(()=>KeyframeFile.Save(path,new[]{0},cancellation.Token));
            Assert.Equal(source,File.ReadAllText(path));Assert.Single(Directory.GetFiles(root));
        }
        finally{Directory.Delete(root,true);}
    }
}
