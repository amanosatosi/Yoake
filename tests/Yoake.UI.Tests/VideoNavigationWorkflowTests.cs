using Xunit;
using Yoake.Core.Settings;
using Yoake.Core.Commands;
using Yoake.UI.ViewModels;

namespace Yoake.UI.Tests;

public sealed partial class EditorWorkflowTests
{
    [Fact] public async Task VideoNavigationRequiresVideoAndPreservesPendingTimingAndDraftWithoutIt()
    {
        await TimingRows();await Command("time/lead/out");_model.Draft!.Text+=" retained draft";
        var source=_model.ActiveEditor!.Document.Serialize();var draft=_model.Draft;var range=_model.PrimaryAudioRange;
        foreach(var id in new[]{"video/play","video/play/line","video/stop","video/jump","video/seek/frame","video/seek",
            "timing/jump-start","timing/jump-end","video/frame/next-keyframe","video/frame/previous-keyframe",
            "video/frame/next/boundary","video/frame/prev/boundary","video/frame/next/large","video/frame/prev/large","video/focus_seek"})
        {
            Assert.False(_model.Actions[id].CanExecute(null));Assert.False(await _model.Registry.InvokeAsync(id,new()));
            Assert.False(await _model.Registry.InvokeAsync(id,new(Guid.NewGuid()),new VideoJumpRequest(Frame:1)));
        }
        Assert.Equal(source,_model.ActiveEditor.Document.Serialize());Assert.Same(draft,_model.Draft);Assert.Equal(range,_model.PrimaryAudioRange);Assert.False(_model.IsPlaying);
    }
    [Fact] public void VideoLineShortcutUsesOnlyTheVideoContextAndDisplaysItsUsableBinding()
    {
        var gesture=Yoake.Core.Hotkeys.HotkeyGesture.Parse("Shift-Space");
        Assert.Equal("video/play/line",_model.Hotkeys.Resolve(gesture,[Yoake.Core.Hotkeys.HotkeyContext.Default,Yoake.Core.Hotkeys.HotkeyContext.Video],false)!.CommandId);
        Assert.Null(_model.Hotkeys.Resolve(gesture,[Yoake.Core.Hotkeys.HotkeyContext.Default,Yoake.Core.Hotkeys.HotkeyContext.SubtitleEdit],false));
        Assert.Null(_model.Hotkeys.Resolve(Yoake.Core.Hotkeys.HotkeyGesture.Parse("Home"),[Yoake.Core.Hotkeys.HotkeyContext.Default,Yoake.Core.Hotkeys.HotkeyContext.SubtitleGrid],false));
        Assert.Equal("Shift-Space",_model.Actions["video/play/line"].Shortcut);
        var binding=_model.Hotkeys.Resolve(gesture,[Yoake.Core.Hotkeys.HotkeyContext.Video],false)!;
        Assert.False(Yoake.Core.Hotkeys.HotkeyInputPolicy.MayRoute(binding,gesture,true));
        Assert.True(Yoake.Core.Hotkeys.HotkeyInputPolicy.MayRoute(binding,gesture,false));
    }
    [Fact] public async Task FastJumpPreferenceDefaultsPersistsAndRejectsBadValues()
    {
        Assert.Equal(10m,_model.VideoFastJumpStep);await Command("video/preferences/fast-step",3);
        Assert.Equal(3m,_model.VideoFastJumpStep);Assert.Equal(3,new SettingsStore(System.IO.Path.Combine(_root,"settings.json")).Load().VideoFastJumpStep);
        Assert.False(await _model.Registry.InvokeAsync("video/preferences/fast-step",new(),0));Assert.Equal(3m,_model.VideoFastJumpStep);
        Assert.Equal(10,new AppSettings{VideoFastJumpStep=-1}.Normalize().VideoFastJumpStep);
    }
}
