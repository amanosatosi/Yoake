using Xunit;
using Yoake.Core.Settings;
using Yoake.Core.Commands;
using Yoake.UI.ViewModels;

namespace Yoake.UI.Tests;

public sealed partial class EditorWorkflowTests
{
    [Fact] public async Task AutoSeekChoosesTheFirstFrameAtOrAfterSubtitleStart()
    {
        var rows=await TimingRows();
        Assert.True(await _model.OpenDummyVideoAsync(new(){Width=320,Height=180,FrameCount=100,FramesPerSecond=25}));
        _model.SelectedEvent=rows[1];_model.SelectedEvent=rows[0];
        Assert.Equal(8,_model.CurrentFrame);
        Assert.Equal(.32,_model.CurrentTimeSeconds,6);
        Assert.Single(_model.VisibleVisualLines());
    }
    [Fact] public async Task SubtitleOpenCommandLoadsIntoATabAndClearsLoadingState()
    {
        var path=Path.Combine(_root,"async-open.ass");
        File.WriteAllText(path,"[Events]\nFormat: Start,End,Text\nDialogue: 0:00:00.00,0:00:01.00,loaded\n");
        var before=_model.ActiveDocumentId;
        await Command(CommandIds.SubtitleOpen,path);
        Assert.Equal(before,_model.ActiveDocumentId);Assert.Single(_model.Tabs);
        Assert.Equal("loaded",Assert.Single(_model.Events).Text);
        Assert.False(_model.OpeningSubtitle);
    }
    [Fact] public async Task SubtitleOpenKeepsEditedUntitledTab()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="unsaved work";
        var original=_model.ActiveDocumentId;
        var path=Path.Combine(_root,"another.ass");File.WriteAllText(path,"[Events]\nFormat: Start,End,Text\nDialogue: 0:00:00.00,0:00:01.00,loaded\n");
        await Command(CommandIds.SubtitleOpen,path);
        Assert.NotEqual(original,_model.ActiveDocumentId);Assert.Equal(2,_model.Tabs.Count);
        await Command(CommandIds.WorkspaceActivateTab,original);Assert.Equal("unsaved work",Assert.Single(_model.Events).Text);
    }
    [Fact] public async Task SubtitleOpenKeepsUntitledTabWithVideo()
    {
        Assert.True(await _model.OpenDummyVideoAsync(new(){Width=320,Height=180,FrameCount=20}));
        var original=_model.ActiveDocumentId;
        var path=Path.Combine(_root,"video-owner.ass");File.WriteAllText(path,"[Events]\nFormat: Start,End,Text\nDialogue: 0:00:00.00,0:00:01.00,loaded\n");
        await Command(CommandIds.SubtitleOpen,path);
        Assert.NotEqual(original,_model.ActiveDocumentId);Assert.Equal(2,_model.Tabs.Count);
        await Command(CommandIds.WorkspaceActivateTab,original);Assert.NotNull(_model.CurrentVideoDetails);
    }
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
