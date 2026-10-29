using Xunit;
using Yoake.UI;

namespace Yoake.UI.Tests;

public sealed class FileDropPlannerTests
{
    [Fact]
    public void VideoIsACombinedMediaDropAndAudioIsSeparate()
    {
        var root=Path.Combine(Path.GetTempPath(),"yoake-drop-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var video=Path.Combine(root,"日本語.mkv");
            var audio=Path.Combine(root,"မြန်မာ.flac");
            File.WriteAllBytes(video,[]);File.WriteAllBytes(audio,[]);
            Assert.True(FileDropPlanner.TryBuild([video],out var plan,out _));
            Assert.Single(plan);Assert.Equal(DroppedFileType.Video,plan[0].Type);
            Assert.True(FileDropPlanner.TryBuild([audio],out plan,out _));
            Assert.Single(plan);Assert.Equal(DroppedFileType.Audio,plan[0].Type);
            Assert.False(FileDropPlanner.TryBuild([video,audio],out plan,out var message));
            Assert.Empty(plan);Assert.Contains("replaces both",message);
        }
        finally { Directory.Delete(root,true); }
    }

    [Fact]
    public void MultipleSubtitlesAndMetadataAreClassifiedBeforeAnyOpen()
    {
        var root=Path.Combine(Path.GetTempPath(),"yoake-drop-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var first=Path.Combine(root,"一.ass");var second=Path.Combine(root,"二.ssa");
            var timecodes=Path.Combine(root,"time.txt");var keyframes=Path.Combine(root,"keys.txt");
            File.WriteAllText(first,"[Script Info]\n");File.WriteAllText(second,"[Script Info]\n");
            File.WriteAllText(timecodes,"# timecode format v2\n0\n40\n");
            File.WriteAllText(keyframes,"# keyframe format v1\nfps 25\n0\n");
            Assert.True(FileDropPlanner.TryBuild([keyframes,first,timecodes,second],out var files,out _));
            Assert.Equal(new[]{DroppedFileType.Subtitle,DroppedFileType.Subtitle,DroppedFileType.Timecodes,DroppedFileType.Keyframes},
                files.Select(f=>f.Type));
            Assert.False(FileDropPlanner.TryBuild([first,Path.Combine(root,"missing.ass")],out _,out _));
            var notes=Path.Combine(root,"notes.txt");File.WriteAllText(notes,"Just a note");
            Assert.False(FileDropPlanner.TryBuild([first,notes],out _,out _));
        }
        finally { Directory.Delete(root,true); }
    }
}
