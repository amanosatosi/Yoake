using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    public EditorAssistantSession BeginAssistant(EditorAssistantMode mode)
    {
        if(!CommitDraft())throw new InvalidOperationException(SubtitleStatus);
        if(SelectedEvent is null)throw new InvalidOperationException("Select a subtitle first.");
        return new(this,mode);
    }
    internal void SelectAssistantLine(AssEvent line)
    {
        var previous=_suppressSelectionSeek;_suppressSelectionSeek=true;
        try { SelectedEvent=line; }finally { _suppressSelectionSeek=previous; }
    }
    internal void PublishAssistantField(AssEvent line,string field,string value,string name)
    {
        var editor=ActiveEditor??throw new InvalidOperationException("The document is closed.");
        using(var transaction=editor.Undo.BeginTransaction(name))
        { editor.SetField(line,field,value,name);transaction.Commit(); }
        ReloadDraft();
    }
    private void RegisterAssistantCommands()
    {
        foreach(var (id,label,mode) in new[] { ("tool/translation_assistant","Translation Assistant…",EditorAssistantMode.Translation),("tool/style/assistant","Styling Assistant…",EditorAssistantMode.Styling) })
        {
            _registry.Register(new(new(id,label,label,"Tools"),async (_,token)=>
            { if(Dialogs is not null&&_activeId is {} owner&&CommitDraft())await Dialogs.ShowAssistantAsync(this,owner,mode,token); },
                context=>SelectedEvent is not null&&!HasGesture&&(context.DocumentId is null||context.DocumentId==_activeId)));
            Actions[id]=new RegistryCommand(_registry,id,CurrentContext,hotkeys:()=>HotkeyProfile);
        }
        foreach(var mode in Enum.GetValues<EditorAssistantMode>())
        {
            var prefix=mode==EditorAssistantMode.Translation?"tool/translation_assistant/":"tool/styling_assistant/";
            foreach(var action in new[] { "commit","preview","next","prev","insert_original" })
            {
                if(mode==EditorAssistantMode.Styling&&action=="insert_original")continue;
                var operation=action;var kind=mode;
                _registry.Register(new(new(prefix+action,action+" in "+mode+" Assistant",action,"Tools"),(invocation,_)=>
                {
                    if(invocation.Parameter is not EditorAssistantSession session||!ReferenceEquals(session.Model,this)||session.Mode!=kind||session.DocumentId!=_activeId)
                        throw new ArgumentException("Use this action in its owning Assistant window.");
                    switch(operation)
                    {
                        case "commit":session.Commit(true);break;
                        case "preview":session.Commit(false);break;
                        case "next":session.Move(1);break;
                        case "prev":session.Move(-1);break;
                        case "insert_original":session.InsertOriginal();break;
                    }
                    return ValueTask.CompletedTask;
                },context=>SelectedEvent is not null&&!HasGesture&&(context.DocumentId is null||context.DocumentId==_activeId)));
            }
        }
    }
}
