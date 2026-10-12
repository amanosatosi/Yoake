namespace Yoake.Core.Hotkeys;

public static class HotkeyInputPolicy
{
    // Called after native IME/dropdown checks. A text control owns ordinary
    // typing, caret movement, word deletion and default clipboard shortcuts.
    // Explicit tool-local Enter/preview/navigation bindings remain routable.
    public static bool MayRoute(HotkeyBinding binding, HotkeyGesture gesture, bool textInput)
    {
        if(!textInput)return true;
        var modifiers=gesture.Modifiers;var key=gesture.NormalizedKey;
        if(modifiers.HasFlag(KeyModifiers.Control)&&modifiers.HasFlag(KeyModifiers.Alt))return false; // AltGr
        if((modifiers & ~KeyModifiers.Shift)==0 && (key=="SPACE"||key.Length==1||key.StartsWith("KP_",StringComparison.Ordinal)&&key.Length==4&&char.IsAsciiDigit(key[3])))return false;
        if(binding.Context==HotkeyContext.Default && modifiers.HasFlag(KeyModifiers.Control) && key is "A" or "C" or "X" or "V")return false;
        if(binding.Context is HotkeyContext.Default or HotkeyContext.Video or HotkeyContext.Audio
            && key is "LEFT" or "RIGHT" or "UP" or "DOWN" or "HOME" or "END" or "PAGEUP" or "PAGEDOWN" or "BACKSPACE" or "DELETE")return false;
        return true;
    }
}
