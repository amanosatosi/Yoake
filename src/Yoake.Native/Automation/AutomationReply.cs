using System.Text;

namespace Yoake.Native.Automation;

// Data-only tuples for the already bundled luabins 0.3 decoder. See its
// saveload.h/write.c: byte tuple count, native little-endian int32 lengths and
// binary64 numbers. Windows x64 is the shipping provider ABI. No Lua code is
// generated or evaluated, and managed code never calls the Lua C API.
internal static class AutomationReply
{
    private static readonly byte[] EmptySuccess = [1, (byte)'1'];
    public static byte[] Encode(object?[] values, string? error = null)
    {
        if (error is null && values.Length == 0) return EmptySuccess;
        if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("Automation reply ABI requires little endian.");
        if (values.Length > 249) throw new ArgumentException("Too many Automation return values.");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((byte)(error is null ? values.Length + 1 : 2));
        writer.Write((byte)(error is null ? '1' : '0'));
        if (error is not null) Write(writer, error);
        else foreach (var value in values) Write(writer, value);
        return stream.ToArray();
    }

    private static void Header(BinaryWriter writer, int array, int hash)
    {
        writer.Write((byte)'T'); writer.Write(array); writer.Write(hash);
    }
    private static void Number(BinaryWriter writer, double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentException("Non-finite Automation response.");
        writer.Write((byte)'N'); writer.Write(value);
    }
    private static void Write(BinaryWriter writer, object? value)
    {
        switch (value)
        {
            case null: writer.Write((byte)'-'); break;
            case bool boolean: writer.Write((byte)(boolean ? '1' : '0')); break;
            case int number: Number(writer, number); break;
            case long number: Number(writer, number); break;
            case double number: Number(writer, number); break;
            case string text:
                var bytes = Encoding.UTF8.GetBytes(text);
                writer.Write((byte)'S'); writer.Write(bytes.Length); writer.Write(bytes); break;
            case IReadOnlyDictionary<string, object?> fields:
                Header(writer, 0, fields.Count);
                foreach (var pair in fields) { Write(writer, pair.Key); Write(writer, pair.Value); }
                break;
            case IReadOnlyDictionary<string, string> fields:
                Header(writer, 0, fields.Count);
                foreach (var pair in fields) { Write(writer, pair.Key); Write(writer, pair.Value); }
                break;
            case IReadOnlyDictionary<int, object?> fields:
                Header(writer, 0, fields.Count);
                foreach (var pair in fields) { Write(writer, pair.Key); Write(writer, pair.Value); }
                break;
            case IReadOnlyList<int> items:
                Header(writer, items.Count, 0);
                for (var i = 0; i < items.Count; i++) { Number(writer, i + 1); Write(writer, items[i]); }
                break;
            case IReadOnlyList<string> items:
                Header(writer, items.Count, 0);
                for (var i = 0; i < items.Count; i++) { Number(writer, i + 1); Write(writer, items[i]); }
                break;
            default: throw new ArgumentException("Unsupported Automation host response value.");
        }
    }
}
