using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    private int _textCursor,_splitCursor;
    private string? _splitText;
    private bool _splitAvailable;
    private void SelectCopies(IReadOnlyList<AssEvent> copies)
    {
        Select(copies.FirstOrDefault());SetSelectedEvents(copies);
    }
    private void RegisterLineCommands()
    {
        RegisterOriginalAndPasteCommands();
        bool Applies(CommandContext context)=>context.DocumentId is null||context.DocumentId==_activeId;
        foreach(var (id,label,preserve) in new[]{("edit/clear","Clear line text",false),("edit/clear/text","Clear visible text",true)})
        {
            _registry.Register(new(new(id,label,preserve?"Clear text, retaining override tags and drawings":label,"Edit"),(_,_)=>
            {RunEdit(editor=>editor.ClearText(SelectedEvent!,preserve));return ValueTask.CompletedTask;},context=>Applies(context)&&_gesture is null&&SelectedEvent is not null));
            Actions[id]=new RegistryCommand(_registry,id,CurrentContext,hotkeys:()=>HotkeyProfile);
        }
        foreach(var (id,label,mode) in new[]{("edit/line/split/preserve","Preserve times",CursorSplitTiming.Preserve),
            ("edit/line/split/estimate","Estimate times",CursorSplitTiming.Estimate),("edit/line/split/video","At video frame",CursorSplitTiming.VideoFrame)})
        {
            _registry.Register(new(new(id,label,"Split at text cursor: "+label,"Edit"),(_,_)=>
            {
                var raw=Draft?.Text??"";var cursor=raw[..Math.Clamp(TextCursor,0,raw.Length)].Replace("\r\n","\\N").Replace("\r","\\N").Replace("\n","\\N").Length;
                var timing=_activeId is {} document&&_documents.TryGetValue(document,out var state)?state.FrameTiming:null;
                RunEdit(editor=>Select(editor.SplitAtCursor(SelectedEvent!,cursor,mode,timing,CurrentFrame,FrameTimes.Count)));return ValueTask.CompletedTask;
            },context=>Applies(context)&&_gesture is null&&CanSplitAtCursor()&&(mode!=CursorSplitTiming.VideoFrame||HasFrameBoundaryTiming)));
            Actions[id]=new RegistryCommand(_registry,id,CurrentContext,hotkeys:()=>HotkeyProfile);
        }
        foreach(var (id,label,before) in new[]{("edit/line/split/before","Split before current frame",true),("edit/line/split/after","Split after current frame",false)})
        {
            _registry.Register(new(new(id,label,label,"Edit"),(_,_)=>
            {
                if(_activeId is {} document&&_documents.TryGetValue(document,out var state)&&state.FrameTiming is {} timing)
                    RunEdit(editor=>SelectCopies(editor.SplitAtFrame(Selection(),timing,CurrentFrame,before,FrameTimes.Count)));
                return ValueTask.CompletedTask;
            },context=>Applies(context)&&_gesture is null&&SelectedEvent is not null&&HasFrameBoundaryTiming));
            Actions[id]=new RegistryCommand(_registry,id,CurrentContext,hotkeys:()=>HotkeyProfile);
        }
        foreach(var (id,label,mode) in new[]{("edit/line/join/concatenate","Concatenate",SubtitleJoinMode.Concatenate),
            ("edit/line/join/keep_first","Keep first text",SubtitleJoinMode.KeepFirst),("edit/line/join/as_karaoke","As karaoke",SubtitleJoinMode.Karaoke)})
        {
            _registry.Register(new(new(id,label,"Join selected lines: "+label,"Edit"),(_,_)=>
            {RunEdit(editor=>Select(editor.Join(Selection(),mode)));return ValueTask.CompletedTask;},context=>Applies(context)&&_gesture is null&&Selection().Length>=2));
            Actions[id]=new RegistryCommand(_registry,id,CurrentContext,hotkeys:()=>HotkeyProfile);
        }
    }
    private bool HasFrameBoundaryTiming=>_media?.HasVideo==true&&_activeId is {} id&&_documents.TryGetValue(id,out var state)&&state.FrameTiming is not null;
    private bool CanSplitAtCursor()
    {
        if(SelectedEvent is null||Draft is null)return false;
        if(ReferenceEquals(_splitText,Draft.Text)&&_splitCursor==TextCursor)return _splitAvailable;
        _splitText=Draft.Text;_splitCursor=TextCursor;
        var cursor=AssText.SafeSplitIndex(Draft.Text,TextCursor);return _splitAvailable=cursor>0&&cursor<Draft.Text.Length;
    }
}
