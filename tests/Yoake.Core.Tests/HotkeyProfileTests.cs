using Yoake.Core.Commands;
using Yoake.Core.Hotkeys;
using Yoake.Core.Settings;

namespace Yoake.Core.Tests;

public sealed class HotkeyProfileTests
{
    [Fact] public void ExactDefaultsKeepAllBindingsAndContextsWithoutConflicts()
    {
        Assert.Equal(120,AegisubHotkeyDefaults.ReferenceBindings.Count);
        Assert.Equal(8,AegisubHotkeyDefaults.ReferenceBindings.Select(binding=>binding.Context).Distinct().Count());
        var profile=new HotkeyProfile(AegisubHotkeyDefaults.ForYoake());
        foreach(var original in AegisubHotkeyDefaults.ReferenceBindings)
            Assert.Contains(profile.Resolver.Bindings,binding=>binding.Context==original.Context&&binding.CommandId==AegisubHotkeyDefaults.MapCommand(original.CommandId)
                &&binding.Gesture.NormalizedKey==original.Gesture.NormalizedKey&&binding.Gesture.Modifiers==original.Gesture.Modifiers);
        Assert.Equal(new[]{"Ctrl-S","F2"},profile.EffectiveGestures("subtitle/save",HotkeyContext.Default));
    }

    [Theory]
    [InlineData(HotkeyContext.Audio,"Right","time/next")]
    [InlineData(HotkeyContext.Video,"Right","video/frame/next")]
    [InlineData(HotkeyContext.SubtitleGrid,"Right","video/frame/next")]
    [InlineData(HotkeyContext.Audio,"S","audio/play/selection")]
    [InlineData(HotkeyContext.Video,"S","video/tool/position")]
    [InlineData(HotkeyContext.Audio,"D","audio/play/selection/end")]
    [InlineData(HotkeyContext.Audio,"H","audio/stop")]
    [InlineData(HotkeyContext.Video,"D","video/tool/rotatez")]
    [InlineData(HotkeyContext.SubtitleEdit,"Enter","edit/commit-next")]
    [InlineData(HotkeyContext.Audio,"Enter","audio/commit")]
    [InlineData(HotkeyContext.Styling,"Enter","tool/styling_assistant/commit")]
    [InlineData(HotkeyContext.Translation,"Enter","tool/translation_assistant/commit")]
    public void SamePhysicalKeyKeepsContextSpecificMeaning(HotkeyContext context,string gesture,string expected)
    {
        var resolver=new HotkeyProfile(AegisubHotkeyDefaults.ForYoake()).Resolver;
        Assert.Equal(expected,resolver.Resolve(HotkeyGesture.Parse(gesture),[HotkeyContext.Default,context])?.CommandId);
    }

    [Fact] public void AudioStopAdapterMigrationRetainsRemapsAndRemovalsAndRunsOnlyOnce()
    {
        var directory=Path.Combine(Path.GetTempPath(),"Yoake-audio-stop-"+Guid.NewGuid());Directory.CreateDirectory(directory);
        try
        {
            var store=new SettingsStore(Path.Combine(directory,"settings.json"));
            File.WriteAllText(store.Path,"""{"hotkeyOverrides":[{"context":3,"commandId":"video/stop","gestures":["F10","H"]},{"context":0,"commandId":"video/stop","gestures":[]}]}""");
            var migrated=store.Load();Assert.True(migrated.AudioStopHotkeysMigrated);
            var profile=new HotkeyProfile(AegisubHotkeyDefaults.ForYoake(),migrated.HotkeyOverrides);
            Assert.Equal(new[]{"F10","H"},profile.EffectiveGestures("audio/stop",HotkeyContext.Audio));
            Assert.Empty(profile.EffectiveGestures("video/stop",HotkeyContext.Audio));Assert.Empty(profile.EffectiveGestures("audio/stop",HotkeyContext.Always));
            profile.Set(HotkeyContext.Audio,"video/stop",["F11"]);store.Save(migrated with {HotkeyOverrides=profile.Overrides});
            var again=store.Load();var restored=new HotkeyProfile(AegisubHotkeyDefaults.ForYoake(),again.HotkeyOverrides);
            Assert.Equal(new[]{"F11"},restored.EffectiveGestures("video/stop",HotkeyContext.Audio));Assert.Equal(new[]{"F10","H"},restored.EffectiveGestures("audio/stop",HotkeyContext.Audio));
            var removed=(new AppSettings {HotkeyOverrides=[new(HotkeyContext.Audio,"video/stop",[])]}).Normalize();
            var empty=new HotkeyProfile(AegisubHotkeyDefaults.ForYoake(),removed.HotkeyOverrides);
            Assert.Empty(empty.EffectiveGestures("audio/stop",HotkeyContext.Audio));Assert.Empty(empty.EffectiveGestures("video/stop",HotkeyContext.Audio));
        }
        finally{Directory.Delete(directory,true);}
    }

