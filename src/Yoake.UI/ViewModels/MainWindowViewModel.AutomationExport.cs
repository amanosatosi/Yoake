using System.Text;
using Yoake.Core.Automation;
using Yoake.Core.Commands;
using Yoake.UI.Services;

namespace Yoake.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly Dictionary<Guid, (string[] Filters, string Encoding)> _automationExportChoices = [];
    private async ValueTask ExportAutomationAsync(CommandInvocation command, CancellationToken token)
    {
        if (command.Context.DocumentId is not { } id || !_documents.TryGetValue(id, out var state) || id != _activeId || Dialogs is null || !CommitDraft() || _automationRuns.ContainsKey(id)) return;
        CancelGesture(); StopPlayback(); _automationValidation?.Cancel();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, _automationLifetime.Token);
        _automationRuns[id] = cancellation;
        try
        {
            var filters = AutomationExportPipeline.Filters(AutomationScripts.Where(e => e.Script is not null).Select(e => e.Script!));
            // Reload/remove cancels the entire chain before any output is written.
            var registrations = filters.Select(f => _automationEntries.First(e => ReferenceEquals(e.Script, f.Script)).Lifetime.Token.Register(cancellation.Cancel)).ToArray();
            try
            {
                var services = CreateAutomationServices(id, null, interactive: false);
                List<AutomationExportOption> options = [];
                foreach (var filter in filters)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    using var subs = new AutomationSubtitleDocument(state.Editor.Document, writable: false);
                    try
                    {
                        var controls = await filter.Script.ConfigureFilterAsync(filter.Filter.Index, new(subs, [], 0, services), cancellation.Token);
                        options.Add(new(filter, controls, null));
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e) { options.Add(new(filter, [], e.Message)); _registry.ReportFailure("automation/export/config", e); }
                }
                var document = state.Editor.Document;
                string Property(string key) => document.TryGetSectionValue("[Aegisub Project Garbage]", key, out var value) ? value : document.GetScriptInfo(key);
                var preferences = _automationExportChoices.TryGetValue(id, out var remembered) ? remembered
                    : (Property("Export Filters").Split('|', StringSplitOptions.RemoveEmptyEntries), Property("Export Encoding"));
                var choice = await Dialogs.ShowAutomationExportAsync(options, preferences.Item1, preferences.Item2, cancellation.Token);
                if (choice is null) return;
                var path = command.Parameter as string ?? await Dialogs.SaveSubtitleAsync(Path.GetFileNameWithoutExtension(SuggestedSubtitleFileName) + ".export.ass");
                if (path is null) return;
                cancellation.Token.ThrowIfCancellationRequested();
                if (!_documents.TryGetValue(id, out var current) || !ReferenceEquals(current, state)) throw new OperationCanceledException();
                // Dialog choices belong to this session. Export does not edit the
                // live ASS document, its dirty state, or its undo/redo history.
                _automationExportChoices[id] = (choice.Filters.Select(f => f.Binding.Name).ToArray(), choice.Encoding);
                var source = document.Serialize();
                var progress = Dialogs.BeginAutomationProgress("Export subtitles");
                using var work = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token, progress?.CancellationToken ?? CancellationToken.None);
                Exception? failure = null;
                try
                {
                    var exportServices = CreateAutomationServices(id, progress, interactive: false);
                    var copy = await Task.Run(async () => await AutomationExportPipeline.RunAsync(source, choice.Filters, exportServices, work.Token), work.Token);
                    var encoding = choice.Encoding switch
                    {
                        "UTF-8 BOM" => (Encoding)new UTF8Encoding(true, true),
                        "UTF-16 LE" => new UnicodeEncoding(false, true, true),
                        "UTF-16 BE" => new UnicodeEncoding(true, true, true),
                        _ => new UTF8Encoding(false, true)
                    };
                    await Task.Run(() => copy.Save(path, encoding, choice.Encoding != "UTF-8", work.Token), work.Token);
                    SubtitleStatus = $"Exported {Path.GetFileName(path)}";
                }
                catch (Exception e) { failure = e; throw; }
                finally { progress?.Complete(failure); }
            }
            finally { foreach (var registration in registrations) registration.Dispose(); }
        }
        catch (OperationCanceledException) { SubtitleStatus = "Export cancelled; output discarded"; }
        finally { _automationRuns.Remove(id); ScheduleAutomationValidation(); }
    }
}
