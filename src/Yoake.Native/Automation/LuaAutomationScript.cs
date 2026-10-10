using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Yoake.Core.Automation;

namespace Yoake.Native.Automation;

internal sealed partial class LuaAutomationScript : IAutomationScript
{
    private readonly AutomationPathResolver _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<AutomationMacro> _macros = [];
    private readonly List<AutomationExportFilter> _filters = [];
    private readonly Dictionary<int, AutomationLine> _reads = [];
    private readonly Dictionary<(string Class, string Raw), AutomationLine> _templates = [];
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);
    private GCHandle _owner;
    private IntPtr _runtime;
    private AutomationInvocation? _invocation;
    private string? _mode;
    private CancellationToken _cancellation;
    private bool _disposed;
    private int _nextRead;
    public string Path { get; }
    public AutomationScriptMetadata Metadata { get; private set; }
    public IReadOnlyList<AutomationMacro> Macros => _macros;
    public IReadOnlyList<AutomationExportFilter> Filters => _filters;

    public unsafe LuaAutomationScript(string path, AutomationPathResolver paths)
    {
        Path = path; _paths = paths;
        Metadata = new(System.IO.Path.GetFileName(path), "", "", "");
        _owner = GCHandle.Alloc(this);
        try
        {
            _runtime = Create((IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int, IntPtr>)&Dispatch, GCHandle.ToIntPtr(_owner));
            if (_runtime == IntPtr.Zero) throw new InvalidOperationException("Could not create the native LuaJIT interpreter.");
        }
        catch { _owner.Free(); throw; }
    }

    public void Initialize(string adapter, CancellationToken cancellationToken)
    {
        _cancellation = cancellationToken;
        Run(adapter, "@Yoake/automation/host.lua", cancellationToken);
        // The adapter's loader handles Unicode paths and .moon without relying
        // on process CWD or Lua's ANSI filesystem functions.
        Run($"include({Literal(Path)})", "@" + Path, cancellationToken);
        using var result = JsonDocument.Parse(Run("return __yoake_metadata()", "=metadata", cancellationToken) ?? "{}");
        var root = result.RootElement;
        string Field(string key) => root.TryGetProperty(key, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number ? v.ToString() : "";
        var name = Field("name");
        Metadata = new(name.Length == 0 ? System.IO.Path.GetFileName(Path) : name, Field("description"), Field("author"), Field("version"));
    }

    public async ValueTask<AutomationMacroResult> RunAsync(int macroIndex, AutomationInvocation invocation, CancellationToken cancellationToken)
    {
        var text = await InvokeAsync(macroIndex, "run", invocation, cancellationToken).ConfigureAwait(false);
        using var json = JsonDocument.Parse(text);
        IReadOnlyList<int>? selection = null; int? active = null;
        if (json.RootElement.TryGetProperty("first", out var first) && first.ValueKind == JsonValueKind.Object)
            selection = first.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Number).Select(p => Integer(p.Value)).ToArray();
        if (json.RootElement.TryGetProperty("second", out var second) && second.ValueKind == JsonValueKind.Number) active = Integer(second);
        return new(selection, active);
    }

    public async ValueTask<AutomationValidation> ValidateAsync(int macroIndex, AutomationInvocation invocation, CancellationToken cancellationToken)
    {
        var macro = _macros.Single(m => m.Index == macroIndex);
        bool enabled = true, active = false; string? help = null;
        if (macro.HasValidation)
        {
            using var json = JsonDocument.Parse(await InvokeAsync(macroIndex, "validate", invocation, cancellationToken).ConfigureAwait(false));
            enabled = json.RootElement.TryGetProperty("first", out var first) && first.ValueKind is not JsonValueKind.Null and not JsonValueKind.False;
            if (json.RootElement.TryGetProperty("second", out var second) && second.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                help = second.ToString();
        }
        if (macro.HasToggle)
        {
            using var json = JsonDocument.Parse(await InvokeAsync(macroIndex, "isactive", invocation, cancellationToken).ConfigureAwait(false));
            active = json.RootElement.TryGetProperty("first", out var first) && first.ValueKind is not JsonValueKind.Null and not JsonValueKind.False;
        }
        return new(enabled, help, active);
    }

    public async ValueTask<IReadOnlyList<AutomationLine>> ConfigureFilterAsync(int filterIndex, AutomationInvocation invocation, CancellationToken cancellationToken)
    {
        if (!_filters.Single(f => f.Index == filterIndex).HasConfiguration) return [];
        using var json = JsonDocument.Parse(await InvokeAsync(filterIndex, "config", invocation, cancellationToken).ConfigureAwait(false));
        if (!json.RootElement.TryGetProperty("first", out var controls) || controls.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Cannot create export configuration from a non-table value.");
        return Values(controls).Select(v => Line(v, default)).ToArray();
    }

    public async ValueTask RunFilterAsync(int filterIndex, AutomationInvocation invocation, IReadOnlyDictionary<string, object?> settings, CancellationToken cancellationToken)
    {
        _ = _filters.Single(f => f.Index == filterIndex);
        await InvokeAsync(filterIndex, "run", invocation, cancellationToken, settings).ConfigureAwait(false);
    }

    private async ValueTask<string> InvokeAsync(int index, string method, AutomationInvocation invocation, CancellationToken cancellationToken, IReadOnlyDictionary<string, object?>? settings = null)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _mode = method == "run" && _filters.Any(f => f.Index == index) ? "filter" : method;
            _invocation = invocation; _cancellation = cancellationToken; _reads.Clear(); _templates.Clear(); _nextRead = 0;
            var result = await Task.Run(() => Run($"return __yoake_invoke({index},{Literal(method)},{Literal(settings)})", "@" + Path, cancellationToken) ?? "{}", cancellationToken).ConfigureAwait(false);
            using var json = JsonDocument.Parse(result);
            if (json.RootElement.TryGetProperty("cancelled", out var cancelled) && cancelled.ValueKind == JsonValueKind.True)
                throw new OperationCanceledException("Automation script cancelled execution.");
            return result;
        }
        finally { _invocation = null; _mode = null; _reads.Clear(); _templates.Clear(); _gate.Release(); }
    }

    private string? Run(string source, string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ResetCancellation(_runtime);
        using var registration = cancellationToken.Register(() => Cancel(_runtime));
        var bytes = Encoding.UTF8.GetBytes(source);
        var status = Execute(_runtime, bytes, bytes.Length, name, out var pointer);
        var result = pointer == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(pointer);
        cancellationToken.ThrowIfCancellationRequested();
        if (status != 0) throw new InvalidOperationException($"Automation script '{Path}' failed:\n{result}");
        return result;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe IntPtr Dispatch(IntPtr owner, IntPtr request, int length)
    {
        string response;
        try
        {
            var script = (LuaAutomationScript)GCHandle.FromIntPtr(owner).Target!;
            using var json = JsonDocument.Parse(new ReadOnlySpan<byte>((void*)request, length).ToArray());
            var root = json.RootElement;
            var values = script.Handle(root.GetProperty("op").GetString()!, root.GetProperty("args"));
            response = "return {ok=true,values={n=" + values.Length + "," + string.Join(',', values.Select((v, i) => "[" + (i + 1) + "]=" + Literal(v))) + "}}";
        }
        catch (Exception exception) { response = "return {ok=false,error=" + Literal(exception.Message) + "}"; }
        try { return Marshal.StringToCoTaskMemUTF8(response); }
        catch { return IntPtr.Zero; } // Never throw across the native callback.
    }

    private object?[] Handle(string op, JsonElement args)
    {
        JsonElement Arg(int i) => args.TryGetProperty(i.ToString(CultureInfo.InvariantCulture), out var v) ? v : default;
        string Text(int i) => Arg(i).ValueKind is JsonValueKind.String or JsonValueKind.Number ? Arg(i).ToString() : throw new ArgumentException("Expected a string argument.");
        int Index(int i) => Integer(Arg(i));
        var subs = _invocation?.Subtitles;
        var services = _invocation?.Services;
        if (op.StartsWith("subs_", StringComparison.Ordinal) || op == "undo_point")
            if (subs is null) throw new InvalidOperationException("No active Automation subtitle context.");
        if (op is "subs_write" or "subs_delete" or "subs_deleterange" or "subs_append" or "subs_insert" or "undo_point")
            if (_mode is not ("run" or "filter")) throw new InvalidOperationException("Subtitles are read-only during validation and export configuration.");
        if (op == "undo_point" && _mode == "filter") throw new InvalidOperationException("Export filters cannot set undo points.");
        if ((op == "dialog" || op.StartsWith("file_dialog_", StringComparison.Ordinal)) && _mode != "run") throw new InvalidOperationException("This Automation invocation cannot open a dialog.");
        switch (op)
        {
            case "read_file": return [File.ReadAllText(Text(1), new UTF8Encoding(false, true)).TrimStart('\uFEFF')];
            case "include_path": return [_paths.ResolveInclude(Text(1))];
            case "package_path": return [_paths.PackagePath];
            case "module_path": return [_paths.ResolveModule(Text(1), Text(2))];
            case "decode_path": return [services?.DecodePath(Text(1)) ?? _paths.DecodePath(Text(1))];
            case "register_macro":
                var name = Text(2);
                if (!_names.Add(name)) throw new ArgumentException($"A macro named '{name}' is already defined in script '{Path}'.");
                _macros.Add(new(Index(1), name, Text(3), Arg(4).GetBoolean(), Arg(5).GetBoolean())); return [];
            case "register_filter": _filters.Add(new(Index(1), Text(2), Text(3), Index(6), Arg(4).GetBoolean())); return [];
            case "selection": return [_invocation!.SelectedLines, _invocation.ActiveLine];
            case "subs_count": return [subs!.Count];
            case "subs_read":
                var line = subs!.Read(Index(1)); var id = ++_nextRead; _reads[id] = line;
                if (line["class"] is string kind && line["raw"] is string raw) _templates.TryAdd((kind, raw), line);
                return [line.Fields, id];
            case "subs_write":
                subs!.Write(Index(1), Arg(2).ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : Line(Arg(2), Arg(3))); return [];
            case "subs_delete":
                var indexes = Arg(1).ValueKind == JsonValueKind.Object ? Values(Arg(1)).Select(Integer) : Values(args).Select(Integer);
                subs!.Delete(indexes.ToArray()); return [];
            case "subs_deleterange": subs!.DeleteRange(Index(1), Index(2)); return [];
            case "subs_append": case "subs_insert":
                var table = Arg(1); var provenance = Arg(2); var count = table.GetProperty("n").GetInt32();
                var offset = op == "subs_insert" ? 2 : 1;
                List<AutomationLine> lines = [];
                for (var i = offset; i <= count; i++)
                {
                    var key = i.ToString(CultureInfo.InvariantCulture);
                    lines.Add(Line(table.GetProperty(key), provenance.TryGetProperty(key, out var token) ? token : default));
                }
                if (op == "subs_insert") subs!.Insert(Integer(table.GetProperty("1")), lines.ToArray());
                else subs!.Append(lines.ToArray());
                return [];
            case "undo_point": subs!.SetUndoPoint(Text(1)); return [];
            case "karaoke": return [AutomationKaraokeParser.Parse(Line(Arg(1), default))
                .ToDictionary(p => int.Parse(p.Key, CultureInfo.InvariantCulture), p => p.Value)];
            case "is_cancelled": return [_cancellation.IsCancellationRequested];
            case "progress": services!.ReportProgress(NullableNumber(Arg(1)), NullableText(Arg(2)), NullableText(Arg(3))); return [];
            case "log": services!.Log(Text(1), Index(2)); return [];
            case "file_name": return [services?.FileName];
            case "project_properties": return [services?.ProjectProperties];
            case "gettext": return [services?.Translate(Text(1)) ?? Text(1)];
            case "frame_from_ms": return [services?.FrameFromMilliseconds(Index(1))];
            case "ms_from_frame": return [services?.MillisecondsFromFrame(Index(1))];
            case "keyframes": return [services?.Keyframes];
            case "video_size":
                return services?.VideoSize is { } size ? [size.Width, size.Height, size.AspectRatio, size.AspectRatioMode] : [null];
            case "text_extents":
                var metrics = services!.MeasureText(Line(Arg(1), default), Text(2));
                return [metrics.Width, metrics.Height, metrics.Descent, metrics.ExternalLeading];
            case "clipboard_get": return [services!.ClipboardGet()];
            case "clipboard_set": return [services!.ClipboardSet(Text(1))];
            case "file_dialog_open": case "file_dialog_save":
                static bool Truth(JsonElement v) => v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined or JsonValueKind.False);
                var save = op == "file_dialog_save";
                var multiple = !save && Truth(Arg(5));
                var mustExist = !save && (Arg(6).ValueKind is JsonValueKind.Null or JsonValueKind.Undefined || Truth(Arg(6)));
                var paths = services!.PickFiles(new(Text(1), Text(2), Text(3), Text(4), save, multiple, mustExist, save && !Truth(Arg(5))), _cancellation);
                return [multiple ? paths : paths?.FirstOrDefault() as object];
            case "dialog":
                var controls = Values(Arg(1)).Select(v => Line(v, default)).ToArray();
                var buttons = Arg(2).ValueKind == JsonValueKind.Object ? Values(Arg(2)).Select(v => v.ToString()).ToArray() : null;
                var ids = Arg(3).ValueKind == JsonValueKind.Object ? Arg(3).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ToString()) : null;
                var dialog = services!.DisplayDialog(new(controls, buttons, ids), _cancellation);
                return [dialog.Button, dialog.Values];
            default: throw new NotSupportedException($"Automation API '{op}' is not implemented yet.");
        }
    }

    private AutomationLine Line(JsonElement table, JsonElement token)
    {
        if (table.ValueKind != JsonValueKind.Object) throw new ArgumentException("Can't convert a non-table value to AssEntry.");
        AutomationLine? template = null;
        if (token.ValueKind == JsonValueKind.Number) _reads.TryGetValue(Integer(token), out template);
        // Unmodified utils.table.copy does not copy a weak-table identity. The
        // exact raw source field survives it and carries the source schema and
        // unknown fields into generated copies without leaking private fields.
        if (template is null && table.TryGetProperty("class", out var kind) && kind.ValueKind == JsonValueKind.String &&
            table.TryGetProperty("raw", out var raw) && raw.ValueKind == JsonValueKind.String)
            _templates.TryGetValue((kind.GetString()!, raw.GetString()!), out template);
        var line = template?.Copy() ?? new AutomationLine();
        line.Fields.Clear();
        foreach (var pair in table.EnumerateObject())
            line[pair.Name] = pair.Value.ValueKind switch
            {
                JsonValueKind.String => pair.Value.GetString(), JsonValueKind.Number => pair.Value.GetDouble(),
                JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.Null => null,
                JsonValueKind.Object when pair.Name == "extra" => pair.Value.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ToString()),
                JsonValueKind.Object => Data(pair.Value),
                _ => throw new ArgumentException($"Unsupported field '{pair.Name}'.")
            };
        return line;
    }

    private static IEnumerable<JsonElement> Values(JsonElement table) => table.EnumerateObject()
        .Where(p => p.Name != "n").OrderBy(p => int.TryParse(p.Name, out var i) ? i : int.MaxValue).Select(p => p.Value);
    private static object? Data(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().ToDictionary(p => p.Name, p => Data(p.Value), StringComparer.Ordinal),
        JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.Null => null,
        _ => throw new ArgumentException("Unsupported Automation data value.")
    };
    private static int Integer(JsonElement element)
    {
        double n;
        if (element.ValueKind == JsonValueKind.Number) n = element.GetDouble();
        else if (element.ValueKind == JsonValueKind.String && double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) n = parsed;
        else throw new ArgumentException("Expected numeric Automation argument.");
        if (!double.IsFinite(n) || n < int.MinValue || n > int.MaxValue) throw new ArgumentException("Automation integer out of range.");
        return (int)n;
    }
    private static double? NullableNumber(JsonElement v) => v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    private static string? NullableText(JsonElement v) => v.ValueKind is JsonValueKind.String or JsonValueKind.Number ? v.ToString() : null;

    // Data-only Lua literals. Strings are byte-escaped; input cannot become code.
    private static string Literal(object? value)
    {
        switch (value)
        {
            case null: return "nil";
            case string text:
                var output = new StringBuilder("\"");
                foreach (var b in Encoding.UTF8.GetBytes(text)) output.Append('\\').Append(b.ToString("D3", CultureInfo.InvariantCulture));
                return output.Append('"').ToString();
            case bool b: return b ? "true" : "false";
            case int n: return n.ToString(CultureInfo.InvariantCulture);
            case long n: return n.ToString(CultureInfo.InvariantCulture);
            case double n when double.IsFinite(n): return n.ToString("G17", CultureInfo.InvariantCulture);
            case IReadOnlyDictionary<string, object?> fields: return "{" + string.Join(',', fields.Select(p => "[" + Literal(p.Key) + "]=" + Literal(p.Value))) + "}";
            case IReadOnlyDictionary<string, string> fields: return "{" + string.Join(',', fields.Select(p => "[" + Literal(p.Key) + "]=" + Literal(p.Value))) + "}";
            case IReadOnlyDictionary<int, object?> fields: return "{" + string.Join(',', fields.Select(p => "[" + Literal(p.Key) + "]=" + Literal(p.Value))) + "}";
            case IReadOnlyList<int> array: return "{" + string.Join(',', array.Select(n => Literal(n))) + "}";
            case IReadOnlyList<string> array: return "{" + string.Join(',', array.Select(n => Literal(n))) + "}";
            default: throw new ArgumentException("Unsupported Automation host response value.");
        }
    }

    public void Dispose()
    {
        _gate.Wait();
        try
        {
            if (_disposed) return;
            _disposed = true; Destroy(_runtime); _runtime = IntPtr.Zero;
            if (_owner.IsAllocated) _owner.Free();
            _reads.Clear(); _templates.Clear(); _macros.Clear(); _filters.Clear();
        }
        finally { _gate.Release(); }
    }

    [LibraryImport("yoake-automation.dll", EntryPoint = "ya_create")]
    private static partial IntPtr Create(IntPtr callback, IntPtr context);
    [LibraryImport("yoake-automation.dll", EntryPoint = "ya_execute", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Execute(IntPtr runtime, byte[] source, int length, string name, out IntPtr result);
    [LibraryImport("yoake-automation.dll", EntryPoint = "ya_cancel")]
    private static partial void Cancel(IntPtr runtime);
    [LibraryImport("yoake-automation.dll", EntryPoint = "ya_reset_cancellation")]
    private static partial void ResetCancellation(IntPtr runtime);
    [LibraryImport("yoake-automation.dll", EntryPoint = "ya_destroy")]
    private static partial void Destroy(IntPtr runtime);
}
