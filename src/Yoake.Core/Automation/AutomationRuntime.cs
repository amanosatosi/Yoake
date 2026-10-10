using Yoake.Core.Subtitles;

namespace Yoake.Core.Automation;

public sealed record AutomationScriptMetadata(string Name, string Description, string Author, string Version);
public sealed record AutomationMacro(int Index, string Name, string Description, bool HasValidation, bool HasToggle);
public sealed record AutomationExportFilter(int Index, string Name, string Description, int Priority, bool HasConfiguration);
public sealed record AutomationMacroResult(IReadOnlyList<int>? Selection, int? ActiveLine);
public sealed record AutomationValidation(bool Enabled, string? Help, bool Active);
public sealed record AutomationInvocation(AutomationSubtitleDocument Subtitles, IReadOnlyList<int> SelectedLines, int ActiveLine, IAutomationHostServices Services);
public sealed record AutomationTextMetrics(double Width, double Height, double Descent, double ExternalLeading);
public sealed record AutomationVideoSize(int Width, int Height, double AspectRatio, int AspectRatioMode);
public sealed record AutomationDialogRequest(IReadOnlyList<AutomationLine> Controls, IReadOnlyList<string>? Buttons, IReadOnlyDictionary<string, string>? ButtonIds);
public sealed record AutomationDialogResult(object Button, IReadOnlyDictionary<string, object?> Values);
public sealed record AutomationFilterBinding(IAutomationScript Script, AutomationExportFilter Filter, string Name);
public sealed record AutomationFilterSettings(AutomationFilterBinding Binding, IReadOnlyDictionary<string, object?> Values);

// Host services are document/session values, never raw Lua state or UI objects.
// Implementations marshal platform/UI operations; the provider calls on workers.
public interface IAutomationHostServices
{
    string? FileName { get; }
    IReadOnlyDictionary<string, object?> ProjectProperties { get; }
    int? FrameFromMilliseconds(int milliseconds);
    int? MillisecondsFromFrame(int frame);
    AutomationVideoSize? VideoSize { get; }
    IReadOnlyList<int> Keyframes { get; }
    AutomationTextMetrics MeasureText(AutomationLine style, string text);
    string Translate(string text);
    string DecodePath(string path) => path;
    string? ClipboardGet();
    bool ClipboardSet(string text);
    AutomationDialogResult DisplayDialog(AutomationDialogRequest request, CancellationToken cancellationToken);
    void ReportProgress(double? percent = null, string? task = null, string? title = null);
    void Log(string message, int level);
}

public interface IAutomationScript : IDisposable
{
    string Path { get; }
    AutomationScriptMetadata Metadata { get; }
    IReadOnlyList<AutomationMacro> Macros { get; }
    IReadOnlyList<AutomationExportFilter> Filters { get; }
    ValueTask<AutomationMacroResult> RunAsync(int macroIndex, AutomationInvocation invocation, CancellationToken cancellationToken);
    ValueTask<AutomationValidation> ValidateAsync(int macroIndex, AutomationInvocation invocation, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<AutomationLine>> ConfigureFilterAsync(int filterIndex, AutomationInvocation invocation, CancellationToken cancellationToken)
        => throw new NotSupportedException("Export configuration is unavailable in this provider.");
    ValueTask RunFilterAsync(int filterIndex, AutomationInvocation invocation, IReadOnlyDictionary<string, object?> settings, CancellationToken cancellationToken)
        => throw new NotSupportedException("Export filters are unavailable in this provider.");
}

public interface IAutomationRuntimeProvider
{
    ValueTask<IAutomationScript> LoadAsync(string path, AutomationPathResolver paths, CancellationToken cancellationToken);
}
