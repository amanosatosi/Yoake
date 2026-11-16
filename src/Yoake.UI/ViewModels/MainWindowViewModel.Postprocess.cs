using Yoake.Core.Subtitles;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed record SubtitlePostprocessToolRequest(Guid Owner,SubtitlePostprocessOptions Options,bool PreviewOnly=false);
public sealed partial class MainWindowViewModel
{
    public SubtitlePostprocessOptions PostprocessPreferences=>_settings.PostprocessPreferences??new();
    private void RegisterPostprocessCommands()
    {
        bool Available(Yoake.Core.Commands.CommandContext context)=>_activeId is not null&&Events.Count>0&&!HasGesture&&!HasEditOperation&&(context.DocumentId is null||context.DocumentId==_activeId);
        _registry.Register(new(new("tool/time/postprocess","Timing Postprocessor…","Add leads, make adjacent and snap to keyframes","Timing"),async (_,token)=>
        { if(Dialogs is not null&&_activeId is {} id)await Dialogs.ShowPostprocessAsync(this,id,token); },Available));
        Actions["tool/time/postprocess"]=new RegistryCommand(_registry,"tool/time/postprocess",CurrentContext,hotkeys:()=>HotkeyProfile);
        _registry.Register(new(new("tool/time/postprocess/apply","Apply timing postprocess","Process timing as one undoable operation","Timing"),async (invocation,token)=>
        {
            if(invocation.Parameter is not SubtitlePostprocessToolRequest request||request.Owner!=_activeId)throw new ArgumentException("Use the timing tool's owning document.");
            var options=request.Options with { Styles=request.Options.Styles?.ToArray() };
            var timing=options.SnapKeyframes?ActiveFrameTiming():null;var keys=options.SnapKeyframes?Keyframes.ToArray():null;
            var frameCount=_media?.HasVideo==true?FrameTimes.Count:(int?)null;
            var count=await CalculateTimingAsync((snapshot,ct)=>snapshot.Postprocess(options,timing,keys,frameCount,ct),Selection(),token,request.PreviewOnly);
            if(count<0||request.PreviewOnly)return;
            var settings=_settings with { PostprocessPreferences=options };
            try { _settingsStore.Save(settings);_settings=settings;OnPropertyChanged(nameof(PostprocessPreferences)); }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException) { SubtitleStatus+=" Preferences could not be saved: "+error.Message; }
        },Available));
    }
}