    [Fact] public void AlwaysOverridesRequireOptInAndExclusiveModesDoNotLeakDefaults()
    {
        var resolver=new HotkeyResolver();
        resolver.Add(new("default",HotkeyContext.Default,new("Enter")));
        resolver.Add(new("local",HotkeyContext.Audio,new("Enter")));
        resolver.Add(new("global",HotkeyContext.Always,new("Enter")));
        Assert.Equal("local",resolver.Resolve(new("Enter"),[HotkeyContext.Default,HotkeyContext.Audio])?.CommandId);
        Assert.Equal("global",resolver.Resolve(new("Enter"),[HotkeyContext.Default,HotkeyContext.Audio],true)?.CommandId);
        Assert.Equal("default",resolver.Resolve(new("Enter"),[HotkeyContext.Video])?.CommandId);
        Assert.Null(resolver.Resolve(new("Enter"),[HotkeyContext.Default,HotkeyContext.Mode39],true));
        resolver.Add(new("exclusive",HotkeyContext.Mode39,new("Enter")));
        Assert.Equal("exclusive",resolver.Resolve(new("Enter"),[HotkeyContext.Default,HotkeyContext.Mode39],true)?.CommandId);
    }

    [Fact] public void RemappingRemovalResetsAndConflictsAreAtomic()
    {
        var profile=new HotkeyProfile(AegisubHotkeyDefaults.ForYoake());
        profile.Set(HotkeyContext.Default,"subtitle/new",["Ctrl-Alt-N","F10"]);
        Assert.Null(profile.Resolver.Resolve(HotkeyGesture.Parse("Ctrl-N"),[HotkeyContext.Default]));
        Assert.Equal("subtitle/new",profile.Resolver.Resolve(HotkeyGesture.Parse("F10"),[HotkeyContext.Default])?.CommandId);
        var before=profile.Overrides;
        Assert.Throws<InvalidOperationException>(()=>profile.Set(HotkeyContext.Default,"subtitle/open",["F10"]));
        Assert.Equal(before.Select(item=>string.Join(',',item.Gestures)),profile.Overrides.Select(item=>string.Join(',',item.Gestures)));
        Assert.Equal("subtitle/open",profile.Resolver.Resolve(HotkeyGesture.Parse("Ctrl-O"),[HotkeyContext.Default])?.CommandId);
        profile.Set(HotkeyContext.Default,"subtitle/new",[]);Assert.Empty(profile.EffectiveGestures("subtitle/new",HotkeyContext.Default));
        profile.ResetCommand(HotkeyContext.Default,"subtitle/new");Assert.Equal(new[]{"Ctrl-N"},profile.EffectiveGestures("subtitle/new",HotkeyContext.Default));
        profile.Set(HotkeyContext.Audio,"audio/commit",["F10"]);profile.Set(HotkeyContext.Default,"subtitle/new",["F10"]);
        profile.ResetContext(HotkeyContext.Audio);Assert.Equal(new[]{"Enter","G"},profile.EffectiveGestures("audio/commit",HotkeyContext.Audio));
        profile.ResetAll();Assert.Empty(profile.Overrides);Assert.Equal(new[]{"Ctrl-N"},profile.EffectiveGestures("subtitle/new",HotkeyContext.Default));
    }

    [Fact] public void LegacyFrameSplitBindingsMigrateWithoutChangingUserGestures()
    {
        var profile=new HotkeyProfile(AegisubHotkeyDefaults.ForYoake(),[
            new(HotkeyContext.Default,"edit/line/duplicate/shift",["Ctrl-Alt-F9"]),
            new(HotkeyContext.Video,"edit/line/duplicate/shift_back",["Shift-F10"])]);
        Assert.Equal("edit/line/split/before",profile.Resolver.Resolve(HotkeyGesture.Parse("Ctrl-Alt-F9"),[HotkeyContext.Default])?.CommandId);
        Assert.Equal("edit/line/split/after",profile.Resolver.Resolve(HotkeyGesture.Parse("Shift-F10"),[HotkeyContext.Default,HotkeyContext.Video])?.CommandId);
        Assert.DoesNotContain(profile.Overrides,item=>item.CommandId.Contains("duplicate/shift",StringComparison.Ordinal));
        Assert.Equal("edit/line/split/before",new HotkeyProfile(AegisubHotkeyDefaults.ForYoake()).Resolver.Resolve(HotkeyGesture.Parse("Ctrl-D"),[HotkeyContext.Default])?.CommandId);
    }

