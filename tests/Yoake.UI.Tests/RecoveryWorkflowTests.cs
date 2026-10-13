using Xunit;
using Yoake.Core.Commands;
using Yoake.Core.Settings;
using Yoake.UI.ViewModels;

namespace Yoake.UI.Tests;

public sealed partial class EditorWorkflowTests
{
    [Fact] public async Task AutosavePreservesDirtyHistorySelectionOriginalAndUncommittedDraftAndDeduplicates()
    {
        await Command(CommandIds.GridInsertAfter);var editor=_model.ActiveEditor!;var source=editor.Document.Serialize();var history=editor.Undo.NextUndoName;var line=_model.SelectedEvent;
        _model.Draft!.Text="pending\n日本語";_model.Draft.Start="incomplete";_model.Draft.MarginL="-";var draft=_model.Draft;var original=_model.OriginalText;
        await _model.RunAutosaveAsync();await _model.RunAutosaveAsync();var entries=await _model.ListRecoveryAsync();Assert.Single(entries);
        Assert.Same(draft,_model.Draft);Assert.Same(line,_model.SelectedEvent);Assert.Equal(history,editor.Undo.NextUndoName);Assert.Equal(source,editor.Document.Serialize());Assert.Equal(original,_model.OriginalText);Assert.True(_workspace.ActiveDocument!.IsDirty);
        Assert.Equal("pending\n日本語",entries[0].Metadata!.Draft!.Values["Text"]);Assert.Equal("incomplete",entries[0].Metadata!.Draft!.Values["Start"]);
    }
    [Fact] public async Task RecoveryOpensSeparateUnsavedTabAndRetainsAlreadyOpenInvalidDraftAndHistory()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="recover";_model.Draft.Start="unfinished";await _model.RunAutosaveAsync();
        var entry=(await _model.ListRecoveryAsync()).Single();var owner=_model.ActiveDocumentId;var editor=_model.ActiveEditor;var draft=_model.Draft;var source=editor!.Document.Serialize();
        await Command("recovery/open-version",entry.Path);Assert.NotEqual(owner,_model.ActiveDocumentId);Assert.Null(_model.ActiveSubtitlePath);Assert.True(_workspace.ActiveDocument!.IsDirty);
        Assert.Equal("recover",_model.Draft!.Text);Assert.Equal("unfinished",_model.Draft.Start);Assert.False(_model.CommitDraft());Assert.Equal(source,_model.ActiveEditor!.Document.Serialize());
        // Explicit workspace switching also exercises owner-local retained drafts.
        _workspace.Activate(owner!.Value);Assert.Same(editor,_model.ActiveEditor);Assert.Same(draft,_model.Draft);Assert.Equal("unfinished",_model.Draft!.Start);
    }
    [Fact] public async Task AutosaveDefaultsDisableEnableAndFailuresRetryThroughRealSettingsCommand()
    {
        Assert.True(_model.AutosaveEnabled);Assert.Equal(60,_model.AutosaveIntervalSeconds);await Command(CommandIds.GridInsertAfter);
        await Command("settings/autosave",new AutosaveOptions(false,1,null));await _model.RunAutosaveAsync();Assert.Empty(await _model.ListRecoveryAsync());
        var blocked=Path.Combine(_root,"blocked");File.WriteAllText(blocked,"file");await Command("settings/autosave",new AutosaveOptions(true,1,blocked));await _model.RunAutosaveAsync();Assert.Contains("directory",_model.SubtitleStatus,StringComparison.OrdinalIgnoreCase);
        await Command("settings/autosave",new AutosaveOptions(true,1,null));await _model.RunAutosaveAsync();Assert.Single(await _model.ListRecoveryAsync());
        var persisted=new SettingsStore(Path.Combine(_root,"settings.json")).Load();Assert.True(persisted.AutosaveEnabled);Assert.Equal(1,persisted.AutosaveIntervalSeconds);
        Assert.False(await _model.Registry.InvokeAsync("settings/autosave",new(),new AutosaveOptions(true,0,null)));
    }
    [Fact] public async Task MultipleTabsAndSaveAsFenceDoNotReviveOldPathsOrOverwriteOriginals()
    {
        await Command(CommandIds.GridInsertAfter);var first=_model.ActiveDocumentId;_model.Draft!.Text="first";await Command(CommandIds.EditCommit);var save=Path.Combine(_root,"first.ass");Assert.True(_model.SaveActiveSubtitle(save));
        _model.Draft!.Text="modified";await Command(CommandIds.EditCommit);var job=_model.RunAutosaveAsync();var newPath=Path.Combine(_root,"renamed.ass");Assert.True(_model.SaveActiveSubtitle(newPath));await job;
        await Command(CommandIds.SubtitleNew);await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="second";_model.Draft.Start="invalid";await _model.RunAutosaveAsync();
        Assert.DoesNotContain(await _model.ListRecoveryAsync(),e=>e.Metadata!.DocumentId==first);Assert.DoesNotContain("modified",File.ReadAllText(save));Assert.Contains("modified",File.ReadAllText(newPath));
        await Command("recovery/open-version",(await _model.ListRecoveryAsync()).Single().Path);Assert.Null(_model.ActiveSubtitlePath);Assert.Equal("second",_model.Draft!.Text);
    }
    [Fact] public async Task CleanDisposeCancelsWritesAndRetainsPublishedRecoveryForAnotherProcess()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="retained after clean shutdown";_model.Draft.Start="invalid";await _model.RunAutosaveAsync();
        var path=(await _model.ListRecoveryAsync()).Single().Path;var job=_model.RunAutosaveAsync();_model.Dispose();await job;Assert.True(File.Exists(path));
        var store=new Yoake.Core.Subtitles.SubtitleRecoveryStore(Path.Combine(_root,"recovery"));Assert.Single(await store.ListAsync());
    }
}
