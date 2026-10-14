using Xunit;
using Yoake.Core.Commands;
using Yoake.Core.Tools;

namespace Yoake.UI.Tests;

public sealed partial class EditorWorkflowTests
{
    [Fact] public async Task AssDrawMissingIsDiscoverableAndPreservesAnInvalidEditorDraft()
    {await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="日本語 draft";_model.Draft.Start="unfinished";var draft=_model.Draft;var source=_model.ActiveEditor!.Document.Serialize();var history=_model.ActiveEditor.Undo.NextUndoName;_model.AssDrawApplicationDirectory=_root;Assert.True(_model.Actions["tool/assdraw"].CanExecute(null));await Command("tool/assdraw");Assert.Equal(AssDrawLauncher.MissingMessage,_model.SubtitleStatus);Assert.Same(draft,_model.Draft);Assert.Equal("unfinished",draft.Start);Assert.Equal(source,_model.ActiveEditor.Document.Serialize());Assert.Equal(history,_model.ActiveEditor.Undo.NextUndoName);}
    [Fact] public async Task AssDrawConfiguredLaunchIsExplicitRepeatableAndDoesNotCommitOrSwitchTabs()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="unique draft";var editor=_model.ActiveEditor;var row=_model.SelectedEvent;var id=_model.ActiveDocumentId;var source=editor!.Document.Serialize();
        var directory=Path.Combine(_root,"日本語 tools with spaces");Directory.CreateDirectory(directory);var executable=Path.Combine(directory,"ASSDraw3.exe");File.WriteAllText(executable,"Path fixture; mocked launch only.");var launcher=new ControlledLauncher();_model.AssDrawProcess=launcher;_model.AssDrawApplicationDirectory=_root;
        await Command("settings/assdraw/path",executable);await Command("tool/assdraw");await Command("tool/assdraw");Assert.Equal(new[]{executable,executable},launcher.Paths);Assert.Equal(id,_model.ActiveDocumentId);Assert.Same(editor,_model.ActiveEditor);Assert.Same(row,_model.SelectedEvent);Assert.Equal(source,editor.Document.Serialize());Assert.Equal("unique draft",_model.Draft!.Text);
        File.Delete(executable);await Command("tool/assdraw");Assert.Equal(2,launcher.Paths.Count);Assert.Equal(AssDrawLauncher.MissingMessage,_model.SubtitleStatus);
    }
    [Fact] public async Task AssDrawLaunchAndConfigurationFailuresRemainUiSafeAndLeaveEditorUntouched()
    {
        await Command(CommandIds.GridInsertAfter);var source=_model.ActiveEditor!.Document.Serialize();var executable=Path.Combine(_root,"ASSDraw3.exe");File.WriteAllText(executable,"Path fixture");_model.AssDrawProcess=new ControlledLauncher{Fail=true};_model.AssDrawApplicationDirectory=_root;
        Assert.False(await _model.Registry.InvokeAsync("settings/assdraw/path",new(),"relative.exe"));Assert.Null(_model.AssDrawExecutable);Assert.False(await _model.Registry.InvokeAsync("tool/assdraw",new()));Assert.Contains("Could not launch ASSDraw3",_model.SubtitleStatus);Assert.Equal(source,_model.ActiveEditor.Document.Serialize());
        using var canceled=new CancellationTokenSource();canceled.Cancel();Assert.False(await _model.Registry.InvokeAsync("tool/assdraw",new(),cancellationToken:canceled.Token));Assert.Equal(source,_model.ActiveEditor.Document.Serialize());
    }
    private sealed class ControlledLauncher:IExternalToolLauncher
    {public bool Fail;public List<string> Paths=[];public void Launch(string executable){if(Fail)throw new IOException("Controlled process-start failure.");Paths.Add(executable);}}
}
