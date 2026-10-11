namespace Yoake.Core.Hotkeys;

// An empty Gestures array is an explicit removal, distinct from no override.
public sealed record HotkeyOverride(HotkeyContext Context, string CommandId, string[] Gestures);

public sealed class HotkeyProfile
{
    private readonly HotkeyBinding[] _defaults;
    private HotkeyOverride[] _overrides = [];
    public HotkeyResolver Resolver { get; } = new();
    public IReadOnlyList<HotkeyBinding> Defaults => Array.AsReadOnly(_defaults);
    public HotkeyOverride[] Overrides => _overrides.Select(Clone).ToArray();
    public event EventHandler? Changed;

    public HotkeyProfile(IEnumerable<HotkeyBinding> defaults, IEnumerable<HotkeyOverride>? overrides = null, Action<string>? report = null)
    {
        _defaults = defaults.ToArray(); Resolver.Replace(_defaults);
        foreach (var item in (overrides ?? []).OfType<HotkeyOverride>())
        {
            try { Set(item.Context, item.CommandId, item.Gestures); }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            { report?.Invoke("Ignored invalid hotkey override: " + exception.Message); }
        }
    }

    private static HotkeyOverride Clone(HotkeyOverride item) => item with { Gestures = (string[])item.Gestures.Clone() };

    public void Set(HotkeyContext context, string commandId, IEnumerable<string> gestures)
    {
        if (!Enum.IsDefined(context)) throw new ArgumentException("Unknown hotkey context.");
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentNullException.ThrowIfNull(gestures);
        commandId = AegisubHotkeyDefaults.MapCommand(commandId);
        var parsed = gestures.Select(HotkeyGesture.Parse).ToArray();
        if(parsed.Any(gesture=>!gesture.IsKnownKey))throw new ArgumentException("Unknown key name. Capture a shortcut or use a supported key name.");
        if (parsed.Any(gesture => (gesture.Modifiers & ~(KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta)) != 0))
            throw new ArgumentException("Invalid hotkey modifiers.");
        var replacement = new HotkeyOverride(context, commandId, parsed.Select(gesture => gesture.Format()).ToArray());
        Apply(_overrides.Where(item => item.Context != context || item.CommandId != commandId).Append(replacement).ToArray());
    }

    public void ResetCommand(HotkeyContext context, string commandId)
    {
        commandId = AegisubHotkeyDefaults.MapCommand(commandId);
        Apply(_overrides.Where(item => item.Context != context || item.CommandId != commandId).ToArray());
    }
    public void ResetContext(HotkeyContext context) => Apply(_overrides.Where(item => item.Context != context).ToArray());
    public void ResetAll() => Apply([]);

    private void Apply(HotkeyOverride[] overrides)
    {
        var replaced = overrides.Select(item => (item.Context, item.CommandId)).ToHashSet();
        var bindings = _defaults.Where(binding => !replaced.Contains((binding.Context, binding.CommandId)))
            .Concat(overrides.SelectMany(item => item.Gestures.Select(gesture => new HotkeyBinding(item.CommandId, item.Context, HotkeyGesture.Parse(gesture))))).ToArray();
        // Validate the whole candidate before changing either effective state or
        // persisted overrides. A conflict leaves the current profile intact.
        Resolver.Replace(bindings);
        _overrides = overrides.Select(Clone).ToArray();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public string[] EffectiveGestures(string commandId, HotkeyContext context) => Resolver.Bindings
        .Where(binding => binding.CommandId == commandId && binding.Context == context)
        .Select(binding => binding.Gesture.Format()).ToArray();

    public static HotkeyOverride[] Normalize(HotkeyOverride[]? overrides, Action<string>? report = null)
        => new HotkeyProfile(AegisubHotkeyDefaults.ForYoake(), (overrides ?? []).OfType<HotkeyOverride>(), report).Overrides;
}
