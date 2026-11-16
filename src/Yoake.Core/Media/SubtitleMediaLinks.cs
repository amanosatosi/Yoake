using System.Globalization;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Media;

/// <summary>Lossless ASS project properties; provider stream indices are zero-based.</summary>
public sealed record SubtitleMediaLinks(string? Video, string? Audio, bool VideoSpecified, bool AudioSpecified, int? AudioStream, string? Timecodes = null, string? Keyframes = null)
{
    public const string Section = "[Aegisub Project Garbage]";
    public const string StreamKey = "Yoake Audio Stream";

    public static SubtitleMediaLinks Read(AssDocument document, string subtitlePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(subtitlePath))!;
        // Read all link fields in one pass even when a large script omits some.
        // Keep the first value, matching AssDocument.GetSectionValue semantics.
        var properties = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Video File", "Audio File", StreamKey, "Timecodes File", "Keyframes File" };
        var inside = false;
        foreach (var line in document.Source)
        {
            if (line.IsSectionHeader) { inside = line.Raw.Trim().Equals(Section,StringComparison.OrdinalIgnoreCase); continue; }
            if (!inside) continue;
            var colon = line.Raw.IndexOf(':'); if (colon <= 0) continue;
            var key = line.Raw[..colon].Trim();
            if (keys.Contains(key)) properties.TryAdd(key,line.Raw[(colon+1)..].Trim());
            if (properties.Count == keys.Count) break;
        }
        var videoSpecified = properties.TryGetValue("Video File",out var rawVideo);
        var video = Resolve(rawVideo ?? "",directory,true);
        var specified = properties.TryGetValue("Audio File",out var rawAudio);
        rawAudio ??= "";
        var audio = rawAudio.Trim().Trim('"').Equals("?video",StringComparison.OrdinalIgnoreCase)
            ? video?.StartsWith("?dummy:",StringComparison.OrdinalIgnoreCase) == false ? video : null
            : Resolve(rawAudio,directory,false);
        var stream = int.TryParse(properties.GetValueOrDefault(StreamKey),NumberStyles.None,CultureInfo.InvariantCulture,out var index) && index >= 0 ? (int?)index : null;
        return new(video,audio,videoSpecified,specified,stream,
            Resolve(properties.GetValueOrDefault("Timecodes File", ""),directory,false),
            Resolve(properties.GetValueOrDefault("Keyframes File", ""),directory,false));
    }

    private static string? Resolve(string reference,string directory,bool allowDummy)
    {
        reference = reference.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(reference)) return null;
        if (reference.StartsWith("?dummy:",StringComparison.OrdinalIgnoreCase)) return allowDummy ? reference : null;
        if (reference.StartsWith("?script",StringComparison.OrdinalIgnoreCase) &&
            (reference.Length == 7 || reference[7] is '/' or '\\')) reference = reference[7..].TrimStart('/','\\');
        else if (reference.StartsWith('?')) return null;
        try { return Path.GetFullPath(reference,directory); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    public static IReadOnlyDictionary<string,string> Capture(string? video,string? audio,int? audioStream) =>
        new Dictionary<string,string>
        {
            ["Video File"] = video ?? "",
            ["Audio File"] = audio ?? "",
            [StreamKey] = audio is not null && audioStream is >= 0 ? audioStream.Value.ToString(CultureInfo.InvariantCulture) : ""
        };
}
