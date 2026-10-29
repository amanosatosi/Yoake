using Yoake.Core.Commands;
using Yoake.Core.Hotkeys;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed record HotkeyChange(HotkeyContext Context, string CommandId, string[] Gestures);
public sealed record HotkeyCommandReset(HotkeyContext Context, string CommandId);

public sealed partial class MainWindowViewModel
{
    public bool GlobalTimingHotkeys => _settings.GlobalTimingHotkeys;
    private void InitializeHotkeyCommands()
    {
        HotkeyProfile = new(AegisubHotkeyDefaults.ForYoake(), _settings.HotkeyOverrides);
        HotkeyProfile.Changed += (_, _) =>
        {
            _settings = _settings with { HotkeyOverrides=HotkeyProfile.Overrides };
            _settingsStore.Save(_settings); _registry.NotifyStateChanged();
            OnPropertyChanged(nameof(AutomationMacros));
        };
        void Register(string id, string label, Func<CommandInvocation,ValueTask> action, Func<bool>? check=null)
        {
            _registry.Register(new(new(id,label,label,"Settings"),(invocation,_)=>action(invocation),isChecked:check is null?null:_=>check()));
            Actions[id]=new RegistryCommand(_registry,id,CurrentContext,hotkeys:()=>HotkeyProfile);
        }
        Register("app/options","Options…",async _=>
        {
            if(Dialogs is not null)await Dialogs.ShowOptionsAsync(this);
        });
        Register("settings/hotkeys/set","Change hotkeys",invocation=>
        {
            if(invocation.Parameter is not HotkeyChange change)throw new ArgumentException("Choose a command, context and bindings.");
            HotkeyProfile.Set(change.Context,change.CommandId,change.Gestures);return ValueTask.CompletedTask;
        });
        Register("settings/hotkeys/reset-command","Reset command hotkeys",invocation=>
        {
            if(invocation.Parameter is not HotkeyCommandReset reset)throw new ArgumentException("Choose a command and context.");
            HotkeyProfile.ResetCommand(reset.Context,reset.CommandId);return ValueTask.CompletedTask;
        });
        Register("settings/hotkeys/reset-context","Reset context hotkeys",invocation=>
        {
            if(invocation.Parameter is not HotkeyContext context)throw new ArgumentException("Choose a hotkey context.");
            HotkeyProfile.ResetContext(context);return ValueTask.CompletedTask;
        });
        Register("settings/hotkeys/reset-all","Reset all hotkeys",_=>{HotkeyProfile.ResetAll();return ValueTask.CompletedTask;});
        Register("app/toggle/global_hotkeys","Global timing hotkeys",_=>
        {
            _settings=_settings with {GlobalTimingHotkeys=!GlobalTimingHotkeys};_settingsStore.Save(_settings);
            OnPropertyChanged(nameof(GlobalTimingHotkeys));return ValueTask.CompletedTask;
        },()=>GlobalTimingHotkeys);
    }
}
