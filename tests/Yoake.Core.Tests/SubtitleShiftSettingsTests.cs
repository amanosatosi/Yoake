using Yoake.Core.Settings;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class SubtitleShiftSettingsTests
{
    [Fact] public void ShiftPreferencesAndHistoryPersistAndNormalizeIndependently()
    {
        var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());Directory.CreateDirectory(root);
        try
        {
            var store=new SettingsStore(Path.Combine(root,"settings.json"));
            var preferences=new SubtitleShiftPreferences{Milliseconds=1230,Frames=2,ByFrames=true,Backward=true,Fields=SubtitleTimingFields.End,Scope=SubtitleTimingScope.SelectionOnward};
            var entry=new SubtitleShiftHistoryEntry(DateTimeOffset.UtcNow,"日本語.ass","Selected rows",preferences);
            store.Save(new(){ShiftPreferences=preferences,ShiftHistory=Enumerable.Repeat(entry,60).ToArray()});var loaded=store.Load();
            Assert.Equal(preferences,loaded.ShiftPreferences);Assert.Equal(50,loaded.ShiftHistory!.Length);Assert.Equal(entry,loaded.ShiftHistory[0]);Assert.Equal(-2,loaded.ShiftPreferences!.Request().Amount);
            File.WriteAllText(store.Path,"""{"shiftPreferences":{"fields":999,"scope":99,"milliseconds":-2},"videoAutoSeek":false,"shiftHistory":[{}, {"when":"bad"}]}""");
            loaded=store.Load();Assert.Equal(new SubtitleShiftPreferences(),loaded.ShiftPreferences);Assert.Empty(loaded.ShiftHistory!);Assert.False(loaded.VideoAutoSeek);
            File.WriteAllText(store.Path,"""{"shiftPreferences":{"byFrames":"bad"},"toolbarVisible":false}""");
            loaded=store.Load();Assert.Equal(new SubtitleShiftPreferences(),loaded.ShiftPreferences);Assert.False(loaded.ToolbarVisible);
        }
        finally{Directory.Delete(root,true);}
    }
}
