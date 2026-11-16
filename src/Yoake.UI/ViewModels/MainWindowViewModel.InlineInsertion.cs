using Yoake.Core.Subtitles;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void RefreshInlineSelection(bool previouslySelected)
    {
        if(previouslySelected!=(TextSelectionStart!=TextSelectionEnd)&&Actions.TryGetValue("text/insert/furigana",out var command))command.Refresh();
    }
    private void RegisterInlineInsertionCommands()
    {
        bool Applies(Yoake.Core.Commands.CommandContext context) => !_disposed && _activeId is {} owner &&
            (context.DocumentId is null || context.DocumentId==owner) && HasSelectedEvent && !HasGesture;
        foreach (var pair in AssInlineInsertion.Brackets)
        {
            var id="text/insert/brackets/"+pair.Id;
            _registry.Register(new(new(id,"Insert "+pair.Left+pair.Right,"Wrap the selection or insert paired Japanese brackets at the caret","Edit"),
                (_,token)=>{token.ThrowIfCancellationRequested();if(Draft is null)return ValueTask.CompletedTask;AssInlineInsertion.Bracket(Draft.Text,TextSelectionStart,TextSelectionEnd,pair.Id);FormatText("Insert Japanese brackets",(text,start,end,_)=>AssInlineInsertion.Bracket(text,start,end,pair.Id));return ValueTask.CompletedTask;},Applies));
            Actions[id]=new RegistryCommand(_registry,id,CurrentContext,hotkeys:()=>HotkeyProfile);
        }
        const string furigana="text/insert/furigana";
        _registry.Register(new(new(furigana,"Insert furigana…","Add Mangetsu reading and opposite-side reading to selected base text","Edit"),
            async (_,token)=>
            {
                if(Dialogs is null || CaptureFormattingTarget() is not {} current || Draft is null)return;
                AssInlineInsertion.FuriganaBase(Draft.Text,TextSelectionStart,TextSelectionEnd);
                var choice=await Dialogs.ChooseFuriganaAsync(Draft.Text,TextSelectionStart,TextSelectionEnd,token);
                token.ThrowIfCancellationRequested();
                if(choice is not null&&current())
                {
                    AssInlineInsertion.Furigana(Draft!.Text,TextSelectionStart,TextSelectionEnd,choice);
                    FormatText("Insert furigana",(text,start,end,_)=>AssInlineInsertion.Furigana(text,start,end,choice));
                }
            },context=>Applies(context)&&Dialogs is not null&&TextSelectionStart!=TextSelectionEnd));
        Actions[furigana]=new RegistryCommand(_registry,furigana,CurrentContext,hotkeys:()=>HotkeyProfile);
    }
}
