using Avalonia.Threading;
using Yoake.Core.Commands;
using Yoake.Core.Fonts;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed record FontCollectorRequest(FontCollectionMode Mode,string Destination,bool IncludeUnusedStyles=false,bool RightsAcknowledged=false);
public sealed class FontCollectorCloseEventArgs(Guid document):EventArgs{public Guid Document {get;}=document;}
public sealed partial class MainWindowViewModel
{
    private bool _fontDialogOpen;
    public IFontResolverFactory FontResolvers {get;set;}=new Yoake.Native.WindowsFontResolverFactory();
    public bool FontCollectorBusy=>_activeId is {} id&&_documents.TryGetValue(id,out var state)&&state.FontWork is not null;
    public FontCollectionReport? FontReport=>_activeId is {} id&&_documents.TryGetValue(id,out var state)?state.FontReport:null;
    public string FontCollectorStatus=>_activeId is {} id&&_documents.TryGetValue(id,out var state)?state.FontStatus:"";
    public string FontCollectorLog=>_activeId is {} id&&_documents.TryGetValue(id,out var state)?string.Join("\n\n",state.FontLog):"";
    public double FontCollectorProgress=>_activeId is {} id&&_documents.TryGetValue(id,out var state)?state.FontProgress:0;
    public FontCollectionOutput? FontOutput=>_activeId is {} id&&_documents.TryGetValue(id,out var state)?state.FontOutput:null;
    public event EventHandler<FontCollectorCloseEventArgs>? FontCollectorCloseRequested;
    private void RegisterFontCollectorCommands()
    {
        bool Owner(CommandContext context)=>!_disposed&&_activeId is {} id&&_documents.ContainsKey(id)&&(context.DocumentId is null||context.DocumentId==id)&&!HasGesture;
        void Register(string id,string label,Func<CommandInvocation,CancellationToken,ValueTask> action,Func<bool>? available=null)
        {
            _registry.Register(new(new(id,label,label,"Tools"),action,c=>Owner(c)&&(available?.Invoke()??true)));
            Actions[id]=new RegistryCommand(_registry,id,CurrentContext,hotkeys:()=>HotkeyProfile);
        }
        Register("tool/font_collector","Fonts Collector…",async(invocation,token)=>
        {
            if(_activeId is not {} id||Dialogs is null)return;var state=_documents[id];_fontDialogOpen=true;state.MediaDraftPauses++;_editBurstDelay?.Cancel();_registry.NotifyStateChanged();
            try{await Dialogs.ShowFontCollectorAsync(this,id,token);}
            finally
            {
                CancelFontWork(id);state.MediaDraftPauses--;_fontDialogOpen=false;
                if(!_disposed&&_activeId==id&&Draft?.IsChanged==true&&!state.KaraokeEnabled)
                {_editBurstDelay?.Cancel();_editBurstDelay?.Dispose();_editBurstDelay=new();_=FinalizeBurstAsync(_editBurstDelay.Token);}
                _registry.NotifyStateChanged();
            }
        },()=>!_fontDialogOpen&&!FontCollectorBusy);
        Register("fonts/start","Check / collect fonts",async(invocation,token)=>
        {
            if(invocation.Parameter is not FontCollectorRequest request||!Enum.IsDefined(request.Mode))throw new ArgumentException("Choose a font collection action.");
            var id=_activeId!.Value;var state=_documents[id];var revision=state.Editor.Document.Revision;var draftRevision=state.DraftRevision;var path=ActiveSubtitlePath;
            var destination=request.Destination;
            if(request.Mode==FontCollectionMode.ScriptFolder)destination=path is not null?Path.GetDirectoryName(path)!:throw new InvalidOperationException("Save the subtitle first or choose a destination folder.");
            if(request.Mode!=FontCollectionMode.Check&&string.IsNullOrWhiteSpace(destination))throw new ArgumentException("Choose a destination parent folder or ZIP filename.");
            if(request.Mode!=FontCollectionMode.Check&&!request.RightsAcknowledged)throw new InvalidOperationException("Confirm font redistribution permission before copying.");
            var snapshot=FontUsageSnapshot.Capture(state.Editor.Document,SelectedEvent,Draft?.Values);var factory=FontResolvers;
            var cached=state.FontReportRevision==revision&&state.FontReportDraftRevision==draftRevision&&state.FontReportUnused==request.IncludeUnusedStyles?state.FontReport:null;
            using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(token);state.FontWork=cancellation;var generation=state.FontLease.Renew();
            state.FontOutput=null;state.FontLog.Clear();state.FontProgress=0;state.FontStatus="Scanning required fonts…";NotifyFonts();
            IProgress<FontProgress> progress=new Progress<FontProgress>(update=>
            {
                void Publish(){if(!_disposed&&_activeId==id&&ReferenceEquals(state.FontWork,cancellation)&&state.FontLease.IsCurrent(generation)){state.FontLog.Add(update.Level+": "+update.Message);state.FontProgress=update.Fraction;NotifyFonts();}}
                if(Dispatcher.UIThread.CheckAccess())Publish();else Dispatcher.UIThread.Post(Publish);
            });
            try
            {
                FontCollectionReport? report=null;FontCollectionOutput? output=null;
                var job=_jobs.Run("Collect subtitle fonts",async ct=>
                {
                    report=cached??FontCollection.Scan(snapshot,request.IncludeUnusedStyles,factory,progress,ct);
                    if(cached is not null)progress.Report(new(0,FontLogLevel.Information,"Reusing the unchanged document/draft font scan.\n"+cached.Summary));
                    if(request.Mode!=FontCollectionMode.Check)output=await FontCollection.WriteAsync(report,request.Mode,destination,request.RightsAcknowledged,state.FontLease,generation,progress,ct).ConfigureAwait(false);
                },cancellation.Token);
                await job.Completion;cancellation.Token.ThrowIfCancellationRequested();
                if(_disposed||_activeId!=id||!_documents.TryGetValue(id,out var live)||!ReferenceEquals(live,state)||state.Editor.Document.Revision!=revision||state.DraftRevision!=draftRevision||ActiveSubtitlePath!=path)throw new OperationCanceledException("The font collection document changed.");
                state.FontReport=report;state.FontReportRevision=revision;state.FontReportDraftRevision=draftRevision;state.FontReportUnused=request.IncludeUnusedStyles;state.FontOutput=output;
                state.FontStatus=output is null?report!.Summary:$"Published {output.Files} font files: {output.Path}. "+report!.Summary;
                state.FontLog.Add(state.FontStatus);state.FontProgress=1;
            }
            catch(OperationCanceledException)when(cancellation.IsCancellationRequested)
            {if(token.IsCancellationRequested)throw;state.FontStatus="Font collection canceled; obsolete work cannot publish.";state.FontLog.Add(state.FontStatus);}
            catch(Exception error){state.FontStatus="Font collection failed: "+error.Message;state.FontLog.Add(state.FontStatus);throw;}
            finally{if(ReferenceEquals(state.FontWork,cancellation))state.FontWork=null;NotifyFonts();}
        },()=>!FontCollectorBusy&&!HasEditOperation);
        Register("fonts/cancel","Cancel font collection",(invocation,token)=>{if(_activeId is {} id)CancelFontWork(id);return ValueTask.CompletedTask;},()=>FontCollectorBusy);
        _registry.Register(new(new("fonts/close","Close Fonts Collector","Cancel pending work and close this document's collector","Tools"),(invocation,token)=>
        {if((invocation.Context.DocumentId??_activeId) is {} id){CancelFontWork(id);FontCollectorCloseRequested?.Invoke(this,new(id));}return ValueTask.CompletedTask;}));
    }
    private void CancelFontWork(Guid id)
    {if(_documents.TryGetValue(id,out var state)){state.FontLease.Invalidate();state.FontWork?.Cancel();}}
    private void NotifyFonts()
    {foreach(var name in new[]{nameof(FontCollectorBusy),nameof(FontReport),nameof(FontCollectorStatus),nameof(FontCollectorLog),nameof(FontCollectorProgress),nameof(FontOutput)})OnPropertyChanged(name);_registry.NotifyStateChanged();}
}
