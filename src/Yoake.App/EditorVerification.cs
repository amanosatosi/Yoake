using Yoake.Core.Subtitles;
using Yoake.Core.Media;
using Yoake.Native;

namespace Yoake.App;

internal static class EditorVerification
{
    // Runs inside the shipping NativeAOT executable against its packaged DLLs.
    // The normal desktop startup smoke remains an independent CI requirement.
    public static int Run(string fixtures, string report)
    {
        try
        {
            string alphaVerification;
            var document=AssDocument.CreateEmpty();var editor=new SubtitleEditor(document);
            editor.SetScriptInfo(new Dictionary<string,string>{{"PlayResX","160"},{"PlayResY","90"}});
            var line=editor.Insert(null,false);
            editor.SetField(line,"Text","{\\pos(80,65)\\future(opaque)\\blur0.4}Yoake 日本語","Edit subtitle text");
            editor.EditStyle(document.Styles[0],new Dictionary<string,string>{{"Fontsize","20"},{"MarginV","5"}});
            using(var drag=editor.Undo.BeginTransaction("Drag timing")){editor.SetTiming(line,100,1800);editor.SetTiming(line,200,1700);drag.Commit();}
            editor.Undo.Undo();editor.Undo.Redo();
            var saved=Path.Combine(fixtures,"roundtrip.ass");document.Save(saved);
            if(AssDocument.Load(saved).Serialize()!=document.Serialize()||!document.Serialize().Contains("\\future(opaque)",StringComparison.Ordinal))throw new InvalidOperationException("ASS save/reopen lost data.");
            using(var video=FfmsMediaSession.Open(Path.Combine(fixtures,"video.avi")))
            {
                if(!video.HasVideo||video.Info.FrameCount!=10)throw new InvalidOperationException("FFMS2 did not decode the video fixture.");
                var next=video.AdjacentFrameTime(0,1);if(next<=0||video.AdjacentFrameTime(next,-1)>=next)throw new InvalidOperationException("FFMS2 frame stepping failed.");
                var originalTimes=video.FrameTimes;var originalInfo=video.Info;var originalKeys=video.Keyframes;
                var expected=video.GetFrameAtTime(originalTimes[2]).Pixels;
                var retimed=TimecodeFile.Read(new StringReader("Assume 25\n0,1,50"));video.SetExternalTimecodes(retimed.ForVideoMap(video.Info.FrameCount));
                if(!video.GetFrameAtTime(.04).Pixels.AsSpan().SequenceEqual(expected)||video.AdjacentFrameTime(.02,1)!=.04||!ReferenceEquals(originalKeys,video.Keyframes))throw new InvalidOperationException("External timecodes must decode the indexed frame without rebuilding providers.");
                video.SetExternalTimecodes(null);
                if(!ReferenceEquals(originalTimes,video.FrameTimes)||!ReferenceEquals(originalInfo,video.Info)||!video.GetFrameAtTime(originalTimes[2]).Pixels.AsSpan().SequenceEqual(expected))throw new InvalidOperationException("Closing timecodes must restore original metadata and decoding.");
                var frame=video.GetFrameAtTime(0.7);var before=(byte[])frame.Pixels.Clone();
                alphaVerification=MangetsuAlphaVerification.Run(frame);
                using var renderer=new MangetsuSubtitleRenderer(document.Serialize(),frame.Width,frame.Height);
                renderer.Composite(frame,0.7);if(before.AsSpan().SequenceEqual(frame.Pixels))throw new InvalidOperationException("Mangetsu produced no subtitle pixels.");
                editor.SetField(line,"Text",AssVisualTags.SetRectangle(line.Text,false,new(0,0),new(160,90)),"Edit clip");renderer.UpdateTrack(document.Serialize());renderer.Composite(video.GetFrameAtTime(1),1);
            }
            using(var audio=FfmsMediaSession.Open(Path.Combine(fixtures,"audio.wav")))
            {
                var peaks=audio.BuildWaveform();if(peaks.Count<100||peaks.Envelopes.Max(p=>p.Maximum)<0.1||peaks.Envelopes.Min(p=>p.Minimum)>-0.1)throw new InvalidOperationException("Audio peak analysis failed.");
                var detail=audio.BuildWaveformViewport(0.7,0.02,1000,CancellationToken.None);
                if(detail.StepSeconds>=peaks.StepSeconds||detail.Envelopes.Select(p=>p.Maximum).Distinct().Count()<20)throw new InvalidOperationException("Fine waveform zoom must decode sample detail rather than repeat coarse slabs.");
                var spectrum=audio.BuildSpectrum(0,2,64,CancellationToken.None);if(spectrum.Bgra.Where((_,i)=>i%4!=3).All(v=>v==0))throw new InvalidOperationException("Spectrogram analysis failed.");
                var clip=Path.Combine(fixtures,"native-audio-clip.wav");audio.SaveAudioClip(clip,new(300,1400));
                using(var exported=FfmsMediaSession.Open(clip))
                {
                    if(exported.Info.AudioSamples!=52800||exported.Info.SampleRate!=audio.Info.SampleRate||exported.Info.Channels!=audio.Info.Channels)throw new InvalidOperationException("Native clip export must retain sample geometry and selected interval.");
                    var original=audio.ReadAudioFrames(14400,64).Samples;var decoded=exported.ReadAudioFrames(0,64).Samples;
                    for(var index=0;index<original.Length;index++)if(Math.Abs(decoded[index]-Yoake.Core.Audio.AudioClipFile.Pcm16(original[index])/32768f)>1f/32768)throw new InvalidOperationException("Native clip PCM must correspond to the selected source interval.");
                }
                try{audio.SaveAudioClip(audio.SourcePath,new(0,100));throw new InvalidOperationException("Audio export must reject overwriting its source.");}catch(ArgumentException){}
                using var cancellation=new CancellationTokenSource();cancellation.Cancel();
                using(var output=new WindowsWaveOutStream())
                {
                    try{output.PlayAsync(audio,0,null,cancellation.Token).GetAwaiter().GetResult();throw new InvalidOperationException("Canceled audio playback must not open an output device.");}
                    catch(OperationCanceledException){}
                    var other=Yoake.Core.Audio.AudioPlaybackPlan.Create(Yoake.Core.Audio.AudioPlaybackKind.Selection,11025,1000,new(0,100));
                    try{output.PlayAsync(audio,0,null,CancellationToken.None,other).GetAwaiter().GetResult();throw new InvalidOperationException("Audio playback must reject foreign-provider sample geometry before opening a device.");}
                    catch(ArgumentException){}
                }
                try{audio.BuildWaveform(cancellation.Token);throw new InvalidOperationException("Peak cancellation failed.");}catch(OperationCanceledException){}
            }
            File.WriteAllText(report,"PASS: NativeAOT ASS edit/undo/save/reopen; FFMS2 video/frame timestamps/audio decode; full peaks/cancellation; viewport spectrum; Mangetsu render/update/composite.\n"+alphaVerification+"\nAudio device playback and manual UI gestures require interactive smoke testing.\n");
            return 0;
        }
        catch(Exception exception){File.WriteAllText(report,exception.ToString());return 1;}
    }
}
