using Yoake.Core.Commands;
using Yoake.Core.Media;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed record RecentTimecodeItem(string Path,RegistryCommand Command);

public sealed partial class MainWindowViewModel
{
    public string? ExternalTimecodePath=>_activeId is {} id&&_documents.TryGetValue(id,out var state)?state.TimecodePath:null;
    public bool HasTimecodeOperation=>_activeId is {} id&&_documents.TryGetValue(id,out var state)&&state.TimecodeLoading is not null;
    public IReadOnlyList<RecentTimecodeItem> RecentTimecodeItems=>(_settings.RecentTimecodes??[]).Select(path=>new RecentTimecodeItem(path,
        new RegistryCommand(_registry,"timecode/open",CurrentContext,path,hotkeys:()=>HotkeyProfile))).ToArray();
    private void RegisterTimecodeCommands()
    {
        bool Applies(CommandContext context)=>_activeId is {} id&&(context.DocumentId is null||context.DocumentId==id)&&_gesture is null&&_documents[id].Loading is null;
        void Register(string id,string label,Func<CommandInvocation,CancellationToken,ValueTask> execute,Func<bool>? enabled=null)
        {
            _registry.Register(new(new(id,label,label,"Video"),execute,context=>Applies(context)&&(enabled?.Invoke()??true)));
            Actions[id]=new RegistryCommand(_registry,id,CurrentContext,hotkeys:()=>HotkeyProfile);
        }
        Register("timecode/open","Open timecodes…",async(invocation,token)=>
        {
            var id=_activeId!.Value;var state=_documents[id];
            var path=invocation.Parameter as string??(Dialogs is null?null:await Dialogs.OpenTimecodesAsync());
            if(path is null||_activeId!=id||!_documents.TryGetValue(id,out var live)||!ReferenceEquals(live,state))return;
            state.TimecodeLoading?.Cancel();var cancellation=CancellationTokenSource.CreateLinkedTokenSource(token);state.TimecodeLoading=cancellation;NotifyTimecodes();
            try
            {
                var full=Path.GetFullPath(path);var media=state.Media?.VideoProvider;ExternalTimecodes? loaded=null;VideoTimecodeMap? prepared=null;
                var job=_jobs.Run("Open timecodes",ct=>
                {loaded=TimecodeFile.Load(full,ct);if(media?.HasVideo==true)prepared=loaded.ForVideoMap(media.Info.FrameCount,ct);return Task.CompletedTask;},cancellation.Token);
                await job.Completion;cancellation.Token.ThrowIfCancellationRequested();
                if(_disposed||!_documents.TryGetValue(id,out live)||!ReferenceEquals(live,state)||!ReferenceEquals(media,state.Media?.VideoProvider))return;
                state.Editing?.Cancel();if(media?.HasVideo==true)media.SetExternalTimecodes(prepared);
                state.ExternalTimecodes=loaded;state.FrameTiming=loaded!.Timing;state.TimecodePath=full;RememberTimecodes(full);
                if(_activeId==id){NotifyTimecodes();MediaStatus=$"Timecodes: {Path.GetFileName(full)} ({loaded.Milliseconds.Count} timestamps)";ScheduleAutomationValidation();await RefreshVideoFrameAsync(CurrentTimeSeconds);}
            }
            catch(OperationCanceledException)when(cancellation.IsCancellationRequested){if(token.IsCancellationRequested)throw;}
            catch{ForgetTimecodes(Path.GetFullPath(path));throw;}
            finally
            {if(ReferenceEquals(state.TimecodeLoading,cancellation))state.TimecodeLoading=null;cancellation.Dispose();if(_activeId==id)NotifyTimecodes();}
        });
        Register("timecode/close","Close external timecodes",async(_,_)=>
        {
            var state=_documents[_activeId!.Value];state.TimecodeLoading?.Cancel();state.TimecodeLoading=null;state.Editing?.Cancel();
            state.Media?.SetExternalTimecodes(null);state.ExternalTimecodes=null;state.TimecodePath=null;state.FrameTiming=state.ProviderFrameTiming;
            NotifyTimecodes();MediaStatus="Using video timecodes.";ScheduleAutomationValidation();await RefreshVideoFrameAsync(CurrentTimeSeconds);
        },()=>ExternalTimecodePath is not null||HasTimecodeOperation);
        Register("timecode/save","Save timecodes…",async(invocation,token)=>
        {
            var id=_activeId!.Value;var state=_documents[id];var external=state.ExternalTimecodes;var media=state.Media;
            var times=media?.FrameTimes;var count=media?.HasVideo==true?media.Info.FrameCount:0;var fps=media?.Info.FramesPerSecond??0;
            var path=invocation.Parameter as string??(Dialogs is null?null:await Dialogs.SaveTimecodesAsync(Path.GetFileNameWithoutExtension(SuggestedSubtitleFileName)+".tc.txt"));
            if(path is null||_disposed||!_documents.TryGetValue(id,out var live)||!ReferenceEquals(live,state))return;
            state.TimecodeLoading?.Cancel();var cancellation=CancellationTokenSource.CreateLinkedTokenSource(token);state.TimecodeLoading=cancellation;NotifyTimecodes();
            try
            {
                var full=Path.GetFullPath(path);var job=_jobs.Run("Save timecodes",ct=>
                {var data=external??TimecodeFile.FromVideo(times!,fps,ct);TimecodeFile.Save(full,data,count,ct);return Task.CompletedTask;},cancellation.Token);
                await job.Completion;cancellation.Token.ThrowIfCancellationRequested();RememberTimecodes(full);if(_activeId==id)MediaStatus=$"Saved timecodes: {Path.GetFileName(full)}";
            }
            catch(OperationCanceledException)when(cancellation.IsCancellationRequested){if(token.IsCancellationRequested)throw;}
            finally{if(ReferenceEquals(state.TimecodeLoading,cancellation))state.TimecodeLoading=null;cancellation.Dispose();if(_activeId==id)NotifyTimecodes();}
        },()=>CanShiftByFrames&&!HasTimecodeOperation);
    }
    private void NotifyTimecodes()
    {
        MediaDurationSeconds=_media?.Info.DurationSeconds??0;
        foreach(var name in new[]{nameof(FrameTimes),nameof(FramePositionDisplay),nameof(RelativeTimingDisplay),nameof(ExternalTimecodePath),nameof(HasTimecodeOperation),nameof(CanShiftByFrames)})OnPropertyChanged(name);
        _registry.NotifyStateChanged();
    }
    private void RememberTimecodes(string path)
    {
        _settings=_settings with{RecentTimecodes=new[]{path}.Concat((_settings.RecentTimecodes??[]).Where(item=>!string.Equals(item,path,StringComparison.OrdinalIgnoreCase))).Take(12).ToArray()};
        _settingsStore.Save(_settings);OnPropertyChanged(nameof(RecentTimecodeItems));
    }
    private void ForgetTimecodes(string path)
    {
        _settings=_settings with{RecentTimecodes=(_settings.RecentTimecodes??[]).Where(item=>!string.Equals(item,path,StringComparison.OrdinalIgnoreCase)).ToArray()};
        _settingsStore.Save(_settings);OnPropertyChanged(nameof(RecentTimecodeItems));
    }
}
