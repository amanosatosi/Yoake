using Yoake.Core.Audio;

namespace Yoake.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    public double PlaybackVolume{get=>_settings.PlaybackVolume??0.8;set=>InvokeAudioSetting("audio/volume",value);}
    public bool PlaybackMuted{get=>_settings.PlaybackMuted;set=>InvokeAudioSetting("audio/mute",value);}
    public double AudioDisplayHeight{get=>_settings.AudioDisplayHeight??160;set=>InvokeAudioSetting("audio/display/height",value);}
    public double AudioIntensity{get=>_settings.AudioIntensity??1;set=>InvokeAudioSetting("audio/display/intensity",value);}
    public double AudioHorizontalZoom{get=>AudioViewportMath.Zoom(AudioWindowSeconds);set=>AudioWindowSeconds=AudioViewportMath.Span(value);}
    public double AudioAmplitude{get=>AudioViewportMath.AmplitudePosition(AudioIntensity);set=>AudioIntensity=AudioViewportMath.Amplitude(value);}
    public bool AudioVolumeLinked{get=>_settings.AudioVolumeLinked;set=>InvokeAudioSetting("audio/volume/link",value);}
    public bool IsVolumeIndependent=>!AudioVolumeLinked;
    private void InvokeAudioSetting(string id,object value)
    {
        var result=_registry.InvokeAsync(id,CurrentContext(),value);
        if(!result.IsCompletedSuccessfully)_=ObserveAudioSettingAsync(result);
    }
    private async Task ObserveAudioSettingAsync(ValueTask<bool> result){try{await result;}catch(Exception e){_registry.ReportFailure("audio/settings",e);}}
    private void SaveAudioSetting(string id,object? value)
    {
        _settings=id switch
        {
            "audio/volume" when value is double v=>_settings with{PlaybackVolume=v},
            "audio/mute" when value is bool m=>_settings with{PlaybackMuted=m},
            "audio/display/height" when value is double h=>_settings with{AudioDisplayHeight=h},
            "audio/display/intensity" when value is double a=>_settings with{AudioIntensity=a},_=>_settings
        };
        if(id=="audio/display/zoom"&&value is double span)_settings=_settings with{AudioWindowSeconds=span};
        if(id=="audio/volume/link"&&value is bool linked)_settings=_settings with{AudioVolumeLinked=linked};
        _settings=_settings.Normalize();
        if(id=="audio/display/zoom"&&_activeId is {} active&&_documents.TryGetValue(active,out var state))state.AudioSpan=_settings.AudioWindowSeconds??20;
        if(AudioVolumeLinked)_settings=_settings with{PlaybackVolume=AudioViewportMath.LinkedVolume(AudioIntensity)};
        _audioPlayer?.SetVolume(PlaybackVolume,PlaybackMuted);
        foreach(var name in new[]{nameof(PlaybackVolume),nameof(PlaybackMuted),nameof(AudioDisplayHeight),nameof(AudioIntensity),nameof(AudioHorizontalZoom),nameof(AudioAmplitude),nameof(AudioVolumeLinked),nameof(IsVolumeIndependent)})OnPropertyChanged(name);
        if(id=="audio/display/zoom")AudioZoomChanged?.Invoke(this,EventArgs.Empty);
        _settingsStore.Save(_settings);
    }
    private readonly Dictionary<(object Media,double Start,double Span,int Width),WaveformData> _waveformCache=[];
    public async Task<WaveformData?> CreateWaveformViewportAsync(double start,double span,int width,CancellationToken token)
    {
        var media=_media;if(media?.HasAudio!=true)return null;var key=((object)media,start,span,width);
        if(_waveformCache.TryGetValue(key,out var cached))return cached;
        var result=await RunAnalysisAsync("Waveform detail",ct=>media.BuildWaveformViewport(start,span,width,ct),token);
        token.ThrowIfCancellationRequested();if(!ReferenceEquals(media,_media))return null;
        if(_waveformCache.Count>=8)_waveformCache.Remove(_waveformCache.Keys.First());_waveformCache[key]=result;return result;
    }
}
