using Yoake.Core.Audio;
using Yoake.Core.Commands;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    public bool IsAudioSpectrum=>_settings.AudioSpectrum??false;
    public bool AudioAutoScroll=>_settings.AudioAutoScroll??true;
    public int AudioDisplayModeIndex
    {
        get=>IsAudioSpectrum?1:0;
        set{if((value is 0 or 1)&&value!=AudioDisplayModeIndex)Actions[value==1?"audio/view/spectrum":"audio/view/waveform"].Execute(null);}
    }
    private double _audioViewportWidth=640;
    public void ReportAudioViewportWidth(double width){if(double.IsFinite(width)&&width>1)_audioViewportWidth=width;}
    private void RegisterAudioPresentationCommands()
    {
        bool Applies(CommandContext context)=>_activeId is not null&&(context.DocumentId is null||context.DocumentId==_activeId);
        void Register(string id,string label,Action action,Func<bool>? available=null,Func<bool>? check=null)
        {
            _registry.Register(new(new(id,label,label,"Audio"),(_,_)=>{action();return ValueTask.CompletedTask;},context=>Applies(context)&&(available?.Invoke()??true),check is null?null:_=>check()));
            Actions[id]=new RegistryCommand(_registry,id,CurrentContext,hotkeys:()=>HotkeyProfile);
        }
        void SetSpectrum(bool value)
        {
            _settings=_settings with{AudioSpectrum=value};_settingsStore.Save(_settings);
            OnPropertyChanged(nameof(IsAudioSpectrum));OnPropertyChanged(nameof(AudioDisplayModeIndex));
        }
        Register("audio/view/spectrum","Spectrum",()=>SetSpectrum(true),check:()=>IsAudioSpectrum);
        Register("audio/view/waveform","Waveform",()=>SetSpectrum(false),check:()=>!IsAudioSpectrum);
        Register("audio/opt/spectrum","Toggle spectrum",()=>SetSpectrum(!IsAudioSpectrum),check:()=>IsAudioSpectrum);
        Register("audio/opt/autoscroll","Autoscroll to selected line",()=>
        {_settings=_settings with{AudioAutoScroll=!AudioAutoScroll};_settingsStore.Save(_settings);OnPropertyChanged(nameof(AudioAutoScroll));},check:()=>AudioAutoScroll);
        Register("audio/opt/vertical_link","Link amplitude and volume",()=>SaveAudioSetting("audio/volume/link",!AudioVolumeLinked),check:()=>AudioVolumeLinked);
        bool Audio()=>_media?.HasAudio==true;
        bool Line()=>Audio()&&SelectedEvent?.StartMilliseconds is {} begin&&SelectedEvent.EndMilliseconds is {} end&&begin>=0&&end>=begin;
        void Move(double start)
        {var viewport=AudioViewport;RememberAudioViewport(start,viewport.Span);OnPropertyChanged(nameof(AudioViewport));}
        foreach(var (id,label,distance) in new[]{("audio/scroll/left","Scroll left",-128d),("audio/scroll/right","Scroll right",128d)})
            Register(id,label,()=>{var viewport=AudioViewport;Move(AudioViewportMath.ScrollPixels(viewport.Start,viewport.Span,_audioViewportWidth,distance,MediaDurationSeconds));},Audio);
        Register("audio/go_to","Go to audio selection",()=>
        {var viewport=AudioViewport;Move(AudioViewportMath.RangeInView(viewport.Start,viewport.Span,SelectedEvent!.StartMilliseconds!.Value/1000d,SelectedEvent.EndMilliseconds!.Value/1000d,MediaDurationSeconds));},Line);
        Register("audio/go_to/start","Center selection start",()=>Move(AudioViewportMath.Center(SelectedEvent!.StartMilliseconds!.Value/1000d,AudioViewport.Span,MediaDurationSeconds)),Line);
        Register("audio/go_to/end","Center selection end",()=>Move(AudioViewportMath.Center(SelectedEvent!.EndMilliseconds!.Value/1000d,AudioViewport.Span,MediaDurationSeconds)),Line);
    }
}
