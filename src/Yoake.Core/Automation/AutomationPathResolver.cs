namespace Yoake.Core.Automation;

public sealed class AutomationPathResolver
{
    private readonly string _masterDirectory;
    private readonly IReadOnlyDictionary<string, string> _tokens;
    public IReadOnlyList<string> IncludeDirectories { get; }
    public string PackagePath => string.Join(';', IncludeDirectories.SelectMany(p => new[] { Path.Combine(p, "?.lua"), Path.Combine(p, "?", "init.lua") })) + ";";

    public AutomationPathResolver(string masterScript, IEnumerable<string> configuredIncludes,
        IReadOnlyDictionary<string, string>? tokens = null)
    {
        if (!Path.IsPathFullyQualified(masterScript)) throw new ArgumentException("Master script path must be absolute.");
        _masterDirectory = Path.GetDirectoryName(Path.GetFullPath(masterScript))!;
        _tokens = tokens ?? new Dictionary<string, string>();
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        IncludeDirectories = new[] { _masterDirectory }.Concat(configuredIncludes.Select(DecodePath)
            .Where(p => Path.IsPathFullyQualified(p) && Directory.Exists(p)).Select(Path.GetFullPath)).Distinct(comparer).ToArray();
    }

    public string DecodePath(string path)
    {
        foreach (var token in new[] { "?audio", "?data", "?dictionary", "?local", "?script", "?temp", "?user", "?video" })
            if (path.StartsWith(token, StringComparison.Ordinal) && _tokens.TryGetValue(token, out var root) && root.Length > 0)
                return Path.Combine(root, path[token.Length..].TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar));
        return path.Replace('/', Path.DirectorySeparatorChar);
    }

    public string ResolveInclude(string name)
    {
        var normalized = name.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        if (name.IndexOfAny(['/', '\\']) >= 0 || Path.IsPathFullyQualified(name))
        {
            var candidate = Path.GetFullPath(normalized, _masterDirectory);
            if (File.Exists(candidate)) return candidate;
        }
        else
            foreach (var directory in IncludeDirectories)
            {
                var candidate = Path.Combine(directory, normalized);
                if (File.Exists(candidate)) return candidate;
            }
        throw new FileNotFoundException($"Lua include not found: {name}", name);
    }

    public string? ResolveModule(string name, string? packagePath = null)
    {
        var module = name.Replace('.', Path.DirectorySeparatorChar);
        foreach (var template in (packagePath ?? PackagePath).Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var path = template.Replace("?", module, StringComparison.Ordinal);
            // Modified relative package.path entries remain master-relative,
            // avoiding process CWD changes affecting other document sessions.
            path = Path.GetFullPath(path, _masterDirectory);
            if (Path.GetExtension(path).Equals(".lua", StringComparison.OrdinalIgnoreCase))
            {
                var moon = Path.ChangeExtension(path, ".moon");
                if (File.Exists(moon)) return moon;
            }
            if (File.Exists(path)) return path;
        }
        return null;
    }

    public static IReadOnlyList<string> Discover(IEnumerable<string> directories, CancellationToken cancellationToken)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        HashSet<string> paths = new(comparer);
        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("Autoload directories must be absolute.");
            if (!Directory.Exists(directory)) continue;
            foreach (var path in Directory.EnumerateFiles(directory).Order(comparer))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Path.GetExtension(path).ToLowerInvariant() is ".lua" or ".moon") paths.Add(Path.GetFullPath(path));
            }
        }
        return paths.Order(comparer).ToArray();
    }
}