    [Fact] public void InvalidKeysAreRejectedWithoutChangingEffectiveBindings()
    {
        var profile=new HotkeyProfile(AegisubHotkeyDefaults.ForYoake());
        foreach(var key in new[]{"Ctrl-NotAKey","Ctrl-Ctrl-N","Ctrl-","F99"})
            Assert.Throws<ArgumentException>(()=>profile.Set(HotkeyContext.Default,"subtitle/new",[key]));
        Assert.Equal(new[]{"Ctrl-N"},profile.EffectiveGestures("subtitle/new",HotkeyContext.Default));
        var warnings=new List<string>();
        var restored=new HotkeyProfile(AegisubHotkeyDefaults.ForYoake(),[new(HotkeyContext.Default,"subtitle/new",["bad key"])],warnings.Add);
        Assert.Single(warnings);Assert.Empty(restored.Overrides);
    }

    [Fact] public void OverridesPersistAndDamagedMembersKeepOtherSettings()
    {
        var directory=Path.Combine(Path.GetTempPath(),"Yoake-hotkeys-"+Guid.NewGuid());Directory.CreateDirectory(directory);
        try
        {
            var store=new SettingsStore(Path.Combine(directory,"settings.json"));
            store.Save(new(){GlobalTimingHotkeys=true,HotkeyOverrides=[new(HotkeyContext.Default,"subtitle/new",[]),new(HotkeyContext.Audio,"audio/commit",["F10","Ctrl-Alt-F11"])]});
            var settings=store.Load();var restored=new HotkeyProfile(AegisubHotkeyDefaults.ForYoake(),settings.HotkeyOverrides);
            Assert.True(settings.GlobalTimingHotkeys);Assert.Empty(restored.EffectiveGestures("subtitle/new",HotkeyContext.Default));
            Assert.Equal(new[]{"F10","Ctrl-Alt-F11"},restored.EffectiveGestures("audio/commit",HotkeyContext.Audio));
            File.WriteAllText(store.Path,"""{"viewMode":2,"globalTimingHotkeys":true,"playbackVolume":0.4,"audioWindowSeconds":30,"audioVolumeLinked":true,"automationIncludeDirectories":["?user/custom"],"hotkeyOverrides":[{"context":1,"commandId":"subtitle/new","gestures":["F10"]},17,{"context":999,"commandId":"bad","gestures":[]}]}""");
            settings=store.Load();Assert.Equal(WorkspaceViewMode.Video,settings.ViewMode);Assert.True(settings.GlobalTimingHotkeys);Assert.Equal(0.4,settings.PlaybackVolume);
            Assert.Equal(30,settings.AudioWindowSeconds);Assert.True(settings.AudioVolumeLinked);Assert.Equal(new[]{"?user/custom"},settings.AutomationIncludeDirectories);
            Assert.Single(settings.HotkeyOverrides!);Assert.Equal("subtitle/new",settings.HotkeyOverrides![0].CommandId);
        }
        finally{Directory.Delete(directory,true);}
    }

    [Fact] public async Task DisabledLocalCommandDoesNotInvokeFallbackCommand()
    {
        var registry=new CommandRegistry();var fallbackCalled=false;
        registry.Register(new(new("local","Local","",""),(_,_)=>throw new Exception("Must not execute"),_=>false));
        registry.Register(new(new("fallback","Fallback","",""),(_,_)=>{fallbackCalled=true;return ValueTask.CompletedTask;}));
        var resolver=new HotkeyResolver();resolver.Add(new("fallback",HotkeyContext.Default,new("Enter")));resolver.Add(new("local",HotkeyContext.Audio,new("Enter")));
        var binding=resolver.Resolve(new("Enter"),[HotkeyContext.Default,HotkeyContext.Audio])!;
        Assert.False(await registry.InvokeAsync(binding.CommandId,new()));Assert.False(fallbackCalled);
    }

    [Theory]
    [InlineData("Ctrl-C",HotkeyContext.Default,false)]
    [InlineData("Ctrl-Delete",HotkeyContext.Default,false)]
    [InlineData("Right",HotkeyContext.Video,false)]
    [InlineData("A",HotkeyContext.Video,false)]
    [InlineData("Ctrl-Alt-D",HotkeyContext.Default,false)]
    [InlineData("Enter",HotkeyContext.SubtitleEdit,true)]
    [InlineData("Enter",HotkeyContext.Translation,true)]
    [InlineData("Home",HotkeyContext.Styling,true)]
    [InlineData("Ctrl-B",HotkeyContext.SubtitleEdit,true)]
    public void NativeTextInputIsIsolatedButToolKeysRemainRoutable(string shortcut,HotkeyContext context,bool expected)
    {
        var gesture=HotkeyGesture.Parse(shortcut);var binding=new HotkeyBinding("test",context,gesture);
        Assert.Equal(expected,HotkeyInputPolicy.MayRoute(binding,gesture,true));
        Assert.True(HotkeyInputPolicy.MayRoute(binding,gesture,false));
    }
}
