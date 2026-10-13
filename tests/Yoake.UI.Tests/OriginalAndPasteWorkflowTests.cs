using Xunit;
using Yoake.Core.Commands;
using Yoake.Core.Hotkeys;
using Yoake.Core.Settings;
using Yoake.Core.Subtitles;

namespace Yoake.UI.Tests;

public sealed partial class EditorWorkflowTests
{
    private async Task SeedOriginal()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="原文 é 👩‍👩‍👧";await Command(CommandIds.EditCommit);
        await Command(CommandIds.GridInsertAfter);await Command(CommandIds.GridLinePrevious);
    }
    [Fact] public async Task OriginalSnapshotSurvivesDraftCommitUndoRedoInvalidMetadataAndLineSelection()
    {
        await SeedOriginal();var original=_model.OriginalText;Assert.Equal("原文 é 👩‍👩‍👧",original);
        _model.Draft!.Text="translated";Assert.Equal(original,_model.OriginalText);await Command(CommandIds.EditCommit);
        await Command(CommandIds.EditUndo);Assert.Equal(original,_model.OriginalText);await Command(CommandIds.EditRedo);Assert.Equal(original,_model.OriginalText);
        _model.Draft!.Start="invalid";_model.Draft.Text="invalid draft";Assert.False(_model.CommitDraft());Assert.Equal(original,_model.OriginalText);
        var active=_model.SelectedEvent;_model.SelectedEvent=_model.Events[1];Assert.Same(active,_model.SelectedEvent);Assert.Equal(original,_model.OriginalText);
        await Command(CommandIds.EditCancel);await Command(CommandIds.GridLineNext);Assert.Equal("",_model.OriginalText);await Command(CommandIds.GridLinePrevious);Assert.Equal("translated",_model.OriginalText);
    }
    [Fact] public async Task InsertRevertClearAndClearTextUseOriginalAndCurrentCaret()
    {
        await SeedOriginal();var original=_model.OriginalText;_model.Draft!.Text="prefix selection suffix";
        _model.TextSelectionStart=7;_model.TextSelectionEnd=16;await Command("edit/insert_original");Assert.Equal("prefix "+original+" suffix",_model.Draft!.Text);
        await Command("edit/revert");Assert.Equal(original,_model.Draft!.Text);
        _model.Draft.Text="{\\future(x)\\distort(1,2)}translated{\\p1}m 0 0 l 5 5{\\p0}";
        await Command("edit/clear/text");Assert.Equal("{\\future(x)\\distort(1,2)}{\\p1}m 0 0 l 5 5{\\p0}",_model.Draft!.Text);
        Assert.Equal(original,_model.OriginalText);await Command("edit/clear");Assert.Equal("",_model.Draft!.Text);await Command("edit/revert");Assert.Equal(original,_model.Draft!.Text);
    }
    [Fact] public async Task OriginalIsDocumentLocalAndTogglePreservesDraftSelectionAndPersists()
    {
        await SeedOriginal();var doc=_model.ActiveDocumentId;var original=_model.OriginalText;var draft=_model.Draft;
        _model.TextSelectionStart=1;_model.TextSelectionEnd=3;await Command("edit/show_original");Assert.True(_model.ShowOriginal);Assert.Same(draft,_model.Draft);Assert.Equal(1,_model.TextSelectionStart);
        Assert.True(new SettingsStore(Path.Combine(_root,"settings.json")).Load().ShowOriginal);
        await Command(CommandIds.SubtitleNew);await Command(CommandIds.GridInsertAfter);Assert.Equal("",_model.OriginalText);
        await Command(CommandIds.WorkspaceActivateTab,doc);Assert.Equal(original,_model.OriginalText);
        Assert.False(await _model.Registry.InvokeAsync("edit/revert",new(Guid.NewGuid())));
    }
    [Fact] public async Task PasteOverUsesDialogFieldsPreservesOriginalSelectionAndOneUndo()
    {
        await SeedOriginal();var original=_model.OriginalText;var editor=_model.ActiveEditor!;var source=editor.Document.Serialize();var selection=_model.SelectedEvents.ToArray();
        _dialogs.ClipboardText="日本語\r\nမြန်မာ";_dialogs.PasteFields=PasteOverFields.Text;
        await Command("edit/line/paste/over");Assert.Equal("日本語",_model.Events[0].Text);Assert.Equal("မြန်မာ",_model.Events[1].Text);Assert.Equal(original,_model.OriginalText);Assert.Equal(selection,_model.SelectedEvents);
        Assert.Equal(PasteOverFields.Text,_model.PasteOverPreferences);await Command(CommandIds.EditUndo);Assert.Equal(source,editor.Document.Serialize());
        _model.SetSelectedEvents([]);Assert.False(_model.Actions["edit/line/paste/over"].CanExecute(null));
    }
    [Fact] public async Task PasteCancellationNoneFailuresAndStaleDialogsNeverPublish()
    {
        await SeedOriginal();var editor=_model.ActiveEditor!;var source=editor.Document.Serialize();var draft=_model.Draft!;
        draft.Text="pending";_dialogs.ClipboardText="replacement";_dialogs.PasteFields=null;await Command("edit/line/paste/over");Assert.Same(draft,_model.Draft);Assert.Equal(source,editor.Document.Serialize());
        _dialogs.PasteFields=PasteOverFields.None;await Command("edit/line/paste/over");Assert.Same(draft,_model.Draft);Assert.Equal(source,editor.Document.Serialize());
        _dialogs.FailTextClipboard=true;Assert.False(await _model.Registry.InvokeAsync("edit/line/paste/over",new()));Assert.Equal(source,editor.Document.Serialize());_dialogs.FailTextClipboard=false;
        _dialogs.ClipboardText="Dialogue: malformed";Assert.False(await _model.Registry.InvokeAsync("edit/line/paste/over",new()));Assert.Equal(source,editor.Document.Serialize());
        _dialogs.ClipboardText="replacement";_dialogs.PasteDialog=()=>{draft.Text="changed while waiting";return Task.FromResult<PasteOverFields?>(PasteOverFields.Text);};
        await Command("edit/line/paste/over");Assert.Equal(source,editor.Document.Serialize());Assert.Equal("changed while waiting",draft.Text);
    }
    [Fact] public void PasteOverShortcutRoutesThroughTextWhileOrdinaryPasteAndAltGrRemainNative()
    {
        var paste=new HotkeyBinding("edit/line/paste/over",HotkeyContext.Default,HotkeyGesture.Parse("Ctrl-Shift-V"));
        Assert.True(HotkeyInputPolicy.MayRoute(paste,paste.Gesture,true));
        Assert.False(HotkeyInputPolicy.MayRoute(new("edit/line/paste",HotkeyContext.Default,HotkeyGesture.Parse("Ctrl-V")),HotkeyGesture.Parse("Ctrl-V"),true));
        Assert.False(HotkeyInputPolicy.MayRoute(paste,HotkeyGesture.Parse("Ctrl-Alt-V"),true));
    }
    [Fact] public async Task PasteOverRetainsAnUnselectedActiveDraft()
    {
        await SeedOriginal();await Command(CommandIds.GridInsertAfter);await Command(CommandIds.GridLinePrevious);
        var active=_model.SelectedEvent!;var source=active.Text;var draft=_model.Draft!;draft.Text="keep this pending draft";
        _model.SetSelectedEvents(_model.Events.Where(l=>!ReferenceEquals(l,active)));_dialogs.ClipboardText="first\nsecond";_dialogs.PasteFields=PasteOverFields.Text;
        await Command("edit/line/paste/over");Assert.Same(draft,_model.Draft);Assert.Equal("keep this pending draft",draft.Text);Assert.Equal(source,active.Text);
        Assert.Equal(new[]{"first","second"},_model.Events.Where(l=>!ReferenceEquals(l,active)).Select(l=>l.Text));
    }
    [Fact] public void OriginalAndPastePreferencesSurviveUnrelatedMalformedSettings()
    {
        var path=Path.Combine(_root,"damaged-settings.json");File.WriteAllText(path,"""{"showOriginal":true,"pasteOverFields":1024,"theme":{},"future":{"keep":true}}""");
        var store=new SettingsStore(path);var settings=store.Load();Assert.True(settings.ShowOriginal);Assert.Equal(PasteOverFields.Text,settings.PasteOverFields);Assert.True(settings.FutureSettings!.ContainsKey("future"));
        store.Save(settings);Assert.True(store.Load().ShowOriginal);Assert.Equal(PasteOverFields.Text,store.Load().PasteOverFields);
    }
    [Fact] public async Task OriginalToggleReportsPersistenceFailureWithoutInconsistentViewOrDraftLoss()
    {
        await SeedOriginal();var source=_model.ActiveEditor!.Document.Serialize();var draft=_model.Draft;var notifications=0;
        _model.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(Yoake.UI.ViewModels.MainWindowViewModel.ShowOriginal))notifications++;};
        Directory.CreateDirectory(Path.Combine(_root,"settings.json"));
        Assert.False(await _model.Registry.InvokeAsync("edit/show_original",new()));Assert.True(_model.ShowOriginal);Assert.Equal(1,notifications);
        Assert.True(_model.Registry.GetRequired("edit/show_original").IsChecked(new()));Assert.Same(draft,_model.Draft);Assert.Equal(source,_model.ActiveEditor.Document.Serialize());Assert.NotEmpty(_model.SubtitleStatus);
    }
}
