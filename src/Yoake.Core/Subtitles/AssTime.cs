namespace Yoake.Core.Subtitles;

public static class AssTime
{
    public static bool TryParse(string? value, out long milliseconds)
    {
        milliseconds = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var parts = value.Trim().Split(':');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var hours) || !int.TryParse(parts[1], out var minutes))
            return false;
        var secondsParts = parts[2].Split('.');
        if (secondsParts.Length is < 1 or > 2 || !int.TryParse(secondsParts[0], out var seconds))
            return false;
        var fraction = 0;
        if (secondsParts.Length == 2)
        {
            var digits = secondsParts[1];
            if (digits.Length == 0 || digits.Length > 3 || !int.TryParse(digits, out fraction))
                return false;
            fraction *= digits.Length switch { 1 => 100, 2 => 10, _ => 1 };
        }
        if (hours < 0 || minutes is < 0 or > 59 || seconds is < 0 or > 59)
            return false;
        milliseconds = ((long)hours * 3600 + minutes * 60L + seconds) * 1000 + fraction;
        return true;
    }

    public static string Format(long milliseconds)
    {
        milliseconds = Math.Max(0, milliseconds);
        var hours = milliseconds / 3_600_000;
        milliseconds %= 3_600_000;
        var minutes = milliseconds / 60_000;
        milliseconds %= 60_000;
        var seconds = milliseconds / 1000;
        var centiseconds = (milliseconds % 1000) / 10;
        return $"{hours}:{minutes:00}:{seconds:00}.{centiseconds:00}";
    }
}
