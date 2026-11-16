using Xunit;
using Yoake.Core.Commands;
using Yoake.Core.Subtitles;

namespace Yoake.UI.Tests;

public sealed partial class EditorWorkflowTests
{
    [Fact]
    public async Task JapaneseLegacyProjectCanBeTranslatedAndSavedAsUtf8()
    {
        var source=AssDocument.CreateEmpty().Serialize()+"Dialogue: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,,日本語\r\n";
        var original=Path.Combine(_root,"legacy.ass");var bytes=SubtitleEncodings.Get(932).GetBytes(source);File.WriteAllBytes(original,bytes);
        Assert.True(_model.OpenSubtitle(original,SubtitleEncodings.Get(932)));Assert.Equal("日本語",_model.Events.Single().Text);
        _model.Draft!.Text="မြန်မာ";await Command(CommandIds.EditCommit);
        Assert.False(_model.SaveActiveSubtitle(original));Assert.Equal(bytes,File.ReadAllBytes(original));
        var unicode=Path.Combine(_root,"translated.ass");await Command("subtitle/save/utf8",unicode);
        Assert.Equal("မြန်မာ",AssDocument.Load(unicode).Events.Single().Text);Assert.Equal(65001,_model.ActiveEditor!.Document.EncodingCodePage);
        Assert.Equal(bytes,File.ReadAllBytes(original));
    }
}
