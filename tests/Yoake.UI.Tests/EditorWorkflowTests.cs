using Xunit;
using Yoake.Core.Commands;
using Yoake.Core.Settings;
using Yoake.Core.Subtitles;
using Yoake.Core.Undo;
using Yoake.Core.Workspace;
using Yoake.UI.Services;
using Yoake.UI.ViewModels;

namespace Yoake.UI.Tests;

public sealed class EditorWorkflowTests : IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"Yoake-tests-"+Guid.NewGuid());
    private readonly WorkspaceManager _workspace=new();
    private readonly MainWindowViewModel _model;
    private readonly TestDialogs _dialogs=new();
    public EditorWorkflowTests()
    {
        Directory.CreateDirectory(_root);
        _model=new(new CommandRegistry(),_workspace,new UndoManager(),new ThemeService(),new SettingsStore(Path.Combine(_root,"settings.json")),new AppSettings());
        _model.Dialogs=_dialogs;
    }
    private async Task Command(string id,object? parameter=null)=>Assert.True(await _model.Registry.InvokeAsync(id,new(),parameter));
    [Fact] public async Task DraftFeedbackIsLiveCoalescedAndRevertible()
    {
        await Command(CommandIds.GridInsertAfter);var line=_model.SelectedEvent!;var revision=_model.PreviewRevision;
        foreach(var text in new[]{"m","mi","mixed မြန်မာ"})_model.Draft.Text=text;
        Assert.Equal("mixed မြန်မာ",line.DisplayText);Assert.Equal("",line.Text);Assert.True(_model.PreviewRevision>revision);
        await Command(CommandIds.EditCancel);Assert.Equal("",line.DisplayText);Assert.Equal("",_model.Draft.Text);
        _model.Draft.Text="one burst";_model.Draft.Text="one burst 日本語";await Command(CommandIds.EditCommit);Assert.Equal("one burst 日本語",line.Text);
        await Command(CommandIds.EditUndo);Assert.Equal("",line.Text);Assert.Single(_model.Events);
    }
    [Fact] public async Task AudioCommandsPersistVolumeMuteAndIndependentDisplayControls()
    {
        await Command("audio/volume",0.35);await Command("audio/mute",true);await Command("audio/display/intensity",3d);await Command("audio/display/height",210d);await Command("styles/layout",new double[]{1.1,1.2,3.5});
        var settings=new SettingsStore(Path.Combine(_root,"settings.json")).Load();Assert.Equal(0.35,settings.PlaybackVolume);Assert.True(settings.PlaybackMuted);Assert.Equal(3,settings.AudioIntensity);Assert.Equal(210,settings.AudioDisplayHeight);Assert.Equal(new double[]{1.1,1.2,3.5},settings.StyleSplitWeights);
    }
    [Fact] public async Task NewDocumentIsConnectedAndInsertCanBeEdited()
    {
        Assert.NotNull(_model.ActiveEditor);Assert.Empty(_model.Events);Assert.Null(_model.Draft);Assert.Equal("",_model.EditorDraft.Text);await Command(CommandIds.GridInsertAfter);
        Assert.Single(_model.Events);Assert.NotNull(_model.Draft);_model.Draft!.Text="日本語 မြန်မာ 👩‍👩‍👧‍👦";
        Assert.True(_workspace.ActiveDocument!.IsDirty);await Command(CommandIds.EditCommit);
        Assert.Equal("日本語 မြန်မာ 👩‍👩‍👧‍👦",_model.Events[0].Text);Assert.Equal("Edit subtitle text",_model.ActiveEditor!.Undo.NextUndoName);
    }
    [Fact] public async Task TabsHaveIndependentHistoryAndCommitBeforeSwitching()
    {
        await Command(CommandIds.GridInsertAfter);var first=_workspace.ActiveDocumentId!.Value;_model.Draft!.Text="first draft";
        await Command(CommandIds.SubtitleNew);Assert.Empty(_model.Events);Assert.False(_model.ActiveEditor!.Undo.CanUndo);
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="second draft";
        await Command(CommandIds.WorkspaceActivateTab,first);Assert.Equal("first draft",_model.Events[0].Text);
        await Command(CommandIds.EditUndo);Assert.Equal("",_model.Events[0].Text);
        await Command(CommandIds.WorkspaceActivateTab,_workspace.Documents[1].Id);Assert.Equal("second draft",_model.Events[0].Text);
    }
    [Fact] public async Task SaveAsChangesPathAndSavedUndoState()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="saved";var path=Path.Combine(_root,"saved.ass");
        Assert.True(_model.SaveActiveSubtitle(path));Assert.Equal(path,_workspace.ActiveDocument!.Path);Assert.Equal("saved.ass",_workspace.ActiveDocument.Title);Assert.False(_workspace.ActiveDocument.IsDirty);
        _model.Draft!.Actor="Actor";await Command(CommandIds.EditCommit);Assert.True(_workspace.ActiveDocument.IsDirty);
        await Command(CommandIds.EditUndo);Assert.False(_workspace.ActiveDocument.IsDirty);
        Assert.Equal("saved",AssDocument.Load(path).Events[0].Text);
    }
    [Fact] public async Task CancelUnsavedCloseRetainsDocumentAndDiscardClosesIt()
    {
        await Command(CommandIds.GridInsertAfter);var id=_workspace.ActiveDocumentId!.Value;
        _dialogs.Choice=UnsavedChoice.Cancel;Assert.False(await _model.CloseDocumentAsync(id));Assert.Contains(_workspace.Documents,d=>d.Id==id);
        _dialogs.Choice=UnsavedChoice.Discard;Assert.True(await _model.CloseDocumentAsync(id));Assert.DoesNotContain(_workspace.Documents,d=>d.Id==id);
    }
    [Fact] public async Task FailedValidationKeepsDraftAndCurrentRow()
    {
        await Command(CommandIds.GridInsertAfter);var first=_model.SelectedEvent;await Command(CommandIds.GridInsertAfter);var second=_model.SelectedEvent;
        _model.Draft!.End="bad";_model.SelectedEvent=first;Assert.Same(second,_model.SelectedEvent);Assert.Same(second,Assert.Single(_model.SelectedEvents));Assert.Equal("bad",_model.Draft.End);Assert.False(_model.CommitDraft());
    }
    [Fact] public async Task ProgrammaticRowJumpSelectsDestinationAndRetainsAnExistingBulkSelection()
    {
        await Command(CommandIds.GridInsertAfter);var first=_model.SelectedEvent!;
        await Command(CommandIds.GridInsertAfter);var second=_model.SelectedEvent!;
        _model.Draft!.Text="pending second";_model.SelectedEvent=first;
        Assert.Same(first,Assert.Single(_model.SelectedEvents));Assert.Equal("pending second",second.Text);
        await Command(CommandIds.GridSelectAll);_model.SelectedEvent=second;
        Assert.Equal(2,_model.SelectedEvents.Count);Assert.Contains(first,_model.SelectedEvents);Assert.Contains(second,_model.SelectedEvents);
        _model.SelectedEvent=null;Assert.Empty(_model.SelectedEvents);Assert.Null(_model.Draft);
    }
    [Fact] public async Task StyleManagerMutationsRefreshMainStyleChoicesBeforeDraftSelection()
    {
        await Command(CommandIds.GridInsertAfter);var style=_model.ActiveEditor!.Document.Styles[0];
        var observed=false;
        _model.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(MainWindowViewModel.EditorDraft)&&_model.Draft?.Style=="Renamed 日本")observed=_model.StyleNames.Contains(_model.Draft.Style);};
        _model.ActiveEditor.EditStyle(style,new Dictionary<string,string>{{"Name","Renamed 日本"}});
        Assert.True(observed);Assert.Equal("Renamed 日本",_model.Draft!.Style);Assert.Contains("Renamed 日本",_model.StyleNames);
        await Command(CommandIds.EditUndo);Assert.Equal("Default",_model.Draft.Style);Assert.Contains("Default",_model.StyleNames);Assert.DoesNotContain("Renamed 日本",_model.StyleNames);
    }
    [Fact] public async Task TimingAndVisualGesturesRollbackOnCancelAndCommitOnce()
    {
        await Command(CommandIds.GridInsertAfter);var line=_model.SelectedEvent!;
        Assert.True(_model.BeginGesture("Timing gesture"));_model.UpdateTimingGesture(2,1);_model.UpdateTimingGesture(2,2);_model.CancelGesture();Assert.Equal(0,line.StartMilliseconds);
        Assert.True(_model.BeginGesture("Position gesture"));_model.UpdatePositionGesture(100,200);Assert.Equal(new AssPoint(100,200),AssVisualTags.Position(_model.VisualText));Assert.Contains("\\pos(100,200)",_model.PreviewSource());_model.UpdatePositionGesture(300,400);_model.EndGesture();Assert.Equal(new AssPoint(300,400),AssVisualTags.Position(line.Text));
        Assert.Equal("Position gesture",_model.ActiveEditor!.Undo.NextUndoName);await Command(CommandIds.EditUndo);Assert.Equal("",line.Text);
    }
    [Fact] public async Task MultiRowCommandsWorkAcrossUndo()
    {
        await Command(CommandIds.GridInsertAfter);await Command(CommandIds.GridInsertAfter);await Command(CommandIds.GridSelectAll);await Command(CommandIds.GridDuplicate);Assert.Equal(4,_model.Events.Count);
        await Command(CommandIds.EditUndo);Assert.Equal(2,_model.Events.Count);await Command(CommandIds.GridSelectAll);await Command(CommandIds.GridToggleComment);Assert.All(_model.Events,l=>Assert.True(l.IsComment));
    }
    [Fact] public async Task ReopenConnectsLoadedRowsAndRecentFiles()
    {
        var doc=AssDocument.CreateEmpty();var editor=new SubtitleEditor(doc);editor.Insert(null,false);var path=Path.Combine(_root,"open.ass");doc.Save(path);
        Assert.True(_model.OpenSubtitle(path));Assert.Single(_model.Events);Assert.Equal(path,_model.RecentFiles[0]);await Command(CommandIds.GridDuplicate);Assert.Equal(2,_model.Events.Count);
    }

    [Fact] public async Task UndoCancelsDraftBeforeUndoingCommittedChanges()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="uncommitted";await Command(CommandIds.EditUndo);Assert.Single(_model.Events);Assert.Equal("",_model.Draft!.Text);
        await Command(CommandIds.EditUndo);Assert.Empty(_model.Events);
    }
    [Fact] public async Task StartingGestureCommitsPendingTextAndCancellationKeepsThatCommit()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="pending";Assert.True(_model.BeginGesture("Drag"));_model.UpdateTimingGesture(2,1);_model.CancelGesture();Assert.Equal("pending",_model.SelectedEvent!.Text);Assert.Equal(0,_model.SelectedEvent.StartMilliseconds);Assert.Equal("Edit subtitle text",_model.ActiveEditor!.Undo.NextUndoName);
    }
    [Fact] public async Task LowercaseFormatFieldsRemainEditable()
    {
        var path=Path.Combine(_root,"lowercase.ass");File.WriteAllText(path,"[Events]\nFormat: layer,start,end,style,name,marginl,marginr,marginv,effect,text\nDialogue: 0,0:00:00.00,0:00:02.00,Default,Old,0,0,0,,Text\n");Assert.True(_model.OpenSubtitle(path));_model.Draft!.Actor="New";await Command(CommandIds.EditCommit);Assert.Equal("New",_model.Events[0].Actor);
    }

    [Fact] public async Task LargeSelectAllPublishesOneSelectionChange()
    {
        var path=Path.Combine(_root,"large.ass");File.WriteAllText(path,"[Events]\nFormat: "+string.Join(',',AssDocument.EventFormat)+"\n"+string.Concat(Enumerable.Range(0,5000).Select(i=>$"Dialogue: 0,0:00:00.00,0:00:02.00,Default,,0,0,0,,Row {i}\n")));Assert.True(_model.OpenSubtitle(path));
        var changes=0;((System.Collections.Specialized.INotifyCollectionChanged)_model.SelectedEvents).CollectionChanged+=(_,_)=>changes++;
        await Command(CommandIds.GridSelectAll);Assert.Equal(5000,_model.SelectedEvents.Count);Assert.Equal(1,changes);
    }

    [Fact] public async Task StructuralUndoAndRedoKeepCurrentDraftAttachedToALiveRow()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="first";await Command(CommandIds.EditCommit);
        await Command(CommandIds.GridDuplicate);var duplicate=_model.SelectedEvent!;
        await Command(CommandIds.EditUndo);Assert.DoesNotContain(duplicate,_model.Events);Assert.Contains(_model.SelectedEvent!,_model.Events);Assert.Equal("first",_model.Draft!.Text);
        await Command(CommandIds.EditRedo);Assert.Equal(2,_model.Events.Count);Assert.Contains(_model.SelectedEvent!,_model.Events);
        await Command(CommandIds.GridSelectAll);await Command(CommandIds.GridDelete);Assert.Empty(_model.Events);Assert.Null(_model.Draft);
        await Command(CommandIds.EditUndo);Assert.Equal(2,_model.Events.Count);Assert.Contains(_model.SelectedEvent!,_model.Events);Assert.All(_model.SelectedEvents,line=>Assert.Contains(line,_model.Events));
        _model.SelectedEvent=_model.Events[1];_model.Draft!.Actor="restored";await Command(CommandIds.EditCommit);Assert.Equal("restored",_model.Events[1].Actor);
        Assert.False(_model.IsSynchronizingSelection);
    }
    [Fact] public async Task SelectionFormattingCommitsDraftAndHasIndependentUndo()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="Hello world";
        _model.TextSelectionStart=6;_model.TextSelectionEnd=11;
        await Command(CommandIds.FormatBold);Assert.Equal("Hello {\\b1}world{\\b0}",_model.SelectedEvent!.Text);
        Assert.Equal("world",_model.Draft!.Text[_model.TextSelectionStart.._model.TextSelectionEnd]);
        await Command(CommandIds.EditUndo);Assert.Equal("Hello world",_model.SelectedEvent.Text);
        await Command(CommandIds.EditRedo);Assert.Equal("Hello {\\b1}world{\\b0}",_model.SelectedEvent.Text);
    }
    [Fact] public async Task FormattingSwatchesAndFlagsFollowDraftStyleAndCaretWithoutMutatingText()
    {
        await Command(CommandIds.GridInsertAfter);
        var style=_model.ActiveEditor!.AddStyle();_model.ActiveEditor.SetField(style,"Italic","-1","Style italic");
        _model.Draft!.Text=@"Hello {\b1\1c&H402010&\1a&H80&}world";
        var original=_model.Draft.Text;_model.TextSelectionStart=10;_model.TextSelectionEnd=10;
        Assert.True(_model.Formatting.Bold);Assert.Equal(new AssColor(16,32,64,128),_model.Formatting.Primary);
        Assert.Equal(original,_model.Draft.Text);
        _model.TextSelectionStart=0;_model.TextSelectionEnd=original.Length;Assert.Null(_model.Formatting.Bold);
        _model.Draft.Style=style.Name;Assert.True(_model.Formatting.Italic);
        await Command(CommandIds.FormatUnderline);
        Assert.Contains("world",_model.Draft!.Text[_model.TextSelectionStart.._model.TextSelectionEnd]);
        await Command(CommandIds.EditUndo);Assert.Equal(original,_model.Draft!.Text);
    }
    public void Dispose(){_model.Dispose();Directory.Delete(_root,true);}
    private sealed class TestDialogs : IEditorDialogs
    {
        public UnsavedChoice Choice=UnsavedChoice.Cancel;
        public Task<string?> OpenSubtitleAsync()=>Task.FromResult<string?>(null);
        public Task<string?> OpenMediaAsync()=>Task.FromResult<string?>(null);
        public Task<string?> SaveSubtitleAsync(string suggestedName)=>Task.FromResult<string?>(null);
        public Task<UnsavedChoice> ConfirmUnsavedAsync(string title)=>Task.FromResult(Choice);
        public Task<bool> ConfirmRevertAsync()=>Task.FromResult(false);
        public Task<string?> ReadClipboardAsync()=>Task.FromResult<string?>(null);
        public Task WriteClipboardAsync(string text)=>Task.CompletedTask;
        public Task ShowStylesAsync(SubtitleEditor editor)=>Task.CompletedTask;
        public Task ShowScriptInfoAsync(SubtitleEditor editor)=>Task.CompletedTask;
        public Task ShowFindAsync(MainWindowViewModel model)=>Task.CompletedTask;
    }
}
