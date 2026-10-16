using System.Text;
using Yoake.Core.Audio;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class AudioClipFileTests
{
    [Fact] public void SelectionSpanIncludesGapsAndCommentsWithoutMutatingSourceOrUndo()
    {
        var editor=new SubtitleEditor(AssDocument.Parse("[Events]\nFormat: "+string.Join(',',AssDocument.EventFormat)+"\n"+
            "Dialogue: 0,0:00:00.10,0:00:00.30,Default,,0,0,0,,{\\future(x)\\distort(1,2,3,4)}日本語 é\n"+
            "Comment: 0,0:00:00.70,0:00:00.90,Default,,0,0,0,,မြန်မာ 👩‍👩‍👧‍👦\n"));
        var source=editor.Document.Serialize();var rows=editor.Document.Events.ToArray();
        Assert.Equal(new AudioClipInterval(100,900),AudioClipInterval.Capture(editor,[rows[1],rows[0]]));
        Assert.Throws<ArgumentException>(()=>AudioClipInterval.Capture(editor,[]));
        Assert.Throws<ArgumentException>(()=>AudioClipInterval.Capture(new SubtitleEditor(AssDocument.CreateEmpty()),rows));
        Assert.Equal(source,editor.Document.Serialize());Assert.False(editor.Undo.CanUndo);
        editor.SetField(rows[1],"Start","unfinished","Invalid timing");Assert.Throws<ArgumentException>(()=>AudioClipInterval.Capture(editor,rows));
    }
    [Fact] public void WavUsesCeilingSampleBoundariesStereoPcmAndReleaseEofClamping()
    {
        using var output=new MemoryStream();List<(long,int)> reads=[];List<double> progress=[];
        AudioClipFile.Write(output,new(11025,2,100),new(1,1000),(start,count)=>
        {reads.Add((start,count));return Enumerable.Range(0,count*2).Select(index=>index%2==0?-1f:1f).ToArray();},progress:progress.Add);
        var bytes=output.ToArray();Assert.Equal(new[]{(12L,88)},reads);Assert.Equal(44+88*4,bytes.Length);
        Assert.Equal("RIFF",Encoding.ASCII.GetString(bytes,0,4));Assert.Equal((uint)bytes.Length-8,BitConverter.ToUInt32(bytes,4));Assert.Equal("WAVEfmt ",Encoding.ASCII.GetString(bytes,8,8));
        Assert.Equal(1,BitConverter.ToUInt16(bytes,20));Assert.Equal(2,BitConverter.ToUInt16(bytes,22));Assert.Equal(11025U,BitConverter.ToUInt32(bytes,24));Assert.Equal(44100U,BitConverter.ToUInt32(bytes,28));
        Assert.Equal(4,BitConverter.ToUInt16(bytes,32));Assert.Equal(16,BitConverter.ToUInt16(bytes,34));Assert.Equal("data",Encoding.ASCII.GetString(bytes,36,4));Assert.Equal(352U,BitConverter.ToUInt32(bytes,40));
        Assert.Equal(short.MinValue,BitConverter.ToInt16(bytes,44));Assert.Equal(short.MaxValue,BitConverter.ToInt16(bytes,46));Assert.Equal(0,progress[0]);Assert.Equal(1,progress[^1]);
        foreach(var interval in new[]{new AudioClipInterval(1000,2000),new AudioClipInterval(1,1)})
        {using var empty=new MemoryStream();AudioClipFile.Write(empty,new(11025,2,100),interval,(_,_)=>throw new InvalidOperationException("Empty clips must not decode."));Assert.Equal(44,empty.Length);}
        Assert.Equal(0,AudioClipFile.Pcm16(float.NaN));Assert.Equal(0,AudioClipFile.Pcm16(float.PositiveInfinity));Assert.Equal(short.MinValue,AudioClipFile.Pcm16(-2));Assert.Equal(short.MaxValue,AudioClipFile.Pcm16(2));
    }
    [Fact] public void ExportIsBoundedAtomicCancellableAndRejectsBadProviderGeometryAndRiffOverflow()
    {
        var root=Path.Combine(Path.GetTempPath(),"Yoake-clip-"+Guid.NewGuid());Directory.CreateDirectory(root);var path=Path.Combine(root,"音声 မြန်မာ.wav");File.WriteAllText(path,"original");
        try
        {
            using var cancellation=new CancellationTokenSource();var reads=0;
            Assert.Throws<OperationCanceledException>(()=>AudioClipFile.Save(path,new(48000,2,96000),new(0,2000),(_,count)=>
            {Assert.True(count*2*2<=65536);reads++;return new float[count*2];},cancellation.Token,value=>{if(value>0)cancellation.Cancel();}));
            Assert.Equal(1,reads);Assert.Equal("original",File.ReadAllText(path));Assert.Single(Directory.GetFiles(root));
            Assert.Throws<InvalidDataException>(()=>AudioClipFile.Save(path,new(48000,2,96000),new(0,2000),(_,_)=>[]));Assert.Equal("original",File.ReadAllText(path));Assert.Single(Directory.GetFiles(root));
            Assert.Throws<IOException>(()=>AudioClipFile.Save(path,new(48000,2,96000),new(0,2000),(_,_)=>throw new IOException("Decoder failed.")));Assert.Equal("original",File.ReadAllText(path));Assert.Single(Directory.GetFiles(root));
            Assert.Throws<ArgumentException>(()=>AudioClipFile.Save(path,new(48000,2,long.MaxValue),new(0,long.MaxValue),(_,_)=>throw new Exception()));
            Assert.Throws<ArgumentException>(()=>AudioClipFile.Save(path,new(0,2,1),new(0,1),(_,_)=>[]));Assert.Equal("original",File.ReadAllText(path));Assert.Single(Directory.GetFiles(root));
            AudioClipFile.Save(path,new(48000,1,48000),new(0,1),(_,count)=>new float[count]);Assert.Equal(44+48*2,new FileInfo(path).Length);Assert.Single(Directory.GetFiles(root));
        }
        finally{Directory.Delete(root,true);}
    }
}
