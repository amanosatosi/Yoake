namespace Yoake.Core.Automation;

public static class AutomationScriptReference
{
    // Only references with a known script location are rewritten. Missing or
    // future reference syntax and separators survive Save As byte-for-byte.
    public static string RebaseForSaveAs(string references, string subtitlePath, string automationBase,
        IReadOnlyDictionary<string, string> resolvedScripts)
    {
        return string.Join('|', references.Split('|').Select(part =>
        {
            var reference = part.Trim();
            if (reference.Length == 0 || !resolvedScripts.TryGetValue(reference, out var path)) return part;
            var start = part.IndexOf(reference, StringComparison.Ordinal);
            return part[..start] + Encode(path, subtitlePath, automationBase) + part[(start + reference.Length)..];
        }));
    }

    public static string Resolve(string reference, string? subtitlePath, string automationBase)
    {
        reference = reference.Trim();
        if (reference.Length < 2) throw new ArgumentException("Empty Automation script reference.");
        var relative = reference[1..].Replace('/', Path.DirectorySeparatorChar);
        return reference[0] switch
        {
            '$' => Path.GetFullPath(relative, Path.GetFullPath(automationBase)),
            '~' when subtitlePath is not null => Path.GetFullPath(relative, Path.GetDirectoryName(Path.GetFullPath(subtitlePath))!),
            '/' when Path.IsPathFullyQualified(relative) => Path.GetFullPath(relative),
            _ => throw new ArgumentException($"Unknown or unresolved Automation script reference '{reference}'.")
        };
    }
    public static string Encode(string scriptPath, string? subtitlePath, string automationBase)
    {
        scriptPath = Path.GetFullPath(scriptPath);
        var candidates = new List<string> { "/" + scriptPath.Replace('\\', '/') };
        var baseRelative = Path.GetRelativePath(Path.GetFullPath(automationBase), scriptPath);
        if (!Path.IsPathFullyQualified(baseRelative)) candidates.Add("$" + baseRelative.Replace('\\', '/'));
        if (subtitlePath is not null)
        {
            var relative = Path.GetRelativePath(Path.GetDirectoryName(Path.GetFullPath(subtitlePath))!, scriptPath);
            if (!Path.IsPathFullyQualified(relative)) candidates.Add("~" + relative.Replace('\\', '/'));
        }
        return candidates.OrderBy(p => p.Length).ThenBy(p => p[0] == '$' ? 0 : p[0] == '~' ? 1 : 2).First();
    }
}
