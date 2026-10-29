using Yoake.Core.Settings;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class SubtitleSearchPreferencesTests
{
    [Fact] public void PreferencesRoundTripAndDamagedMembersRecoverWithoutLosingOtherSettings()
    {
        var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());Directory.CreateDirectory(root);
        try
        {
            var store=new SettingsStore(Path.Combine(root,"settings.json"));
            var preferences=new SubtitleSearchPreferences{Query="日本語 é",Replacement="မြန်မာ",Field=SubtitleSearchField.Actor,SelectionOnly=true,IgnoreComments=true};
            store.Save(new AppSettings{SearchPreferences=preferences,VideoAutoSeek=false});var loaded=store.Load();
            Assert.Equal(preferences,loaded.SearchPreferences);Assert.False(loaded.VideoAutoSeek);Assert.True(loaded.SearchPreferences!.Options().SkipTags);
            File.WriteAllText(store.Path,"""{"searchPreferences":{"query":"日本語","field":99,"rawSource":true},"videoAutoSeek":false,"viewMode":2}""");
            loaded=store.Load();Assert.Equal(SubtitleSearchField.Text,loaded.SearchPreferences!.Field);Assert.Equal("日本語",loaded.SearchPreferences.Query);
            Assert.True(loaded.SearchPreferences.RawSource);Assert.False(loaded.VideoAutoSeek);Assert.Equal(WorkspaceViewMode.Video,loaded.ViewMode);
            File.WriteAllText(store.Path,"""{"searchPreferences":{"matchCase":"bad"},"videoAutoSeek":false,"toolbarVisible":false}""");
            loaded=store.Load();Assert.Equal(new SubtitleSearchPreferences(),loaded.SearchPreferences);Assert.False(loaded.VideoAutoSeek);Assert.False(loaded.ToolbarVisible);
        }
        finally{Directory.Delete(root,true);}
    }
}
