namespace Yoake.UI.Services;

public enum UnsavedChoice { Cancel, Discard, Save }
public interface IEditorDialogs
{
    Task<FontChoice?> ChooseFontAsync(string family,string size)=>Task.FromResult<FontChoice?>(null);
    Task<AssColor?> ChooseColorAsync(AssColor color)=>Task.FromResult<AssColor?>(null);
    Task<string?> OpenSubtitleAsync();
    Task<string?> OpenMediaAsync();
    Task<string?> SaveSubtitleAsync(string suggestedName);
    Task<UnsavedChoice> ConfirmUnsavedAsync(string title);
    Task<bool> ConfirmRevertAsync();
    Task<string?> ReadClipboardAsync();
    Task WriteClipboardAsync(string text);
    Task ShowStylesAsync(Yoake.Core.Subtitles.SubtitleEditor editor);
    Task ShowScriptInfoAsync(Yoake.Core.Subtitles.SubtitleEditor editor);
    Task ShowFindAsync(Yoake.UI.ViewModels.MainWindowViewModel model);
}
public sealed record FontChoice(string Family,string Size);
