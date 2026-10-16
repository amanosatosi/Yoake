using System.Globalization;
using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    private enum SearchAction { Next, Previous, Replace, ReplaceAll }
    public event EventHandler<SearchWindowCloseEventArgs>? SearchWindowCloseRequested;
    public SubtitleSearchToolState SearchState(Guid document)=>_documents.TryGetValue(document,out var state)?state.Search:throw new InvalidOperationException("Search document is closed.");
    private void RegisterSearchCommands()
    {
        bool Applies(CommandContext context)=>_activeId is not null&&(context.DocumentId is null||context.DocumentId==_activeId)&&_gesture is null;
        _registry.Register(new(new(CommandIds.FindReplace,"Find / Replace…","Find and replace subtitles","Search"),async(_,_)=>
        {if(CommitDraft()&&Dialogs is not null)await Dialogs.ShowFindAsync(this,true);},Applies));
        Actions[CommandIds.FindReplace]=new RegistryCommand(_registry,CommandIds.FindReplace,CurrentContext,hotkeys:()=>HotkeyProfile);
        foreach(var (id,label,action) in new[]{(CommandIds.FindNext,"Find next",SearchAction.Next),("search/next","Find next",SearchAction.Next),
            ("search/previous","Find previous",SearchAction.Previous),("search/replace","Replace",SearchAction.Replace),("search/replace-all","Replace all",SearchAction.ReplaceAll)})
        {
            _registry.Register(new(new(id,label,label,"Search"),async(invocation,token)=>
            {
                var owner=invocation.Context.DocumentId??_activeId;
                try{await ExecuteSearchAsync(action,invocation.Parameter as SubtitleSearchRequest,token);}
                catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
                catch(Exception exception){if(owner is {} document&&_documents.TryGetValue(document,out var state))state.Search.Status=exception.Message;throw;}
            },context=>Applies(context)&&!HasEditOperation));
            Actions[id]=new RegistryCommand(_registry,id,CurrentContext,hotkeys:()=>HotkeyProfile);
        }
        _registry.Register(new(new("search/cancel","Cancel search","Cancel this document's active search","Search"),(invocation,_)=>
        {if((invocation.Context.DocumentId??_activeId) is {} document&&_documents.TryGetValue(document,out var state))state.Search.Cancellation?.Cancel();return ValueTask.CompletedTask;},context=>(context.DocumentId??_activeId) is {} id&&_documents.TryGetValue(id,out var state)&&state.Search.Cancellation is not null));
        Actions["search/cancel"]=new RegistryCommand(_registry,"search/cancel",CurrentContext,hotkeys:()=>HotkeyProfile);
        _registry.Register(new(new("search/close","Close search","Close this document's search window","Search"),(invocation,_)=>
        {if((invocation.Context.DocumentId??_activeId) is {} document)SearchWindowCloseRequested?.Invoke(this,new(document));return ValueTask.CompletedTask;},context=>(context.DocumentId??_activeId) is {} id&&_documents.ContainsKey(id)));
        Actions["search/close"]=new RegistryCommand(_registry,"search/close",CurrentContext,hotkeys:()=>HotkeyProfile);
    }
    private async Task ExecuteSearchAsync(SearchAction action,SubtitleSearchRequest? request,CancellationToken parent)
    {
        if(_activeId is not {} document||!_documents.TryGetValue(document,out var state))return;
        var tool=state.Search;var preferences=request?.Preferences??tool.Preferences;
        if(string.IsNullOrEmpty(preferences.Query)&&request is null)
        {if(Dialogs is not null)await Dialogs.ShowFindAsync(this);return;}
        var search=new SubtitleSearch(preferences.Options()); // Validate before committing a draft.
        if(!CommitDraft())return;
        var changed=tool.Preferences!=preferences;
        if(changed)tool.Match=null;
        tool.Preferences=preferences;
        if(request?.Scope is {} scope)tool.Scope=scope.ToArray();
        else if(preferences.SelectionOnly&&tool.Scope.Length==0)tool.Scope=Selection();
        if(_settings.SearchPreferences!=preferences){_settings=_settings with{SearchPreferences=preferences};_settingsStore.Save(_settings);}
        var selected=tool.Scope.ToHashSet();var lines=state.Editor.Document.Events.Where(line=>!preferences.SelectionOnly||selected.Contains(line)).ToArray();
        var inputs=search.CaptureInputs(lines);var current=SelectedEvent;
        var offset=action==SearchAction.Previous?Math.Min(TextSelectionStart,TextSelectionEnd):Math.Max(TextSelectionStart,TextSelectionEnd);
        var matched=tool.Match;
        // Zero-width regex matches have no selected span. Advance past the
        // previous one before wrapping, so F3 does not remain on it forever.
        if(action==SearchAction.Next&&matched is {Length:0,FieldName:"Text"}&&ReferenceEquals(matched.Line,current)&&offset==matched.Index)
            offset++;
        if(action==SearchAction.Replace&&(matched is null||!ReferenceEquals(matched.Line,current)||matched.FieldName=="Text"&&(Math.Min(TextSelectionStart,TextSelectionEnd)!=matched.Index||Math.Max(TextSelectionStart,TextSelectionEnd)!=matched.Index+matched.Length)))action=SearchAction.Next;
        state.Editing?.Cancel();var cancellation=CancellationTokenSource.CreateLinkedTokenSource(parent);state.Editing=cancellation;tool.Cancellation=cancellation;
        OnPropertyChanged(nameof(HasEditOperation));_registry.NotifyStateChanged();var replaced=false;
        try
        {
            tool.Status=action==SearchAction.ReplaceAll?"Preparing replacements…":"Searching…";
            SubtitleSearchMatch? found=null;SubtitleSearch.Replacement[]? edits=null;SubtitleSearch.Replacement? edit=null;
            var job=_jobs.Run("Subtitle search",token=>
            {
                if(action==SearchAction.ReplaceAll)edits=search.PrepareSnapshotReplacements(inputs,preferences.Replacement,token);
                else if(action==SearchAction.Replace)
                {
                    var input=inputs.FirstOrDefault(candidate=>ReferenceEquals(candidate.Line,matched!.Line))??throw new InvalidOperationException("Search result is outside the selected scope.");
                    edit=search.PrepareReplacement(input,matched!,preferences.Replacement,token);
                }
                else found=search.FindSnapshot(inputs,current,offset,action==SearchAction.Previous,token);
                return Task.CompletedTask;
            },cancellation.Token);
            await job.Completion;cancellation.Token.ThrowIfCancellationRequested();
            if(_disposed||_activeId!=document||!_documents.TryGetValue(document,out var live)||!ReferenceEquals(live,state))return;
            if(action==SearchAction.ReplaceAll)
            {
                var count=0;RunEdit(editor=>count=SubtitleSearch.ApplyReplacements(editor,edits!));tool.Match=null;tool.Status=$"Changed {count} subtitle rows.";
            }
            else if(action==SearchAction.Replace)
            {
                if(edit!.Line.Get(edit.FieldName)!=edit.Original)throw new InvalidOperationException("Search result changed; find again.");
                RunEdit(editor=>editor.SetField(edit.Line,edit.FieldName,edit.Text,"Replace subtitle text"));
                var length=matched!.Length+edit.Text.Length-edit.Original.Length;
                tool.Match=new(edit.Line,matched.Index,length){FieldName=edit.FieldName,OriginalSource=edit.Text};
                SelectSearchResult(tool.Match,preferences.SelectionOnly?tool.Scope:null);tool.Status="Replaced match.";replaced=true;
            }
            else
            {
                tool.Match=found;
                if(found is null)tool.Status="No matches.";
                else
                {
                    if(found.Line.Get(found.FieldName)!=found.OriginalSource)throw new InvalidOperationException("Search result changed; find again.");
                    SelectSearchResult(found,preferences.SelectionOnly?tool.Scope:null);
                    var character=new StringInfo(found.OriginalSource![..found.Index]).LengthInTextElements+1;
                    tool.Status=$"Row {found.Line.Number}, character {character}.";
                }
            }
        }
        catch(OperationCanceledException)when(cancellation.IsCancellationRequested)
        {
            tool.Status="Search canceled.";if(parent.IsCancellationRequested)throw;
        }
        finally
        {
            if(ReferenceEquals(state.Editing,cancellation))state.Editing=null;
            if(ReferenceEquals(tool.Cancellation,cancellation))tool.Cancellation=null;
            cancellation.Dispose();OnPropertyChanged(nameof(HasEditOperation));_registry.NotifyStateChanged();
        }
        if(replaced&&!_disposed&&_activeId==document)await ExecuteSearchAsync(SearchAction.Next,null,parent);
    }
}
