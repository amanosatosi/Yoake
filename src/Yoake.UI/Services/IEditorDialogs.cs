using Yoake.Core.Subtitles;

namespace Yoake.UI.Services;

public enum UnsavedChoice { Cancel, Discard, Save }
public interface IEditorDialogs
{
    Task ShowResampleAsync(Yoake.UI.ViewModels.MainWindowViewModel model,Guid id,CancellationToken token)=>throw new NotSupportedException("Resolution resampling is unavailable.");
    Task ShowFontCollectorAsync(Yoake.UI.ViewModels.MainWindowViewModel model,Guid id,CancellationToken token)=>throw new NotSupportedException("Fonts Collector is unavailable.");
    Task<bool> ShowAssDrawMissingAsync(CancellationToken token)=>Task.FromResult(false);
    Task ShowAssDrawOptionsAsync(Yoake.UI.ViewModels.MainWindowViewModel model)=>ShowOptionsAsync(model);
    Task ShowAttachmentsAsync(Yoake.UI.ViewModels.MainWindowViewModel model,Guid id,CancellationToken token)=>throw new NotSupportedException("Attachment manager is unavailable.");
    Task<bool> ConfirmAttachmentOverwriteAsync(string path,CancellationToken token)=>Task.FromResult(false);
    Task ShowRecoveryAsync(Yoake.UI.ViewModels.MainWindowViewModel model,CancellationToken token)=>throw new NotSupportedException("Recovery manager is unavailable.");
    Task<bool> ConfirmRecoveryDiscardAsync(string name,CancellationToken token)=>Task.FromResult(false);
    Task SetVideoDetachedAsync(bool detached, CancellationToken token) => throw new NotSupportedException("Detached Video is unavailable.");
    Task ShowOptionsAsync(Yoake.UI.ViewModels.MainWindowViewModel model) => throw new NotSupportedException("Options are unavailable.");
    Task ShowLogAsync(string path)=>throw new NotSupportedException("Log window is unavailable.");
    Task<string?> OpenAutomationScriptAsync() => Task.FromResult<string?>(null);
    Task ShowAutomationManagerAsync(Yoake.UI.ViewModels.MainWindowViewModel model) => Task.CompletedTask;
    Task<AutomationSearchPaths?> EditAutomationPathsAsync(IReadOnlyList<string> autoload, IReadOnlyList<string> includes) => Task.FromResult<AutomationSearchPaths?>(null);
    Task<Yoake.Core.Automation.AutomationDialogResult> ShowAutomationDialogAsync(Yoake.Core.Automation.AutomationDialogRequest request, CancellationToken cancellationToken) => throw new NotSupportedException("Automation dialogs are unavailable.");
    IAutomationProgressSession? BeginAutomationProgress(string title) => null;
    Task<IReadOnlyList<string>?> ShowAutomationFileDialogAsync(Yoake.Core.Automation.AutomationFileDialogRequest request, CancellationToken token) => throw new NotSupportedException("Automation file dialogs are unavailable.");
    Task<AutomationExportChoice?> ShowAutomationExportAsync(IReadOnlyList<AutomationExportOption> options, IReadOnlyList<string> selected, string encoding, CancellationToken token) => throw new NotSupportedException("Automation export is unavailable.");
    Task<FontChoice?> ChooseFontAsync(string family,string size)=>Task.FromResult<FontChoice?>(null);
    Task<AssColor?> ChooseColorAsync(AssColor color)=>Task.FromResult<AssColor?>(null);
    Task<string?> OpenSubtitleAsync();
    Task<string?> OpenMediaAsync();
    Task<string?> OpenVideoAsync()=>Task.FromResult<string?>(null);
    Task<string?> OpenAudioAsync()=>Task.FromResult<string?>(null);
    Task<Yoake.UI.ViewModels.VideoJumpRequest?> ShowVideoJumpAsync(Yoake.UI.ViewModels.MainWindowViewModel model)=>Task.FromResult<Yoake.UI.ViewModels.VideoJumpRequest?>(null);
    Task<string?> OpenKeyframesAsync()=>Task.FromResult<string?>(null);
    Task<string?> OpenTimecodesAsync()=>Task.FromResult<string?>(null);
    Task<string?> SaveTimecodesAsync(string suggestedName)=>Task.FromResult<string?>(null);
    Task<string?> SaveKeyframesAsync(string suggestedName)=>Task.FromResult<string?>(null);
    Task<string?> SaveAudioClipAsync(string suggestedName)=>Task.FromResult<string?>(null);
    Task<string?> SaveSubtitleAsync(string suggestedName);
    Task<UnsavedChoice> ConfirmUnsavedAsync(string title);
    Task<bool> ConfirmRevertAsync();
    Task<string?> ReadClipboardAsync();
    Task<PasteOverFields?> ShowPasteOverAsync(PasteOverFields initial,CancellationToken token=default)=>Task.FromResult<PasteOverFields?>(null);
    Task WriteClipboardAsync(string text);
    Task WriteFrameClipboardAsync(Yoake.Native.DecodedVideoFrame image, Func<bool> isCurrent, CancellationToken token) => throw new NotSupportedException("Image clipboard is unavailable.");
    Task<Yoake.Core.Media.DummyVideoOptions?> ShowDummyVideoAsync(Yoake.Core.Media.DummyVideoOptions initial) => Task.FromResult<Yoake.Core.Media.DummyVideoOptions?>(null);
    Task<string?> ShowCustomAspectAsync(double current) => Task.FromResult<string?>(null);
    Task ShowVideoDetailsAsync(Yoake.UI.ViewModels.VideoDetails details) => throw new NotSupportedException("Video details are unavailable.");
    Task ShowStylesAsync(Yoake.Core.Subtitles.SubtitleEditor editor);
    Task ShowScriptInfoAsync(Yoake.Core.Subtitles.SubtitleEditor editor);
    Task ShowFindAsync(Yoake.UI.ViewModels.MainWindowViewModel model);
    Task ShowFindAsync(Yoake.UI.ViewModels.MainWindowViewModel model,bool focusReplacement)=>ShowFindAsync(model);
    Task ShowShiftTimesAsync(Yoake.UI.ViewModels.MainWindowViewModel model)=>throw new NotSupportedException("Shift Times is unavailable.");
    Task ShowAudioTimingOptionsAsync(Yoake.UI.ViewModels.MainWindowViewModel model)=>throw new NotSupportedException("Audio timing options are unavailable.");
    Task ShowSelectLinesAsync(Yoake.UI.ViewModels.MainWindowViewModel model)=>throw new NotSupportedException("Select Lines is unavailable.");
}
public interface IAutomationProgressSession
{
    CancellationToken CancellationToken { get; }
    void Report(double? percent, string? task, string? title);
    void Log(string message, int level);
    void Complete(Exception? failure);
}
public sealed record FontChoice(string Family,string Size);
