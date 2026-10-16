using Yoake.Core.Commands;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    public bool VideoAutoSeek=>_settings.VideoAutoSeek??true;
    private void RegisterVideoOptions()
    {
        const string id="video/autoseek";
        _registry.Register(new(new(id,"Video autoseek","Seek loaded video to the selected line's start","Video"),(_,_)=>
        {
            _settings=_settings with{VideoAutoSeek=!VideoAutoSeek};OnPropertyChanged(nameof(VideoAutoSeek));_settingsStore.Save(_settings);return ValueTask.CompletedTask;
        },isChecked:_=>VideoAutoSeek));
        Actions[id]=new RegistryCommand(_registry,id,CurrentContext,hotkeys:()=>HotkeyProfile);
    }
}
