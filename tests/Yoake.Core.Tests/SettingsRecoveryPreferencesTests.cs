using Yoake.Core.Settings;

namespace Yoake.Core.Tests;

public sealed class SettingsRecoveryPreferencesTests
{
    [Fact]
    public void ACorruptMemberDoesNotLoseFastJumpKaraokeOrFontPreferences()
    {
        var root=Path.Combine(Path.GetTempPath(),"Yoake-settings-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            var path=Path.Combine(root,"settings.json");
            File.WriteAllText(path,"""{"theme":"damaged","videoFastJumpStep":35,"karaokeAutoCut":false,"uiFontFamily":"日本語 မြန်မာ","uiFontSize":16,"futurePreference":{"enabled":true}}""");
            var store=new SettingsStore(path);var loaded=store.Load();
            Assert.Equal(35,loaded.VideoFastJumpStep);Assert.False(loaded.KaraokeAutoCut);
            Assert.Equal("日本語 မြန်မာ",loaded.UiFontFamily);Assert.Equal(16,loaded.UiFontSize);
            store.Save(loaded);var reopened=store.Load();
            Assert.Equal(35,reopened.VideoFastJumpStep);Assert.False(reopened.KaraokeAutoCut);
            Assert.True(reopened.FutureSettings!["futurePreference"].GetProperty("enabled").GetBoolean());
        }
        finally { Directory.Delete(root,true); }
    }
}
