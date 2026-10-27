using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.Core.Settings;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    public bool HasEditOperation=>_activeId is {} id&&_documents.TryGetValue(id,out var state)&&state.Editing is not null;
    public GridTagDisplayMode GridTagDisplay=>_settings.GridTagDisplay??GridTagDisplayMode.Simplified;
    private void RegisterGridCommands()
    {
        bool Applies(CommandContext context)=>context.DocumentId is null||context.DocumentId==_activeId;
        foreach(var (id,label,mode) in new[]{("grid/tags/show","Full tags",GridTagDisplayMode.Full),
            ("grid/tags/simplify","Simplified tags",GridTagDisplayMode.Simplified),("grid/tags/hide","Hide tags",GridTagDisplayMode.Hidden)})
        {
            _registry.Register(new(new(id,label,"Subtitle grid tag display: "+label,"Grid"),
                (_,_)=>{SetGridTagDisplay(mode);return ValueTask.CompletedTask;},isChecked:_=>GridTagDisplay==mode));
            Actions[id]=new RegistryCommand(_registry,id,CurrentContext,hotkeys:()=>HotkeyProfile);
        }
        _registry.Register(new(new("grid/tag/cycle_hiding","Cycle tag display","Cycle full, simplified and hidden grid tags","Grid"),
            (_,_)=>{SetGridTagDisplay((GridTagDisplayMode)(((int)GridTagDisplay+1)%3));return ValueTask.CompletedTask;}));
        Actions["grid/tag/cycle_hiding"]=new RegistryCommand(_registry,"grid/tag/cycle_hiding",CurrentContext,hotkeys:()=>HotkeyProfile);
        foreach(var key in Enum.GetValues<SubtitleSortKey>())
        foreach(var selected in new[]{false,true})
        {
            var sortKey=key;var selectedOnly=selected;
            var id="grid/sort/"+(key==SubtitleSortKey.TextStripped?"text_stripped":key.ToString().ToLowerInvariant())+(selected?"/selected":"");
            _registry.Register(new(new(id,"Sort "+(selected?"selected":"all")+" by "+key.ToString().ToLowerInvariant(),"Stable sort of "+(selected?"each selected block":"all subtitle lines"),"Grid"),
                async(_,token)=>await SortLinesAsync(sortKey,selectedOnly,token),context=>Applies(context)&&_gesture is null&&(selectedOnly?Selection().Length:Events.Count)>=2));
            Actions[id]=new RegistryCommand(_registry,id,CurrentContext,hotkeys:()=>HotkeyProfile);
        }
        _registry.Register(new(new("grid/swap","Swap selected lines","Swap exactly two selected subtitle lines","Grid"),
            (_,_)=>{RunEdit(editor=>editor.SwapLines(Selection()));return ValueTask.CompletedTask;},context=>Applies(context)&&_gesture is null&&Selection().Length==2));
        Actions["grid/swap"]=new RegistryCommand(_registry,"grid/swap",CurrentContext,hotkeys:()=>HotkeyProfile);
        _registry.Register(new(new("editor/cancel-operation","Cancel operation","Cancel the current document's background editing operation","Edit"),
            (_,_)=>{if(_activeId is {} id&&_documents.TryGetValue(id,out var state))state.Editing?.Cancel();return ValueTask.CompletedTask;},context=>Applies(context)&&HasEditOperation));
        Actions["editor/cancel-operation"]=new RegistryCommand(_registry,"editor/cancel-operation",CurrentContext,hotkeys:()=>HotkeyProfile);
    }
    private void SetGridTagDisplay(GridTagDisplayMode mode)
    {
        if(GridTagDisplay==mode)return;
        _settings=_settings with{GridTagDisplay=mode};OnPropertyChanged(nameof(GridTagDisplay));_settingsStore.Save(_settings);
    }
    private async Task SortLinesAsync(SubtitleSortKey key,bool selected,CancellationToken parent)
    {
        if(_activeId is not {} id||!_documents.TryGetValue(id,out var state)||!CommitDraft())return;
        state.Editing?.Cancel();var cancellation=CancellationTokenSource.CreateLinkedTokenSource(parent);state.Editing=cancellation;
        OnPropertyChanged(nameof(HasEditOperation));_registry.NotifyStateChanged();
        var editor=state.Editor;SubtitleSortOrder? order=null;
        try
        {
            var snapshot=editor.CaptureSort(key,selected?Selection():null);
            SubtitleStatus="Sorting subtitles…";
            var job=_jobs.Run("Sort subtitles",token=>{order=snapshot.Calculate(token);return Task.CompletedTask;},cancellation.Token);
            await job.Completion;cancellation.Token.ThrowIfCancellationRequested();
            if(_disposed||!_documents.TryGetValue(id,out var live)||!ReferenceEquals(live,state))return;
            if(_activeId==id)RunEdit(active=>active.ApplySort(order!));else editor.ApplySort(order!);
            if(_activeId==id)SubtitleStatus="Subtitles sorted.";
        }
        catch(OperationCanceledException)when(cancellation.IsCancellationRequested)
        {
            if(parent.IsCancellationRequested)throw;
            if(!_disposed&&_activeId==id&&ReferenceEquals(state.Editing,cancellation))SubtitleStatus="Sort canceled.";
        }
        finally
        {
            if(ReferenceEquals(state.Editing,cancellation))state.Editing=null;cancellation.Dispose();
            OnPropertyChanged(nameof(HasEditOperation));_registry.NotifyStateChanged();
        }
    }
}
