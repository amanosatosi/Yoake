using Yoake.Core.Subtitles;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed record OpenSubtitleEncodingRequest(string Path,int CodePage);
public sealed partial class MainWindowViewModel
{
    private void RegisterEncodingCommands()
    {
        _registry.Register(new(new("subtitle/open/charset","Open with character set…","Decode a subtitle using an explicit encoding","Subtitle"),async (invocation,token)=>
        {
            var request=invocation.Parameter as OpenSubtitleEncodingRequest;
            if(request is null&&Dialogs is {} dialogs)
            {
                var path=await dialogs.OpenSubtitleAsync();if(path is null)return;
                var codePage=await dialogs.ChooseSubtitleEncodingAsync();if(codePage is null)return;
                request=new(path,codePage.Value);
            }
            token.ThrowIfCancellationRequested();if(request is not null)await OpenSubtitleForUserAsync(request.Path,SubtitleEncodings.Get(request.CodePage));
        },_=>!OpeningSubtitle));
        _registry.Register(new(new("subtitle/save/utf8","Save As UTF-8…","Save a Unicode subtitle and retain UTF-8 for future saves","Subtitle"),async (invocation,token)=>
        {
            var path=invocation.Parameter as string??(Dialogs is null?null:await Dialogs.SaveSubtitleAsync(_workspace.ActiveDocument?.Title??"Untitled.ass"));
            token.ThrowIfCancellationRequested();if(path is not null)SaveActiveSubtitle(path,true);
        },_=>ActiveEditor is not null));
        foreach(var id in new[] { "subtitle/open/charset","subtitle/save/utf8" })Actions[id]=new RegistryCommand(_registry,id,CurrentContext,hotkeys:()=>HotkeyProfile);
    }
}
