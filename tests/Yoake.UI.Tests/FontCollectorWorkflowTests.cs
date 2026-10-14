using Xunit;
using Yoake.Core.Commands;
using Yoake.Core.Fonts;

namespace Yoake.UI.Tests;

public sealed partial class EditorWorkflowTests
{
    [Fact] public async Task FontCheckKeepsInvalidDraftSourceHistoryAndSelectionAndCachesBetweenModes()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="{\\fnControlled}日本語 မြန်မာ";_model.Draft.Start="unfinished";var draft=_model.Draft;var line=_model.SelectedEvent;var source=_model.ActiveEditor!.Document.Serialize();var undo=_model.ActiveEditor.Undo.NextUndoName;
        var resolver=new ControlledFontFactory();_model.FontResolvers=resolver;await Command("fonts/start",new FontCollectorRequest(FontCollectionMode.Check,""));
        Assert.Equal(1,resolver.Calls);Assert.Equal(1,resolver.Disposed);Assert.Equal("Controlled",Assert.Single(_model.FontReport!.Fonts).Usage.Request.Family);Assert.Same(draft,_model.Draft);Assert.Same(line,_model.SelectedEvent);Assert.Equal("unfinished",draft.Start);Assert.Equal(source,_model.ActiveEditor.Document.Serialize());Assert.Equal(undo,_model.ActiveEditor.Undo.NextUndoName);
        await Command("fonts/start",new FontCollectorRequest(FontCollectionMode.Check,""));Assert.Equal(1,resolver.Calls);
        draft.Text="{\\fnChanged}a";await Command("fonts/start",new FontCollectorRequest(FontCollectionMode.Check,""));Assert.Equal(2,resolver.Calls);Assert.Equal("Changed",Assert.Single(_model.FontReport!.Fonts).Usage.Request.Family);
    }
    [Fact] public async Task FontCommandsRejectForeignContextAndMissingDestinationsWithoutDocumentMutations()
    {
        await Command(CommandIds.GridInsertAfter);_model.FontResolvers=new ControlledFontFactory();var source=_model.ActiveEditor!.Document.Serialize();
        Assert.False(await _model.Registry.InvokeAsync("fonts/start",new(Guid.NewGuid()),new FontCollectorRequest(FontCollectionMode.Check,"")));
        Assert.False(await _model.Registry.InvokeAsync("fonts/start",new(),new FontCollectorRequest(FontCollectionMode.ScriptFolder,"",RightsAcknowledged:true)));
        Assert.False(await _model.Registry.InvokeAsync("fonts/start",new(),new FontCollectorRequest(FontCollectionMode.Folder,_root)));
        Assert.Equal(source,_model.ActiveEditor.Document.Serialize());Assert.False(_model.FontCollectorBusy);
    }
    [Fact] public async Task CancellationAndDocumentSwitchCannotPublishAnotherTabsFontReport()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="font work";await Command(CommandIds.EditCommit);var owner=_model.ActiveDocumentId;var editor=_model.ActiveEditor;
        var blocking=new ControlledFontFactory{Block=true};_model.FontResolvers=blocking;
        var operation=_model.Registry.InvokeAsync("fonts/start",new(),new FontCollectorRequest(FontCollectionMode.Check,"")).AsTask();await blocking.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Command(CommandIds.SubtitleNew);Assert.NotEqual(owner,_model.ActiveDocumentId);await operation;Assert.Null(_model.FontReport);Assert.False(_model.FontCollectorBusy);Assert.Equal(1,blocking.Disposed);Assert.NotSame(editor,_model.ActiveEditor);
    }
    [Fact] public async Task FontCancelOwnsTheBackgroundJobAndLeavesNoHistoryEntry()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="pending text";await Command(CommandIds.EditCommit);var editor=_model.ActiveEditor!;var undo=editor.Undo.NextUndoName;var source=editor.Document.Serialize();
        var blocking=new ControlledFontFactory{Block=true};_model.FontResolvers=blocking;var operation=_model.Registry.InvokeAsync("fonts/start",new(),new FontCollectorRequest(FontCollectionMode.Check,"")).AsTask();await blocking.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(_model.Actions["fonts/cancel"].CanExecute(null));await Command("fonts/cancel");await operation;Assert.False(_model.FontCollectorBusy);Assert.Null(_model.FontOutput);Assert.Equal(source,editor.Document.Serialize());Assert.Equal(undo,editor.Undo.NextUndoName);Assert.Equal(1,blocking.Disposed);
    }
    private sealed class ControlledFontFactory:IFontResolverFactory
    {
        public int Calls,Disposed;public bool Block;public TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IFontFileResolver Create()=>new Resolver(this);
        private sealed class Resolver(ControlledFontFactory owner):IFontFileResolver
        {
            public ResolvedFont Resolve(FontUsage usage,CancellationToken token)
            {Interlocked.Increment(ref owner.Calls);owner.Started.TrySetResult();if(owner.Block){token.WaitHandle.WaitOne();token.ThrowIfCancellationRequested();}return new(usage,FontMatch.Missing,"","",0,false,false,[],[],["Controlled missing result"]);}
            public void Dispose()=>Interlocked.Increment(ref owner.Disposed);
        }
    }
}
