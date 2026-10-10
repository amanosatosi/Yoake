using System.Security.Cryptography;
using System.Text;
using Yoake.Core.Commands;

namespace Yoake.Core.Automation;

public sealed record AutomationMacroBinding(string CommandId, IAutomationScript Script, AutomationMacro Macro, AutomationValidation Validation);

// Owned by a workspace controller. All registration/state changes occur on the
// caller's application dispatcher; Lua execution remains behind the provider.
public sealed class AutomationCommandCatalog : IDisposable
{
    private sealed record Registration(Guid? DocumentId, string Path, IAutomationScript Script);
    private readonly CommandRegistry _registry;
    private readonly Func<AutomationMacroBinding, CommandInvocation, CancellationToken, ValueTask> _execute;
    private readonly Dictionary<(Guid? Scope, string Path), Registration> _scripts = [];
    private readonly Dictionary<string, AppCommand> _commands = new(StringComparer.Ordinal);
    private readonly Dictionary<(Guid Document, string Command), (IAutomationScript Script, AutomationValidation State)> _validation = [];
    private readonly HashSet<Guid> _busy = [];
    private bool _disposed;
    public event EventHandler? Changed;

    public AutomationCommandCatalog(CommandRegistry registry,
        Func<AutomationMacroBinding, CommandInvocation, CancellationToken, ValueTask> execute)
    {
        _registry = registry; _execute = execute;
    }

    public static string CommandId(string path, string name)
    {
        var normalized = Normalize(path);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        return $"automation/lua/{hash}/{Uri.EscapeDataString(name)}";
    }
    private static string Normalize(string path) => OperatingSystem.IsWindows()
        ? System.IO.Path.GetFullPath(path).ToUpperInvariant() : System.IO.Path.GetFullPath(path);

    // The caller owns script disposal and must cancel an active run before
    // disposing/replacing its interpreter. This catalog owns only commands.
    public void SetScript(Guid? documentId, string path, IAutomationScript? script)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var normalized = Normalize(path);
        if (script is not null)
        {
            foreach (var macro in script.Macros)
            {
                var id = CommandId(path, macro.Name);
                if (_registry.TryGet(id, out var existing) && !_commands.ContainsKey(id))
                    throw new InvalidOperationException($"Automation command ID '{id}' is owned by another controller.");
            }
            _scripts[(documentId, normalized)] = new(documentId, normalized, script);
        }
        else _scripts.Remove((documentId, normalized));
        foreach (var key in _validation.Keys.Where(k => k.Document == documentId || documentId is null).ToArray())
            _validation.Remove(key);
        Rebuild();
    }

    public void RemoveDocument(Guid documentId)
    {
        foreach (var key in _scripts.Keys.Where(k => k.Scope == documentId).ToArray()) _scripts.Remove(key);
        foreach (var key in _validation.Keys.Where(k => k.Document == documentId).ToArray()) _validation.Remove(key);
        _busy.Remove(documentId); Rebuild();
    }

    public IReadOnlyList<AutomationMacroBinding> Macros(Guid documentId) => _scripts.Values
        .Where(r => r.DocumentId is null || r.DocumentId == documentId)
        .SelectMany(r => r.Script.Macros.Select(m => CommandId(r.Path, m.Name)))
        .Distinct(StringComparer.Ordinal).Select(id => Resolve(documentId, id)!).Where(b => b is not null)
        .OrderBy(b => b.Script.Metadata.Name, StringComparer.CurrentCulture).ThenBy(b => b.Macro.Name, StringComparer.CurrentCulture).ToArray();

    public bool IsBusy(Guid documentId) => _busy.Contains(documentId);
    public void UpdateValidation(Guid documentId, AutomationMacroBinding binding, AutomationValidation state)
    {
        if (Resolve(documentId, binding.CommandId)?.Script != binding.Script) return;
        _validation[(documentId, binding.CommandId)] = (binding.Script, state);
        Notify();
    }

    private AutomationMacroBinding? Resolve(Guid documentId, string id)
    {
        foreach (var registration in _scripts.Values.Where(r => r.DocumentId is null || r.DocumentId == documentId)
                     .OrderByDescending(r => r.DocumentId.HasValue))
            foreach (var macro in registration.Script.Macros)
                if (CommandId(registration.Path, macro.Name) == id)
                {
                    var state = _validation.TryGetValue((documentId, id), out var cached) && ReferenceEquals(cached.Script, registration.Script)
                        ? cached.State : new AutomationValidation(!macro.HasValidation, null, false);
                    return new(id, registration.Script, macro, state);
                }
        return null;
    }

    private void Rebuild()
    {
        var desired = _scripts.Values.SelectMany(r => r.Script.Macros.Select(m => (Id: CommandId(r.Path, m.Name), Macro: m)))
            .GroupBy(p => p.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Macro, StringComparer.Ordinal);
        foreach (var id in _commands.Keys.Where(id => !desired.ContainsKey(id)).ToArray())
        {
            _registry.Unregister(_commands[id]); _commands.Remove(id);
        }
        foreach (var (id, macro) in desired)
        {
            if (_commands.ContainsKey(id)) continue;
            var command = new AppCommand(new(id, macro.Name, macro.Description, "Automation"),
                async (invocation, token) =>
                {
                    if (invocation.Context.DocumentId is not { } doc || Resolve(doc, id) is not { } binding || !_busy.Add(doc)) return;
                    Notify();
                    try { await _execute(binding, invocation, token); }
                    finally { _busy.Remove(doc); Notify(); }
                },
                context => context.DocumentId is { } doc && !_busy.Contains(doc) && Resolve(doc, id)?.Validation.Enabled == true,
                context => context.DocumentId is { } doc && Resolve(doc, id)?.Validation.Active == true);
            _registry.Register(command); _commands[id] = command;
        }
        Notify();
    }

    private void Notify() { Changed?.Invoke(this, EventArgs.Empty); _registry.NotifyStateChanged(); }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        foreach (var command in _commands.Values) _registry.Unregister(command);
        _commands.Clear(); _scripts.Clear(); _validation.Clear(); _busy.Clear(); Notify();
    }
}
