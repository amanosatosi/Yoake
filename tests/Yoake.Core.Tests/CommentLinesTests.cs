using Yoake.Core.Commands;
using Yoake.Core.Hotkeys;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class CommentLinesTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    public void SelectionUsesOneTargetAndOneLosslessUndo(bool first, bool second, bool expected)
    {
        var source = "[Events]\nFormat: Start, End, Style, Name, Effect, Text\n" +
            $"{(first ? "Comment" : "Dialogue")}: 0:00:01.00,0:00:02.00,Default,Actor,fx,{{\\custom(1)}}日本語\\Nမြန်မာ\n" +
            $"{(second ? "Comment" : "Dialogue")}: 0:00:02.00,0:00:03.00,Other,B,extra,text\n";
        var editor = new SubtitleEditor(AssDocument.Parse(source));
        var lines = editor.Document.Events.ToArray();
        var fields = lines.Select(line => line.FieldNames.Select(line.Get).ToArray()).ToArray();
        editor.ToggleComment(lines);
        Assert.All(lines, line => Assert.Equal(expected, line.IsComment));
        for (var i = 0; i < lines.Length; i++) Assert.Equal(fields[i], lines[i].FieldNames.Select(lines[i].Get));
        editor.Undo.Undo();
        Assert.Equal(source, editor.Document.Serialize());
        Assert.False(editor.Undo.CanUndo);
        editor.Undo.Redo();
        Assert.All(lines, line => Assert.Equal(expected, line.IsComment));
    }

    [Fact]
    public void DefaultAndCustomHotkeysUseExistingCommand()
    {
        var profile = new HotkeyProfile(AegisubHotkeyDefaults.ForYoake());
        var gesture = HotkeyGesture.Parse("Alt-C");
        foreach (var context in new[] { HotkeyContext.SubtitleGrid, HotkeyContext.SubtitleEdit })
        {
            var binding = profile.Resolver.Resolve(gesture, [HotkeyContext.Default, context]);
            Assert.Equal(CommandIds.GridToggleComment, binding?.CommandId);
            Assert.True(HotkeyInputPolicy.MayRoute(binding!, gesture, true));
        }
        profile.Set(HotkeyContext.Default, "edit/line/comment", ["Alt-F9"]);
        Assert.Null(profile.Resolver.Resolve(gesture, [HotkeyContext.SubtitleGrid]));
        Assert.Equal(CommandIds.GridToggleComment, profile.Resolver.Resolve(HotkeyGesture.Parse("Alt-F9"), [HotkeyContext.SubtitleGrid])?.CommandId);
    }
}
