using System.Globalization;
using Avalonia.Threading;
using Yoake.Core.Automation;
using Yoake.Core.Logging;
using Yoake.Native.Automation;
using Yoake.UI.Services;

namespace Yoake.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    private IAutomationHostServices CreateAutomationServices(Guid id, IAutomationProgressSession? progress, bool interactive)
    {
        var state = _documents[id]; var document = state.Editor.Document;
        var session = _workspace.Documents.Single(s => s.Id == id); var media = state.Media;
        string Property(string key) => document.TryGetSectionValue("[Aegisub Project Garbage]", key, out var value) ? value : document.GetScriptInfo(key);
        int IntProperty(string key) => int.TryParse(Property(key), out var n) ? n : 0;
        double NumberProperty(string key, double fallback) => double.TryParse(Property(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : fallback;
        var scriptDirectory = session.Path is { } path ? Path.GetDirectoryName(path)! : AppContext.BaseDirectory;
        string AbsoluteProperty(string key) => Property(key) is { Length: > 0 } value ? Path.GetFullPath(value, scriptDirectory) : "";
        var properties = new Dictionary<string, object?>
        {
            ["automation_scripts"] = Property("Automation Scripts"), ["export_filters"] = Property("Export Filters"),
            ["export_encoding"] = Property("Export Encoding") is { Length: > 0 } encoding ? encoding : "UTF-8",
            ["style_storage"] = Property("Last Style Storage"), ["video_zoom"] = NumberProperty("Video Zoom Percent", 1),
            ["ar_value"] = NumberProperty("Video AR Value", media?.HasVideo == true ? (double)media.Info.Width / media.Info.Height : 1),
            ["ar_mode"] = IntProperty("Video AR Mode"), ["scroll_position"] = IntProperty("Scroll Position"),
            ["active_row"] = id == _activeId && SelectedEvent is { } selected ? document.Events.ToList().IndexOf(selected) : IntProperty("Active Line"),
            ["video_position"] = IntProperty("Video Position"),
            ["audio_file"] = media?.HasAudio == true ? media.SourcePath : AbsoluteProperty("Audio File"),
            ["video_file"] = media?.HasVideo == true ? media.SourcePath : AbsoluteProperty("Video File"),
            ["timecodes_file"] = AbsoluteProperty("Timecodes File"), ["keyframes_file"] = AbsoluteProperty("Keyframes File")
        };
        var timecodes = media?.HasVideo == true && media.FrameTimes.Count > 0 ? new AutomationTimecodeView(media.FrameTimes, media.Info.FramesPerSecond) : null;
        var size = media?.HasVideo == true ? new AutomationVideoSize(media.Info.Width, media.Info.Height, (double)properties["ar_value"]!, (int)properties["ar_mode"]!) : null;
        return new EditorAutomationServices(session.Path is null ? null : Path.GetFileName(session.Path), properties, timecodes, size,
            media?.Keyframes.ToArray() ?? [], new AutomationPathResolver(Path.Combine(AppContext.BaseDirectory, "context.lua"), [], AutomationTokens(id)),
            Dialogs, progress, _automationLog, interactive);
    }

    private sealed class EditorAutomationServices(string? fileName, IReadOnlyDictionary<string, object?> properties,
        AutomationTimecodeView? timecodes, AutomationVideoSize? size, IReadOnlyList<int> keyframes, AutomationPathResolver paths,
        IEditorDialogs? dialogs, IAutomationProgressSession? progress, IAppLog log, bool interactive) : IAutomationHostServices
    {
        public string? FileName => fileName;
        public IReadOnlyDictionary<string, object?> ProjectProperties => properties;
        public AutomationVideoSize? VideoSize => size;
        public IReadOnlyList<int> Keyframes => keyframes;
        public int? FrameFromMilliseconds(int milliseconds) => timecodes?.FrameFromMilliseconds(milliseconds);
        public int? MillisecondsFromFrame(int frame) => timecodes?.MillisecondsFromFrame(frame);
        public AutomationTextMetrics MeasureText(AutomationLine style, string text) => WindowsAutomationTextMeasurer.Measure(style, text);
        public string Translate(string text) => text;
        public string DecodePath(string path) => paths.DecodePath(path);
        public string? ClipboardGet()
        {
            if (dialogs is null) return null;
            var value = Dispatcher.UIThread.InvokeAsync(dialogs.ReadClipboardAsync).GetAwaiter().GetResult();
            return string.IsNullOrEmpty(value) ? null : value;
        }
        public bool ClipboardSet(string text)
        {
            RequireInteractive(); if (dialogs is null) return false;
            Dispatcher.UIThread.InvokeAsync(() => dialogs.WriteClipboardAsync(text)).GetAwaiter().GetResult(); return true;
        }
        public AutomationDialogResult DisplayDialog(AutomationDialogRequest request, CancellationToken token)
        {
            RequireInteractive();
            if (dialogs is null) throw new InvalidOperationException("Automation requires a desktop dialog host.");
            return Dispatcher.UIThread.InvokeAsync(() => dialogs.ShowAutomationDialogAsync(request, token)).WaitAsync(token).GetAwaiter().GetResult();
        }
        public IReadOnlyList<string>? PickFiles(AutomationFileDialogRequest request, CancellationToken token)
        {
            RequireInteractive();
            if (dialogs is null) throw new InvalidOperationException("Automation requires a desktop dialog host.");
            return Dispatcher.UIThread.InvokeAsync(() => dialogs.ShowAutomationFileDialogAsync(request, token)).WaitAsync(token).GetAwaiter().GetResult();
        }
        public void ReportProgress(double? percent = null, string? task = null, string? title = null) => progress?.Report(percent, task, title);
        public void Log(string message, int level) { log.Info($"Automation [{level}]: {message}"); progress?.Log(message, level); }
        private void RequireInteractive() { if (!interactive) throw new InvalidOperationException("Interactive Automation services are unavailable during validation."); }
    }
}
