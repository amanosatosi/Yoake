using Yoake.Core.Commands;
using Yoake.Core.Media;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed record RecentKeyframeItem(string Path,RegistryCommand Command);

public sealed partial class MainWindowViewModel
{
    public string? ExternalKeyframePath=>_activeId is {} id&&_documents.TryGetValue(id,out var state)?state.KeyframePath:null;
    public bool HasKeyframeOperation=>_activeId is {} id&&_documents.TryGetValue(id,out var state)&&state.KeyframeLoading is not null;
    public IReadOnlyList<RecentKeyframeItem> RecentKeyframeItems=>( _settings.RecentKeyframes??[]).Select(path=>new RecentKeyframeItem(path,
        new RegistryCommand(_registry,"keyframe/open",CurrentContext,path,hotkeys:()=>HotkeyProfile))).ToArray();
    private void RegisterKeyframeCommands()
    {
        bool Applies(CommandContext context)=>_activeId is not null&&(context.DocumentId is null||context.DocumentId==_activeId)&&_gesture is null;
        void Register(string id,string label,Func<CommandInvocation,CancellationToken,ValueTask> execute,Func<bool>? enabled=null)
        {
            _registry.Register(new(new(id,label,label,"Video"),execute,context=>Applies(context)&&(enabled?.Invoke()??true)));
            Actions[id]=new RegistryCommand(_registry,id,CurrentContext,hotkeys:()=>HotkeyProfile);
        }
        Register("keyframe/open","Open keyframes…",async(invocation,token)=>
        {
            var id=_activeId!.Value;var state=_documents[id];
            var path=invocation.Parameter as string??(Dialogs is null?null:await Dialogs.OpenKeyframesAsync());
            if(path is null||_activeId!=id||!_documents.TryGetValue(id,out var live)||!ReferenceEquals(live,state))return;
            state.KeyframeLoading?.Cancel();var cancellation=CancellationTokenSource.CreateLinkedTokenSource(token);state.KeyframeLoading=cancellation;
            NotifyKeyframes();
            try
            {
                var full=Path.GetFullPath(path);IReadOnlyList<int>? loaded=null;
                var job=_jobs.Run("Open keyframes",ct=>{loaded=KeyframeFile.Load(full,ct);return Task.CompletedTask;},cancellation.Token);
                await job.Completion;cancellation.Token.ThrowIfCancellationRequested();
                if(_disposed||!_documents.TryGetValue(id,out live)||!ReferenceEquals(live,state))return;
                state.Media?.SetExternalKeyframes(loaded);state.ExternalKeyframes=loaded;state.KeyframePath=full;
                RememberKeyframes(full);
                if(_activeId==id){NotifyKeyframes();MediaStatus=$"Keyframes: {Path.GetFileName(full)} ({loaded!.Count} cuts)";ScheduleAutomationValidation();}
            }
            catch(OperationCanceledException)when(cancellation.IsCancellationRequested){if(token.IsCancellationRequested)throw;}
            catch
            {
                // Remove a missing/malformed recent entry, retaining the
                // previously loaded map, provider, position and draft.
                ForgetKeyframes(Path.GetFullPath(path));throw;
            }
            finally
            {
                if(ReferenceEquals(state.KeyframeLoading,cancellation))state.KeyframeLoading=null;cancellation.Dispose();
                if(_activeId==id)NotifyKeyframes();
            }
        });
        Register("keyframe/close","Close external keyframes",(_,_)=>
        {
            var state=_documents[_activeId!.Value];state.KeyframeLoading?.Cancel();state.KeyframeLoading=null;state.ExternalKeyframes=null;state.KeyframePath=null;state.Media?.SetExternalKeyframes(null);
            NotifyKeyframes();MediaStatus="Using video keyframes.";ScheduleAutomationValidation();return ValueTask.CompletedTask;
        },()=>ExternalKeyframePath is not null||HasKeyframeOperation);
        Register("keyframe/save","Save keyframes…",async(invocation,token)=>
        {
            var id=_activeId!.Value;var state=_documents[id];var keys=(state.ExternalKeyframes??Keyframes).ToArray();
            var path=invocation.Parameter as string??(Dialogs is null?null:await Dialogs.SaveKeyframesAsync(Path.GetFileNameWithoutExtension(SuggestedSubtitleFileName)+".key.txt"));
            if(path is null)return;
            if(_disposed||!_documents.TryGetValue(id,out var live)||!ReferenceEquals(live,state))return;
            var cancellation=CancellationTokenSource.CreateLinkedTokenSource(token);state.KeyframeLoading=cancellation;NotifyKeyframes();
            try
            {
                var full=Path.GetFullPath(path);var job=_jobs.Run("Save keyframes",ct=>{KeyframeFile.Save(full,keys,ct);return Task.CompletedTask;},cancellation.Token);await job.Completion;
                cancellation.Token.ThrowIfCancellationRequested();RememberKeyframes(full);if(_activeId==id)MediaStatus=$"Saved keyframes: {Path.GetFileName(full)}";
            }
            catch(OperationCanceledException)when(cancellation.IsCancellationRequested){if(token.IsCancellationRequested)throw;}
            finally
            {
                if(ReferenceEquals(state.KeyframeLoading,cancellation))state.KeyframeLoading=null;cancellation.Dispose();if(_activeId==id)NotifyKeyframes();
            }
        },()=>Keyframes.Count>0&&!HasKeyframeOperation);
    }
    private void NotifyKeyframes(){OnPropertyChanged(nameof(Keyframes));OnPropertyChanged(nameof(ExternalKeyframePath));OnPropertyChanged(nameof(HasKeyframeOperation));OnPropertyChanged(nameof(FramePositionDisplay));_registry.NotifyStateChanged();}
    private void RememberKeyframes(string path)
    {
        _settings=_settings with{RecentKeyframes=new[]{path}.Concat((_settings.RecentKeyframes??[]).Where(item=>!string.Equals(item,path,StringComparison.OrdinalIgnoreCase))).Take(12).ToArray()};
        _settingsStore.Save(_settings);OnPropertyChanged(nameof(RecentKeyframeItems));
    }
    private void ForgetKeyframes(string path)
    {
        _settings=_settings with{RecentKeyframes=(_settings.RecentKeyframes??[]).Where(item=>!string.Equals(item,path,StringComparison.OrdinalIgnoreCase)).ToArray()};
        _settingsStore.Save(_settings);OnPropertyChanged(nameof(RecentKeyframeItems));
    }
}
