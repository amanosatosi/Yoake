using Yoake.Core.Subtitles;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed record MangetsuMetadataEdit(Guid Owner,long Revision,MangetsuMetadataKind Kind,string Target,string Text,bool Remove=false);
public sealed partial class MainWindowViewModel
{
    private void RegisterMangetsuMetadataCommands()
    {
        _registry.Register(new(new("mangetsu/metadata","Mangetsu Metadata…","Edit style-extension and actor defaults","Tools"),async (_,token)=>
        { if(Dialogs is not null&&_activeId is {} id&&CommitDraft())await Dialogs.ShowMangetsuMetadataAsync(this,id,token); },context=>_activeId is not null&&!HasGesture&&(context.DocumentId is null||context.DocumentId==_activeId)));
        Actions["mangetsu/metadata"]=new RegistryCommand(_registry,"mangetsu/metadata",CurrentContext,hotkeys:()=>HotkeyProfile);
        _registry.Register(new(new("mangetsu/metadata/apply","Apply Mangetsu metadata","Change renderer metadata in one undo step","Tools"),(invocation,token)=>
        {
            token.ThrowIfCancellationRequested();
            if(invocation.Parameter is not MangetsuMetadataEdit edit||edit.Owner!=_activeId||ActiveEditor is not {} editor||editor.Document.Revision!=edit.Revision||Draft?.IsChanged==true)
                throw new InvalidOperationException("The document changed. Reload metadata before applying.");
            if(edit.Remove)editor.RemoveMangetsuMetadata(edit.Kind,edit.Target);else editor.SetMangetsuMetadata(edit.Kind,edit.Target,edit.Text);
            ReloadDraft();if(edit.Kind==MangetsuMetadataKind.Actor)OnPropertyChanged(nameof(ActorNames));return ValueTask.CompletedTask;
        },context=>_activeId is not null&&!HasGesture&&(context.DocumentId is null||context.DocumentId==_activeId)));
    }
}
