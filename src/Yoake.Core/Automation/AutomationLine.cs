using System.Globalization;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Automation;

// Detached Lua-table data. Native provider details never enter this model.
public sealed class AutomationLine
{
    public Dictionary<string, object?> Fields { get; } = new(StringComparer.Ordinal);
    internal AssRecord? Template { get; init; }
    public object? this[string field] { get => Fields.GetValueOrDefault(field); set => Fields[field] = value; }

    public AutomationLine Copy()
    {
        var copy = new AutomationLine { Template = Template };
        foreach (var pair in Fields)
            copy.Fields[pair.Key] = pair.Value is IReadOnlyDictionary<string, string> extra
                ? new Dictionary<string, string>(extra, StringComparer.Ordinal) : pair.Value;
        return copy;
    }

    internal string String(string key)
    {
        var value = this[key];
        return value switch
        {
            string text => text,
            int number => number.ToString(CultureInfo.InvariantCulture),
            long number => number.ToString(CultureInfo.InvariantCulture),
            double number when double.IsFinite(number) => number.ToString("G14", CultureInfo.InvariantCulture),
            _ => throw BadField(key, "string")
        };
    }

    internal double Number(string key)
    {
        var value = this[key];
        double number = value switch
        {
            int n => n, long n => n, double n => n,
            string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) => n,
            _ => throw BadField(key, "number")
        };
        if (!double.IsFinite(number)) throw BadField(key, "finite number");
        return number;
    }

    internal int Integer(string key)
    {
        var number = Math.Truncate(Number(key));
        if (number < int.MinValue || number > int.MaxValue) throw BadField(key, "32-bit integer");
        return (int)number;
    }

    internal bool Boolean(string key) => this[key] is bool b ? b : throw BadField(key, "boolean");
    private ArgumentException BadField(string key, string type) =>
        new($"Invalid or missing field '{key}' in '{this["class"]}' class subtitle line (expected {type}).");
}
