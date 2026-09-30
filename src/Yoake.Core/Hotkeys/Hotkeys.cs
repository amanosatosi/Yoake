namespace Yoake.Core.Hotkeys;

[Flags]
public enum KeyModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
    Meta = 8,
}

public enum HotkeyContext
{
    Always,
    Default,
    Video,
    Audio,
    SubtitleGrid,
    SubtitleEdit,
    VisualTools,
    KTiming,
    Mode39,
    Translation,
    Styling,
}

public readonly record struct HotkeyGesture(string Key, KeyModifiers Modifiers = KeyModifiers.None)
{
    public string NormalizedKey { get; } = Normalize(Key);

    private static string Normalize(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A hotkey key cannot be empty.", nameof(key));
        return key.Trim().ToUpperInvariant();
    }
}

public sealed record HotkeyBinding(string CommandId, HotkeyContext Context, HotkeyGesture Gesture);

public sealed class HotkeyResolver
{
    private readonly List<HotkeyBinding> _bindings = [];

    public IReadOnlyList<HotkeyBinding> Bindings => _bindings;

    public void Add(HotkeyBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var conflict = _bindings.FirstOrDefault(existing =>
            existing.Context == binding.Context &&
            existing.Gesture.NormalizedKey == binding.Gesture.NormalizedKey &&
            existing.Gesture.Modifiers == binding.Gesture.Modifiers);

        if (conflict is not null)
            throw new InvalidOperationException(
                $"Hotkey conflict in {binding.Context}: {binding.Gesture.Modifiers}+{binding.Gesture.NormalizedKey} " +
                $"is already bound to {conflict.CommandId}.");

        _bindings.Add(binding);
    }

    public HotkeyBinding? Resolve(HotkeyGesture gesture, IReadOnlyList<HotkeyContext> activeContexts)
    {
        for (var index = activeContexts.Count - 1; index >= 0; index--)
        {
            var context = activeContexts[index];
            var match = _bindings.FirstOrDefault(binding =>
                binding.Context == context &&
                binding.Gesture.NormalizedKey == gesture.NormalizedKey &&
                binding.Gesture.Modifiers == gesture.Modifiers);
            if (match is not null)
                return match;
        }

        return _bindings.FirstOrDefault(binding =>
            binding.Context == HotkeyContext.Always &&
            binding.Gesture.NormalizedKey == gesture.NormalizedKey &&
            binding.Gesture.Modifiers == gesture.Modifiers);
    }
}
