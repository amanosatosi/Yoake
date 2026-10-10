using System.Security.Cryptography;
using System.Text;
using Yoake.Core.Automation;
using Yoake.Core.Subtitles;

namespace Yoake.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly Dictionary<Guid, SemaphoreSlim> _automationReferenceGates = [];
    private readonly Dictionary<Guid, (string? Path, string References)> _automationReferenceState = [];
    private static string LocalAutomationReferences(AssDocument document) =>
        document.TryGetSectionValue("[Aegisub Project Garbage]", "Automation Scripts", out var value) ? value : document.GetScriptInfo("Automation Scripts");

    private void ScheduleDocumentAutomationSync(Guid id) => _ = SynchronizeDocumentAutomationAsync(id, _automationLifetime.Token);
    private async Task SynchronizeDocumentAutomationAsync(Guid id, CancellationToken token)
    {
        if (_disposed || !_documents.ContainsKey(id)) return;
        if (!_automationReferenceGates.TryGetValue(id, out var gate)) _automationReferenceGates[id] = gate = new(1, 1);
        var entered = false;
        try
        {
            await gate.WaitAsync(token); entered = true;
            while (!_disposed && _documents.TryGetValue(id, out var state))
            {
                token.ThrowIfCancellationRequested();
                var session = _workspace.Documents.FirstOrDefault(s => s.Id == id);
                if (session is null) return;
                var current = (session.Path, LocalAutomationReferences(state.Editor.Document));
                if (_automationReferenceState.TryGetValue(id, out var previous) && previous == current) return;
                _automationReferenceState[id] = current;
                var references = current.Item2.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Distinct(StringComparer.Ordinal).ToArray();
                foreach (var item in _automationEntries.Where(e => e.DocumentId == id && (previous.Path != current.Path || !references.Contains(e.StoredReference))).ToArray())
                    await RemoveAutomationScriptAsync(item);
                foreach (var reference in references)
                {
                    token.ThrowIfCancellationRequested();
                    if (!_documents.ContainsKey(id)) return;
                    if (_automationEntries.Any(e => e.DocumentId == id && e.StoredReference == reference)) continue;
                    string path;
                    try { path = AutomationScriptReference.Resolve(reference, session.Path, AutomationBaseDirectory); }
                    catch (Exception e) when (e is ArgumentException or NotSupportedException)
                    {
                        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reference)));
                        var invalid = new AutomationScriptItem(Path.Combine(AutomationUserDirectory, "invalid-reference-" + identity + ".lua"), id)
                        { StoredReference = reference, Error = e.Message };
                        _automationEntries.Add(invalid); OnPropertyChanged(nameof(AutomationScripts)); continue;
                    }
                    var loaded = await LoadAutomationScriptAsync(path, id, token); loaded.StoredReference = reference;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e) { _registry.ReportFailure("automation/local-scripts", e); }
        finally { if (entered) gate.Release(); }
    }
}
