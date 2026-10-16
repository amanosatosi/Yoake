using Avalonia.Input;
using Yoake.Core.Hotkeys;

namespace Yoake.UI.Commands;

public static class EditorKeyNames
{
    // Enter and Return have the same enum value. Enum.ToString() is free to
    // choose either alias, whereas our stable hotkey vocabulary uses Enter.
    public static string FromKey(Key key)
    {
        var name=key switch
        {
            Key.Enter=>"Enter",Key.Back=>"Backspace",Key.PageUp=>"PageUp",Key.PageDown=>"PageDown",
            Key.CapsLock=>"CapsLock",Key.PrintScreen=>"PrintScreen",
            Key.Oem1=>"Oem1",Key.Oem2=>"Oem2",Key.Oem3=>"Oem3",Key.Oem4=>"Oem4",Key.Oem5=>"Oem5",
            Key.Oem6=>"Oem6",Key.Oem7=>"Oem7",Key.Oem102=>"Oem102",_=>key.ToString(),
        };
        var gesture=new HotkeyGesture(name);
        return gesture.Format();
    }
    public static string FromEvent(KeyEventArgs args)=>args.PhysicalKey==PhysicalKey.NumPadEnter?"KP_Enter":FromKey(args.Key);
    public static Yoake.Core.Hotkeys.KeyModifiers FromModifiers(Avalonia.Input.KeyModifiers modifiers)
    {
        var result=Yoake.Core.Hotkeys.KeyModifiers.None;
        if(modifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))result|=Yoake.Core.Hotkeys.KeyModifiers.Control;
        if(modifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt))result|=Yoake.Core.Hotkeys.KeyModifiers.Alt;
        if(modifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift))result|=Yoake.Core.Hotkeys.KeyModifiers.Shift;
        if(modifiers.HasFlag(Avalonia.Input.KeyModifiers.Meta))result|=Yoake.Core.Hotkeys.KeyModifiers.Meta;
        return result;
    }

    public static KeyGesture? ToGesture(HotkeyGesture gesture)
    {
        var key=gesture.NormalizedKey;
        if(key=="BACKSPACE")key="Back";
        if(key.Length==1&&char.IsAsciiDigit(key[0]))key="D"+key;
        else if(key.StartsWith("KP_",StringComparison.Ordinal))key=key[3..] switch
        {
            "ENTER"=>"Enter", "ADD"=>"Add", "SUBTRACT"=>"Subtract", "MULTIPLY"=>"Multiply", "DIVIDE"=>"Divide", "DECIMAL"=>"Decimal",
            var value when value.Length==1&&char.IsAsciiDigit(value[0])=>"NumPad"+value,
            var value=>value,
        };
        if(!Enum.TryParse<Key>(key,true,out var parsed))return null;
        var modifiers=Avalonia.Input.KeyModifiers.None;
        if(gesture.Modifiers.HasFlag(Yoake.Core.Hotkeys.KeyModifiers.Control))modifiers|=Avalonia.Input.KeyModifiers.Control;
        if(gesture.Modifiers.HasFlag(Yoake.Core.Hotkeys.KeyModifiers.Alt))modifiers|=Avalonia.Input.KeyModifiers.Alt;
        if(gesture.Modifiers.HasFlag(Yoake.Core.Hotkeys.KeyModifiers.Shift))modifiers|=Avalonia.Input.KeyModifiers.Shift;
        if(gesture.Modifiers.HasFlag(Yoake.Core.Hotkeys.KeyModifiers.Meta))modifiers|=Avalonia.Input.KeyModifiers.Meta;
        return new(parsed,modifiers);
    }
}
