using Yoake.Core.Commands;
using Yoake.Core.Undo;
using Yoake.Core.Subtitles;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    public bool ShowOriginal=>_settings.ShowOriginal;
    public string OriginalText=>_activeId is {} id&&_documents.TryGetValue(id,out var state)?state.OriginalText:"";
    public PasteOverFields PasteOverPreferences=>_settings.PasteOverFields is {} fields&&(fields&~PasteOverFields.All)==0?fields:PasteOverFields.Text;
    private bool _pasteOverBusy;
    private bool _publishingPasteOver;
    private void RefreshOriginalText()
    {
        if(_activeId is {} id&&_documents.TryGetValue(id,out var state)&&!ReferenceEquals(state.OriginalLine,SelectedEvent))
        {state.OriginalLine=SelectedEvent;state.OriginalText=SelectedEvent?.Text??"";}
        OnPropertyChanged(nameof(OriginalText));
    }
    private void RegisterOriginalAndPasteCommands()
    {
        void R(string id,string label,Func<CancellationToken,ValueTask> action,Func<bool> available,Func<bool>? check=null)
        {
            _registry.Register(new(new(id,label,label,"Edit"),(_,token)=>{token.ThrowIfCancellationRequested();return action(token);},context=>(context.DocumentId is null||context.DocumentId==_activeId)&&available(),check is null?null:_=>check()));
            Actions[id]=new RegistryCommand(_registry,id,CurrentContext,hotkeys:()=>HotkeyProfile);
        }
        bool HasLine()=>SelectedEvent is not null&&!HasGesture;
        R("edit/show_original","Show Original",ignoredToken=>
        {
            _settings=_settings with{ShowOriginal=!ShowOriginal};
            // The view changes immediately even if persistence fails. Report
            // that failure centrally without leaving checked state and layout
            // disagreeing or disturbing the editor's pending draft.
            OnPropertyChanged(nameof(ShowOriginal));_registry.NotifyStateChanged();
            _settingsStore.Save(_settings);return ValueTask.CompletedTask;
        },()=>true,()=>ShowOriginal);
        R("edit/revert","Revert to original",ignoredToken=>{FormatText("Revert to original",(_,_,_,_)=>new(OriginalText,0,OriginalText.Length));return ValueTask.CompletedTask;},HasLine);
        R("edit/insert_original","Insert Original",ignoredToken=>
        {
            FormatText("Insert original",(text,start,end,_)=>
            {
                var first=Math.Clamp(Math.Min(start,end),0,text.Length);var last=Math.Clamp(Math.Max(start,end),first,text.Length);
                first=OriginalInsertionBoundary(text,first);last=OriginalInsertionBoundary(text,last);
                return new(text[..first]+OriginalText+text[last..],first,first+OriginalText.Length);
            });return ValueTask.CompletedTask;
        },HasLine);
        R("edit/line/join/next","Join Next (normal)",_=>{JoinAdjacent(1,false);return ValueTask.CompletedTask;},()=>HasLine()&&!HasPendingAudioTiming&&AdjacentJoinLine(1) is not null);
        R("edit/line/join/next/translatormode","Join Next",_=>{JoinAdjacent(1,true);return ValueTask.CompletedTask;},()=>HasLine()&&!HasPendingAudioTiming&&AdjacentJoinLine(1) is not null);
        R("edit/line/join/last","Join Last",_=>{JoinAdjacent(-1,false);return ValueTask.CompletedTask;},()=>HasLine()&&!HasPendingAudioTiming&&AdjacentJoinLine(-1) is not null);
        R("edit/line/paste/over","Paste Lines Over…",PasteOverAsync,()=>HasLine()&&!_pasteOverBusy&&SelectedEvents.Any(Events.Contains)&&Dialogs is not null);
    }
    private AssEvent? AdjacentJoinLine(int direction)
    {
        if(SelectedEvent is not {} active||MangetsuMetadata.Kind(active)!=MangetsuMetadataKind.None||MangetsuMetadata.IsInherited(active))return null;
        var index=Events.ToList().IndexOf(active);
        if(index<0)return null;
        for(var i=index+direction;i>=0&&i<Events.Count;i+=direction)
        {
            var line=Events[i];
            if(MangetsuMetadata.Kind(line)==MangetsuMetadataKind.None&&!MangetsuMetadata.IsInherited(line))return line;
        }
        return null;
    }
    private static string JoinedText(string first,string second)
    {
        first=first.Trim();second=second.Trim();return first.Length==0?second:second.Length==0?first:first+" "+second;
    }
    private void JoinAdjacent(int direction,bool translator)
    {
        if(!CommitDraft()||HasGesture||HasPendingAudioTiming||SelectedEvent is not {} active||AdjacentJoinLine(direction) is not {} neighbor||
            _activeId is not {} id||!_documents.TryGetValue(id,out var state))return;
        if(active.StartMilliseconds is not {} start||active.EndMilliseconds is not {} end||neighbor.StartMilliseconds is not {} otherStart||neighbor.EndMilliseconds is not {} otherEnd||
            start<0||end<start||otherStart<0||otherEnd<otherStart)return;
        var before=state.OriginalText;
        var current=active.Text;var other=neighbor.Text;
        var joined=direction>0?JoinedText(current,other):JoinedText(other,current);
        var original=direction>0?JoinedText(string.IsNullOrEmpty(before)?current:before,other):joined;
        void Snapshot(string value)
        {
            if(!ReferenceEquals(state.OriginalLine,active))return;
            state.OriginalText=value;
            if(_activeId==id)OnPropertyChanged(nameof(OriginalText));
        }
        RunEdit(editor=>
        {
            using var transaction=editor.Undo.BeginTransaction(direction>0?"Join next":"Join last");
            editor.SetTiming(active,Math.Min(start,otherStart),Math.Max(end,otherEnd));
            if(!translator)editor.SetField(active,"Text",joined,"Join lines");
            editor.Delete([neighbor]);
            editor.Undo.Execute(new DelegateUndoOperation("Join original snapshot",()=>Snapshot(before),()=>Snapshot(original)));
            transaction.Commit();
            Select(active);
        });
    }
    private static int OriginalInsertionBoundary(string text,int index)
    {
        if(index==text.Length)return index;
        return System.Globalization.StringInfo.ParseCombiningCharacters(text).LastOrDefault(p=>p<=index);
    }
    private async ValueTask PasteOverAsync(CancellationToken token)
    {
        if(Dialogs is null||_activeId is not {} id||!_documents.TryGetValue(id,out var state)||Draft is not {} draft||SelectedEvent is not {} active)return;
        var editor=state.Editor;var revision=editor.Document.Revision;var selection=SelectedEvents.Where(Events.Contains).ToArray();
        var values=draft.Values.ToDictionary(p=>p.Key,p=>p.Value);
        bool Current()=>!_disposed&&_activeId==id&&ReferenceEquals(ActiveEditor,editor)&&ReferenceEquals(SelectedEvent,active)&&editor.Document.Revision==revision&&ReferenceEquals(Draft,draft)&&selection.SequenceEqual(SelectedEvents)&&values.All(p=>draft.Values.GetValueOrDefault(p.Key)==p.Value);
        _pasteOverBusy=true;state.MediaDraftPauses++;_editBurstDelay?.Cancel();_editBurstDelay?.Dispose();_editBurstDelay=null;_registry.NotifyStateChanged();
        try
        {
            var text=await Dialogs.ReadClipboardAsync();token.ThrowIfCancellationRequested();if(string.IsNullOrEmpty(text)||!Current())return;
            var clipboard=await Task.Run(()=>SubtitlePasteClipboard.Parse(text,token),token);if(clipboard.Count==0||!Current())return;
            var fields=await Dialogs.ShowPasteOverAsync(PasteOverPreferences,token);token.ThrowIfCancellationRequested();if(fields is null||!Current())return;
            _publishingPasteOver=true;
            try{editor.PasteOver(clipboard,selection,active,fields.Value,values,token);}
            finally{_publishingPasteOver=false;}
            if(fields!=PasteOverFields.None&&(selection.Length==1||selection.Contains(active)))ReloadDraft();
            _settings=_settings with{PasteOverFields=fields};_settingsStore.Save(_settings);
        }
        finally
        {
            _pasteOverBusy=false;state.MediaDraftPauses--;_registry.NotifyStateChanged();
            if(!_disposed&&_activeId==id&&ReferenceEquals(Draft,draft)&&draft.IsChanged&&state.MediaDraftPauses==0)
            {var delay=new CancellationTokenSource();_editBurstDelay=delay;_=FinalizeBurstAsync(delay.Token);}
        }
    }
}
