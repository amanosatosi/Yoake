using Yoake.Core.Commands;
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
        void R(string id,string label,Func<ValueTask> action,Func<bool> available,Func<bool>? check=null)
        {
            _registry.Register(new(new(id,label,label,"Edit"),(_,_)=>action(),context=>(context.DocumentId is null||context.DocumentId==_activeId)&&available(),check is null?null:_=>check()));
            Actions[id]=new RegistryCommand(_registry,id,CurrentContext,hotkeys:()=>HotkeyProfile);
        }
        bool HasLine()=>SelectedEvent is not null&&!HasGesture;
        R("edit/show_original","Show Original",()=>
        {
            _settings=_settings with{ShowOriginal=!ShowOriginal};
            // The view changes immediately even if persistence fails. Report
            // that failure centrally without leaving checked state and layout
            // disagreeing or disturbing the editor's pending draft.
            OnPropertyChanged(nameof(ShowOriginal));_registry.NotifyStateChanged();
            _settingsStore.Save(_settings);return ValueTask.CompletedTask;
        },()=>true,()=>ShowOriginal);
        R("edit/revert","Revert to original",()=>{FormatText("Revert to original",(_,_,_,_)=>new(OriginalText,0,OriginalText.Length));return ValueTask.CompletedTask;},HasLine);
        R("edit/insert_original","Insert Original",()=>
        {
            FormatText("Insert original",(text,start,end,_)=>
            {
                var first=Math.Clamp(Math.Min(start,end),0,text.Length);var last=Math.Clamp(Math.Max(start,end),first,text.Length);
                first=OriginalInsertionBoundary(text,first);last=OriginalInsertionBoundary(text,last);
                return new(text[..first]+OriginalText+text[last..],first,first+OriginalText.Length);
            });return ValueTask.CompletedTask;
        },HasLine);
        R("edit/line/paste/over","Paste Lines Over…",PasteOverAsync,()=>HasLine()&&!_pasteOverBusy&&SelectedEvents.Any(Events.Contains)&&Dialogs is not null);
    }
    private static int OriginalInsertionBoundary(string text,int index)
    {
        if(index==text.Length)return index;
        return System.Globalization.StringInfo.ParseCombiningCharacters(text).LastOrDefault(p=>p<=index);
    }
    private async ValueTask PasteOverAsync()
    {
        if(Dialogs is null||_activeId is not {} id||!_documents.TryGetValue(id,out var state)||Draft is not {} draft||SelectedEvent is not {} active)return;
        var editor=state.Editor;var revision=editor.Document.Revision;var selection=SelectedEvents.Where(Events.Contains).ToArray();
        var values=draft.Values.ToDictionary(p=>p.Key,p=>p.Value);
        bool Current()=>!_disposed&&_activeId==id&&ReferenceEquals(ActiveEditor,editor)&&ReferenceEquals(SelectedEvent,active)&&editor.Document.Revision==revision&&ReferenceEquals(Draft,draft)&&selection.SequenceEqual(SelectedEvents)&&values.All(p=>draft.Values.GetValueOrDefault(p.Key)==p.Value);
        _pasteOverBusy=true;state.MediaDraftPauses++;_editBurstDelay?.Cancel();_editBurstDelay?.Dispose();_editBurstDelay=null;_registry.NotifyStateChanged();
        try
        {
            var text=await Dialogs.ReadClipboardAsync();if(string.IsNullOrEmpty(text)||!Current())return;
            var clipboard=await Task.Run(()=>SubtitlePasteClipboard.Parse(text));if(clipboard.Count==0||!Current())return;
            var fields=await Dialogs.ShowPasteOverAsync(PasteOverPreferences);if(fields is null||!Current())return;
            _publishingPasteOver=true;
            try{editor.PasteOver(clipboard,selection,active,fields.Value,values);}
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
