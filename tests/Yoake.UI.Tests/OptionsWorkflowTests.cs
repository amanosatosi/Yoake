using Xunit;
using Yoake.Core.Commands;
using Yoake.Core.Hotkeys;
using Yoake.Core.Settings;
using Yoake.Core.Subtitles;
using Yoake.UI.ViewModels;

namespace Yoake.UI.Tests;

public sealed partial class EditorWorkflowTests
{
    [Fact]
    public async Task OptionsDraftAppliesTogetherAndSurvivesRestartWithoutChangingDocuments()
    {
        await Command(CommandIds.GridInsertAfter);
        _model.Draft!.Text=@"{\fnSubtitle Font\future(x)}日本語 မြန်မာ é";
        await Command(CommandIds.EditCommit);
        var editor=_model.ActiveEditor!;var source=editor.Document.Serialize();var undo=editor.Undo.NextUndoName;
        _model.Draft.Start="unfinished";var draft=_model.Draft;
        var baseline=_model.CaptureOptions();
        var hotkeys=new HotkeyProfile(_model.HotkeyProfile.Defaults,baseline.HotkeyOverrides);
        hotkeys.Set(HotkeyContext.Default,CommandIds.SubtitleNew,["Ctrl-Alt-K"]);
        var edited=baseline with { UiFontFamily="Noto Sans Myanmar",UiFontSize=17, Theme=ThemePreference.Dark,
            VideoAutoSeek=false,VideoFastJumpStep=25,AudioAutoCommit=true,AudioAutoNext=false,
            AudioDefaultDuration=2500,AudioLeadIn=80,AudioLeadOut=200,AudioSpectrum=true,AudioAutoScroll=false,
            ShowOriginal=true,BetterView=false,GridTagDisplay=GridTagDisplayMode.Hidden,
            AutosaveEnabled=false,AutosaveIntervalSeconds=120,RecoveryDirectory="recovery-two",
            AssDrawExecutable="custom ASSDraw.exe",HotkeyOverrides=hotkeys.Overrides,GlobalTimingHotkeys=true };
        Assert.NotEqual(edited.UiFontFamily,_model.UiFontFamily);Assert.True(_model.VideoAutoSeek);
        Assert.Equal("Ctrl-N",_model.Actions[CommandIds.SubtitleNew].Shortcut);
        await _model.ApplyOptionsAsync(baseline,edited);
        var saved=new SettingsStore(Path.Combine(_root,"settings.json")).Load();
        Assert.Equal("Noto Sans Myanmar",saved.UiFontFamily);Assert.Equal(17,saved.UiFontSize);
        Assert.Equal(ThemePreference.Dark,saved.Theme);Assert.False(saved.VideoAutoSeek);
        Assert.Equal(25,saved.VideoFastJumpStep);Assert.Equal(2500,saved.AudioDefaultDuration);
        Assert.Equal(80,saved.AudioLeadIn);Assert.Equal(200,saved.AudioLeadOut);
        Assert.True(_model.AudioAutoCommit);Assert.False(_model.AudioAutoNext);Assert.True(_model.IsAudioSpectrum);
        Assert.False(_model.AudioAutoScroll);Assert.True(_model.ShowOriginal);Assert.False(_model.BetterView);
        Assert.Equal(GridTagDisplayMode.Hidden,_model.GridTagDisplay);Assert.False(_model.AutosaveEnabled);
        Assert.EndsWith("recovery-two",_model.RecoveryDirectory);Assert.Equal(120,_model.AutosaveIntervalSeconds);
        Assert.Equal("Ctrl-Alt-K",_model.Actions[CommandIds.SubtitleNew].Shortcut);
        Assert.Equal("Ctrl-Alt-K",new HotkeyProfile(_model.HotkeyProfile.Defaults,saved.HotkeyOverrides).EffectiveGestures(CommandIds.SubtitleNew,HotkeyContext.Default).Single());
        Assert.Same(draft,_model.Draft);Assert.Equal("unfinished",draft.Start);
        Assert.Same(editor,_model.ActiveEditor);Assert.Equal(source,editor.Document.Serialize());Assert.Equal(undo,editor.Undo.NextUndoName);
    }

    [Fact]
    public async Task OptionsMergePreservesPreferencesChangedOutsideDialogAndRecentFiles()
    {
        var baseline=_model.CaptureOptions();var edited=baseline with { UiFontSize=16 };
        await Command("video/autoseek");await Command("audio/timing/preferences",new AudioTimingPreferencesRequest(4000,50,150));
        var path=Path.Combine(_root,"recent.ass");File.WriteAllText(path,"[Events]\nFormat: "+string.Join(',',AssDocument.EventFormat)+"\n");
        Assert.True(_model.OpenSubtitle(path));
        await _model.ApplyOptionsAsync(baseline,edited);
        var saved=new SettingsStore(Path.Combine(_root,"settings.json")).Load();
        Assert.False(saved.VideoAutoSeek);Assert.Equal(4000,saved.AudioDefaultDuration);Assert.Contains(path,saved.RecentFiles);Assert.Equal(16,saved.UiFontSize);
    }

    [Fact]
    public async Task FailedOptionsWriteDoesNotPublishAnyPreferenceOrBindings()
    {
        var baseline=_model.CaptureOptions();var shortcut=_model.Actions[CommandIds.SubtitleNew].Shortcut;
        var hotkeys=new HotkeyProfile(_model.HotkeyProfile.Defaults,baseline.HotkeyOverrides);
        hotkeys.Set(HotkeyContext.Default,CommandIds.SubtitleNew,["Ctrl-Alt-K"]);
        // A directory occupying the destination reliably fails atomic replacement on all platforms.
        Directory.CreateDirectory(Path.Combine(_root,"settings.json"));
        var failure=await Assert.ThrowsAnyAsync<Exception>(()=>_model.ApplyOptionsAsync(baseline,baseline with {
            UiFontSize=20,VideoAutoSeek=false,AudioLeadIn=500,HotkeyOverrides=hotkeys.Overrides }));
        Assert.True(failure is IOException or UnauthorizedAccessException);
        Assert.Equal(baseline.UiFontSize,_model.UiFontSize);Assert.True(_model.VideoAutoSeek);
        Assert.Equal(100,_model.AudioTimingLeadIn);Assert.Equal(shortcut,_model.Actions[CommandIds.SubtitleNew].Shortcut);
    }
    [Fact]
    public async Task InvalidRecoveryOrAutomationPathCannotPartiallyApplyOtherPreferences()
    {
        var baseline=_model.CaptureOptions();
        await Assert.ThrowsAsync<ArgumentException>(()=>_model.ApplyOptionsAsync(baseline,baseline with { UiFontSize=20,RecoveryDirectory="bad\0path" }));
        Assert.Equal(baseline.UiFontSize,_model.UiFontSize);
        await Assert.ThrowsAsync<ArgumentException>(()=>_model.ApplyOptionsAsync(baseline,baseline with { UiFontSize=20,AutomationIncludeDirectories=["bad\0path"] }));
        Assert.Equal(baseline.UiFontSize,_model.UiFontSize);Assert.False(File.Exists(Path.Combine(_root,"settings.json")));
    }
}
