namespace Yoake.Core.Hotkeys;

public static partial class AegisubHotkeyDefaults
{
    public static HotkeyOverride[] MigrateAudioStopOverrides(HotkeyOverride[]? overrides)
    {
        var source=(overrides??[]).OfType<HotkeyOverride>().ToArray();var result=new List<HotkeyOverride>();
        foreach(var item in source)
        {
            if(item.CommandId!="video/stop"||item.Context is not (HotkeyContext.Audio or HotkeyContext.Always))
            {result.Add(item);continue;}
            try
            {if(item.Gestures is null||item.Gestures.Any(text=>!HotkeyGesture.Parse(text).IsKnownKey)){result.Add(item);continue;}}
            catch(ArgumentException){result.Add(item);continue;}
            // The old audio-stop adapter shared video/stop's Audio/Always row.
            // Preserve its complete custom gesture set, including explicit empty
            // removals; suppress the old row so Escape is not silently restored.
            result.Add(item with {Gestures=[]});
            if(!source.Any(other=>other.Context==item.Context&&other.CommandId=="audio/stop"))
                result.Add(item with {CommandId="audio/stop"});
        }
        return result.ToArray();
    }
    // Preserve stable Yoake IDs for existing actions. New actions retain the
    // upstream semantic ID; their registration remains a separate backend task.
    public static string MapCommand(string command) => command switch
    {
        "edit/line/duplicate/shift" => "edit/line/split/before",
        "edit/line/duplicate/shift_back" => "edit/line/split/after",
        "edit/line/copy" => "grid/copy", "edit/line/cut" => "grid/cut", "edit/line/paste" => "grid/paste",
        "edit/line/delete" => "grid/delete", "grid/line/prev" => "grid/line/previous",
        "edit/line/comment" => "grid/toggle-comment",
        "grid/line/next/create" => "edit/commit-next", "subtitle/save/as" => "subtitle/save-as",
        "subtitle/select/all" => "grid/select-all", "subtitle/find" => "edit/find",
        "video/frame/prev" => "video/frame/previous",
        "video/frame/next/keyframe" => "video/frame/next-keyframe", "video/frame/prev/keyframe" => "video/frame/previous-keyframe",
        "video/jump/start" => "timing/jump-start", "video/jump/end" => "timing/jump-end",
        "video/tool/cross" => "video/tool/crosshair", "video/tool/drag" => "video/tool/position",
        "video/tool/rotate/z" => "video/tool/rotatez", "video/tool/rotate/xy" => "video/tool/rotatexy",
        "video/tool/vector_clip" => "video/tool/vectorclip",
        "video/tool/vclip/drag" => "video/vector/select", "video/tool/vclip/line" => "video/vector/line",
        "video/tool/vclip/bicubic" => "video/vector/bicubic", "video/tool/vclip/convert" => "video/vector/convert",
        "video/tool/vclip/insert" => "video/vector/insert", "video/tool/vclip/remove" => "video/vector/remove",
        "video/tool/vclip/freehand" => "video/vector/freehand", "video/tool/vclip/freehand_smooth" => "video/vector/smooth",
        "edit/color/primary" => "text/format/primary", "edit/color/secondary" => "text/format/secondary",
        "edit/color/outline" => "text/format/outline", "edit/color/shadow" => "text/format/shadow",
        _ => command,
    };

    public static IReadOnlyList<HotkeyBinding> ForYoake()
    {
        var bindings = ReferenceBindings.Select(binding => binding with { CommandId = MapCommand(binding.CommandId) }).ToList();
        void Add(string command, HotkeyContext context, string gesture) => bindings.Add(new(command, context, HotkeyGesture.Parse(gesture)));
        // Yoake additions occupy no shipped Aegisub context/gesture pair.
        Add("subtitle/close", HotkeyContext.Default, "Ctrl-W");
        Add("edit/redo", HotkeyContext.Default, "Ctrl-Shift-Z");
        Add("edit/commit", HotkeyContext.Default, "Ctrl-Enter");
        Add("edit/cancel", HotkeyContext.Default, "Escape");
        Add("text/format/bold", HotkeyContext.SubtitleEdit, "Ctrl-B");
        Add("text/format/italic", HotkeyContext.SubtitleEdit, "Ctrl-I");
        Add("text/format/underline", HotkeyContext.SubtitleEdit, "Ctrl-U");
        Add("text/insert/hard-newline", HotkeyContext.SubtitleEdit, "Shift-Enter");
        Add("edit/commit-next", HotkeyContext.SubtitleGrid, "Enter");
        Add("grid/insert/after", HotkeyContext.SubtitleGrid, "Insert");
        Add("grid/insert/before", HotkeyContext.SubtitleGrid, "Ctrl-Insert");
        Add("grid/delete", HotkeyContext.SubtitleGrid, "Delete");
        Add("grid/duplicate", HotkeyContext.Default, "Ctrl-Alt-D");
        Add("grid/toggle-comment", HotkeyContext.Default, "Alt-C");
        Add("grid/split", HotkeyContext.Default, "Ctrl-Alt-Shift-D");
        Add("grid/line/next", HotkeyContext.Default, "Alt-PageDown");
        Add("grid/line/previous", HotkeyContext.Default, "Alt-PageUp");
        Add("grid/move/up", HotkeyContext.Default, "Alt-Shift-Up");
        Add("grid/move/down", HotkeyContext.Default, "Alt-Shift-Down");
        foreach (var context in new[] { HotkeyContext.Video, HotkeyContext.SubtitleGrid })
        {
            Add("video/play", context, "Space"); Add("audio/play/selection", context, "R");
        }
        // Styling/Translation retain their shipped Home binding. The main
        // Video view gains a free context-local audition key; editor/grid Home
        // remains ordinary navigation.
        Add("video/play/line", HotkeyContext.Video, "Shift-Space");
        Add("video/stop", HotkeyContext.Video, "Escape");
        Add("video/stop", HotkeyContext.Audio, "Escape");
        Add("audio/zoom/in", HotkeyContext.Audio, "OemPlus");
        Add("audio/zoom/out", HotkeyContext.Audio, "OemMinus");
        return bindings.AsReadOnly();
    }
}
