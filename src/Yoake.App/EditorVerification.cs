using Yoake.Core.Subtitles;
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
                var frame=video.GetFrameAtTime(0.7);var before=(byte[])frame.Pixels.Clone();
                alphaVerification=MangetsuAlphaVerification.Run(frame);
                using var renderer=new MangetsuSubtitleRenderer(document.Serialize(),frame.Width,frame.Height);
                renderer.Composite(frame,0.7);if(before.AsSpan().SequenceEqual(frame.Pixels))throw new InvalidOperationException("Mangetsu produced no subtitle pixels.");
                editor.SetField(line,"Text",AssVisualTags.SetRectangle(line.Text,false,new(0,0),new(160,90)),"Edit clip");renderer.UpdateTrack(document.Serialize());renderer.Composite(video.GetFrameAtTime(1),1);
            }
            using(var audio=FfmsMediaSession.Open(Path.Combine(fixtures,"audio.wav")))
            {
                var peaks=audio.BuildWaveform();if(peaks.Length<100||peaks.Max()<0.1)throw new InvalidOperationException("Audio peak analysis failed.");
                var spectrum=audio.BuildSpectrum(0,2,64,CancellationToken.None);if(spectrum.Bgra.Where((_,i)=>i%4!=3).All(v=>v==0))throw new InvalidOperationException("Spectrogram analysis failed.");
                using var cancellation=new CancellationTokenSource();cancellation.Cancel();
                try{audio.BuildWaveform(cancellation.Token);throw new InvalidOperationException("Peak cancellation failed.");}catch(OperationCanceledException){}
            }
            File.WriteAllText(report,"PASS: NativeAOT ASS edit/undo/save/reopen; FFMS2 video/frame timestamps/audio decode; full peaks/cancellation; viewport spectrum; Mangetsu render/update/composite.\n"+alphaVerification+"\nAudio device playback and manual UI gestures require interactive smoke testing.\n");
            return 0;
        }
        catch(Exception exception){File.WriteAllText(report,exception.ToString());return 1;}
    }
}
