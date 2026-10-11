using Avalonia.Controls;
using Yoake.Core.Commands;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _exitInProgress;
    public event EventHandler? WindowCloseRequested;
    public bool ToolbarVisible=>_settings.ToolbarVisible??true;
    public GridLength ToolbarRowHeight=>new(ToolbarVisible?54:24);
    public string ApplicationLogPath=>Path.Combine(Path.GetDirectoryName(_settingsStore.Path)!,"logs","yoake.log");

    private void RegisterApplicationCommands()
    {
        void Register(string id,string label,Func<ValueTask> action,Func<bool>? enabled=null,Func<bool>? check=null)
        {
            _registry.Register(new(new(id,label,label,"Application"),(_,_)=>action(),_=>enabled?.Invoke()??true,check is null?null:_=>check()));
            Actions[id]=new RegistryCommand(_registry,id,CurrentContext,hotkeys:()=>HotkeyProfile);
        }
        Register(CommandIds.AppExit,"Exit",async ()=>
        {
            _exitInProgress=true;_registry.NotifyStateChanged();
            try{if(await CloseWindowAsync())WindowCloseRequested?.Invoke(this,EventArgs.Empty);}
            finally{_exitInProgress=false;}
        },()=>!_exitInProgress);
        Register(CommandIds.AppToggleToolbar,"Toolbar",()=>
        {
            _settings=_settings with{ToolbarVisible=!ToolbarVisible};_settingsStore.Save(_settings);
            OnPropertyChanged(nameof(ToolbarVisible));OnPropertyChanged(nameof(ToolbarRowHeight));return ValueTask.CompletedTask;
        },check:()=>ToolbarVisible);
        Register(CommandIds.AppLog,"Log window…",async ()=>
        {
            if(Dialogs is not null)await Dialogs.ShowLogAsync(ApplicationLogPath);
        });
    }
}
