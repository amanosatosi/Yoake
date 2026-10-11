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
        var normalized = key.Trim().ToUpperInvariant();
        if (normalized.Length == 2 && normalized[0] == 'D' && char.IsAsciiDigit(normalized[1])) return normalized[1..];
        if (normalized.StartsWith("NUMPAD", StringComparison.Ordinal) && normalized.Length == 7 && char.IsAsciiDigit(normalized[6])) return "KP_" + normalized[6];
        return normalized switch
        {
            "RETURN" => "ENTER", "ADD" => "KP_ADD", "SUBTRACT" => "KP_SUBTRACT",
            "MULTIPLY" => "KP_MULTIPLY", "DIVIDE" => "KP_DIVIDE", "DECIMAL" => "KP_DECIMAL",
            _ => normalized,
        };
    }

    public bool IsKnownKey => NormalizedKey.Length==1 && char.IsAsciiLetterOrDigit(NormalizedKey[0])
        || NormalizedKey.StartsWith("F",StringComparison.Ordinal) && int.TryParse(NormalizedKey[1..],out var function) && function is >=1 and <=24
        || NormalizedKey.StartsWith("KP_",StringComparison.Ordinal) && (NormalizedKey.Length==4 && char.IsAsciiDigit(NormalizedKey[3])
            || NormalizedKey[3..] is "ENTER" or "ADD" or "SUBTRACT" or "MULTIPLY" or "DIVIDE" or "DECIMAL")
        || NormalizedKey is "ENTER" or "ESCAPE" or "SPACE" or "TAB" or "LEFT" or "RIGHT" or "UP" or "DOWN" or "HOME" or "END"
            or "PAGEUP" or "PAGEDOWN" or "INSERT" or "DELETE" or "BACKSPACE" or "PAUSE" or "PRINTSCREEN" or "SCROLL" or "CAPSLOCK" or "NUMLOCK"
            or "OEM1" or "OEM2" or "OEM3" or "OEM4" or "OEM5" or "OEM6" or "OEM7" or "OEM8" or "OEM102"
            or "OEMPLUS" or "OEMMINUS" or "OEMCOMMA" or "OEMPERIOD" or "APPS";

    public static HotkeyGesture Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var parts = text.Trim().Split('-');
        var modifiers = KeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            var modifier = part.Trim().ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => KeyModifiers.Control,
                "ALT" => KeyModifiers.Alt, "SHIFT" => KeyModifiers.Shift,
                "META" or "CMD" => KeyModifiers.Meta,
                _ => throw new ArgumentException("Unknown hotkey modifier: " + part),
            };
            if ((modifiers & modifier) != 0) throw new ArgumentException("Repeated hotkey modifier: " + part);
            modifiers |= modifier;
        }
        return new(parts[^1], modifiers);
    }

    public string Format()
    {
        var modifiers = (Modifiers.HasFlag(KeyModifiers.Control) ? "Ctrl-" : "") +
            (Modifiers.HasFlag(KeyModifiers.Alt) ? "Alt-" : "") +
            (Modifiers.HasFlag(KeyModifiers.Shift) ? "Shift-" : "") +
            (Modifiers.HasFlag(KeyModifiers.Meta) ? "Meta-" : "");
        // Canonical names are case-insensitive; preserve a human-readable key.
        var key = NormalizedKey switch
        {
            "ENTER" => "Enter", "ESCAPE" => "Escape", "SPACE" => "Space", "TAB" => "Tab",
            "LEFT" => "Left", "RIGHT" => "Right", "UP" => "Up", "DOWN" => "Down",
            "HOME" => "Home", "END" => "End", "PAGEUP" => "PageUp", "PAGEDOWN" => "PageDown",
            "INSERT" => "Insert", "DELETE" => "Delete", "BACKSPACE" => "Backspace",
            _ => NormalizedKey,
        };
        return modifiers + key;
    }
}

public sealed record HotkeyBinding(string CommandId, HotkeyContext Context, HotkeyGesture Gesture);

public sealed class HotkeyResolver
{
    private readonly List<HotkeyBinding> _bindings = [];

    public IReadOnlyList<HotkeyBinding> Bindings => _bindings;

    public void Replace(IEnumerable<HotkeyBinding> bindings)
    {
        var replacement = new HotkeyResolver();
        foreach (var binding in bindings) replacement.Add(binding);
        _bindings.Clear(); _bindings.AddRange(replacement._bindings);
    }

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

    public HotkeyBinding? Resolve(HotkeyGesture gesture, IReadOnlyList<HotkeyContext> activeContexts, bool globalTimingOverrides = false)
    {
        bool Matches(HotkeyBinding binding) => binding.Gesture.NormalizedKey == gesture.NormalizedKey && binding.Gesture.Modifiers == gesture.Modifiers;
        // Exclusive input modes resolve only their explicit bindings. They must
        // never inherit ordinary editor commands or Medusa keypad overrides.
        var exclusive = activeContexts.LastOrDefault(context => context is HotkeyContext.Mode39 or HotkeyContext.KTiming);
        if (exclusive is HotkeyContext.Mode39 or HotkeyContext.KTiming)
            return _bindings.FirstOrDefault(binding => binding.Context == exclusive && Matches(binding));
        if (globalTimingOverrides)
        {
            var global = _bindings.FirstOrDefault(binding => binding.Context == HotkeyContext.Always && Matches(binding));
            if (global is not null) return global;
        }
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

        return _bindings.FirstOrDefault(binding => binding.Context == HotkeyContext.Default && Matches(binding));
    }
}
