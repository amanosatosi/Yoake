using Yoake.Core.Subtitles;

namespace Yoake.Core.Automation;

public static class AutomationExportPipeline
{
    // Assign duplicate-name suffixes in registration order, then sort stably by
    // descending priority, matching AssExportFilterChain::Register in 3.2.2.
    public static IReadOnlyList<AutomationFilterBinding> Filters(IEnumerable<IAutomationScript> scripts)
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        List<AutomationFilterBinding> filters = [];
        foreach (var script in scripts)
            foreach (var filter in script.Filters)
            {
                var name = filter.Name; var suffix = 1;
                while (!names.Add(name)) name = $"{filter.Name} ({suffix++})";
                filters.Add(new(script, filter, name));
            }
        return filters.OrderByDescending(f => f.Filter.Priority).ToArray();
    }

    // The input is a serialized snapshot captured by the session owner. Each
    // filter sees the preceding filter's output, never the live editor or undo.
    // Nothing is returned on failure/cancellation, including partial changes.
    public static async ValueTask<AssDocument> RunAsync(string source, IReadOnlyList<AutomationFilterSettings> filters,
        IAutomationHostServices services, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var copy = AssDocument.Parse(source);
        var editor = new SubtitleEditor(copy);
        foreach (var filter in filters)
        {
            token.ThrowIfCancellationRequested();
            using var subs = new AutomationSubtitleDocument(copy);
            await filter.Binding.Script.RunFilterAsync(filter.Binding.Filter.Index, new(subs, [], 0, services), filter.Values, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            subs.Commit(editor, filter.Binding.Name);
        }
        token.ThrowIfCancellationRequested();
        return copy;
    }
}
