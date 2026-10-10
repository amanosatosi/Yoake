using Avalonia.Input;

namespace Yoake.UI.Commands;

public static class EditorKeyNames
{
    // Enter and Return have the same enum value. Enum.ToString() is free to
    // choose either alias, whereas our stable hotkey vocabulary uses Enter.
    public static string FromKey(Key key)=>key==Key.Enter?"Enter":key.ToString();
}
