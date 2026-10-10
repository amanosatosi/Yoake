using Yoake.Core.Subtitles;

namespace Yoake.UI.Services;

public enum UnsavedChoice { Cancel, Discard, Save }
public interface IEditorDialogs
{
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
    Task<string?> SaveSubtitleAsync(string suggestedName);
    Task<UnsavedChoice> ConfirmUnsavedAsync(string title);
    Task<bool> ConfirmRevertAsync();
    Task<string?> ReadClipboardAsync();
    Task WriteClipboardAsync(string text);
    Task ShowStylesAsync(Yoake.Core.Subtitles.SubtitleEditor editor);
    Task ShowScriptInfoAsync(Yoake.Core.Subtitles.SubtitleEditor editor);
    Task ShowFindAsync(Yoake.UI.ViewModels.MainWindowViewModel model);
}
public interface IAutomationProgressSession
{
    CancellationToken CancellationToken { get; }
    void Report(double? percent, string? task, string? title);
    void Log(string message, int level);
    void Complete(Exception? failure);
}
public sealed record FontChoice(string Family,string Size);
