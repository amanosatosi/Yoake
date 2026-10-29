using Yoake.Core.Tools;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _assDrawLaunching;
    public string? AssDrawExecutable=>_settings.AssDrawExecutable;
    public string AssDrawApplicationDirectory {get;set;}=AppContext.BaseDirectory;
    public IExternalToolLauncher AssDrawProcess {get;set;}=new ExternalToolLauncher();
    private void RegisterAssDrawCommands()
    {
        _registry.Register(new(new("tool/assdraw","ASSDraw3…","Launch the external ASSDraw3 vector drawing application","Tools"),async(invocation,token)=>
        {
            _assDrawLaunching=true;_registry.NotifyStateChanged();
            try
            {
                var configured=AssDrawExecutable;var directory=AssDrawApplicationDirectory;var launcher=AssDrawProcess;AssDrawLocation? location=null;
                var job=_jobs.Run("Launch ASSDraw3",ct=>
                {ct.ThrowIfCancellationRequested();location=AssDrawLauncher.Discover(configured,directory);ct.ThrowIfCancellationRequested();if(location is not null)launcher.Launch(location.Path);return Task.CompletedTask;},token);
                await job.Completion;token.ThrowIfCancellationRequested();if(_disposed)return;
                if(location is null)
                {
                    SubtitleStatus=AssDrawLauncher.MissingMessage;
                    if(Dialogs is not null&&await Dialogs.ShowAssDrawMissingAsync(token))await Dialogs.ShowAssDrawOptionsAsync(this);
                }
                else SubtitleStatus="Launched ASSDraw3: "+location.Path;
            }
            catch(Exception error)when(error is not OperationCanceledException)
            {throw new InvalidOperationException("Could not launch ASSDraw3: "+error.Message,error);}
            finally{_assDrawLaunching=false;_registry.NotifyStateChanged();}
        },context=>!_disposed&&!_assDrawLaunching&&!HasGesture));
        Actions["tool/assdraw"]=new RegistryCommand(_registry,"tool/assdraw",CurrentContext,hotkeys:()=>HotkeyProfile);
        _registry.Register(new(new("settings/assdraw/path","Configure ASSDraw3 executable","Choose an existing explicitly configured executable","Settings"),async(invocation,token)=>
        {
            var text=(invocation.Parameter as string)?.Trim();
            if(!string.IsNullOrEmpty(text))
            {
                var valid=false;var job=_jobs.Run("Check ASSDraw3 configuration",ct=>{ct.ThrowIfCancellationRequested();valid=AssDrawLauncher.IsExecutablePath(text);return Task.CompletedTask;},token);await job.Completion;token.ThrowIfCancellationRequested();
                if(!valid)throw new ArgumentException("Choose an existing .exe using its absolute path, or clear the field for bundled discovery.");text=Path.GetFullPath(text);
            }
            _settings=_settings with{AssDrawExecutable=string.IsNullOrEmpty(text)?null:text};_settingsStore.Save(_settings);OnPropertyChanged(nameof(AssDrawExecutable));SubtitleStatus="ASSDraw3 executable configuration saved.";
        },context=>!_disposed));
    }
}
