using System.Text;

namespace Yoake.UI;

// An Explorer drop is classified before opening any resource. A malformed or
// ambiguous batch never partially replaces the user's open media or scripts.
public enum DroppedFileType { Subtitle, Video, Audio, Timecodes, Keyframes }
public sealed record DroppedFile(string Path, DroppedFileType Type);

public static class FileDropPlanner
{
    private static readonly HashSet<string> Subtitle = new(StringComparer.OrdinalIgnoreCase) { ".ass", ".ssa" };
    private static readonly HashSet<string> Video = new(StringComparer.OrdinalIgnoreCase)
        { ".mkv", ".mp4", ".webm", ".avi", ".mov", ".m2ts", ".ts", ".wmv", ".mpeg", ".mpg", ".y4m" };
    private static readonly HashSet<string> Audio = new(StringComparer.OrdinalIgnoreCase)
        { ".wav", ".flac", ".mp3", ".aac", ".m4a", ".opus", ".ogg" };
    private static readonly HashSet<string> Metadata = new(StringComparer.OrdinalIgnoreCase)
        { ".txt", ".tc", ".timecodes", ".log", ".pass", ".stats" };

    public static bool IsCandidate(string path)
    {
        var ext = Path.GetExtension(path);
        return Subtitle.Contains(ext) || Video.Contains(ext) || Audio.Contains(ext) || Metadata.Contains(ext);
    }

    public static bool TryBuild(IEnumerable<string> paths, out DroppedFile[] files, out string error)
    {
        files = []; error = "";
        var classified = new List<DroppedFile>();
        try
        {
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path)) { error = "Only local files can be dropped."; return false; }
                var full = Path.GetFullPath(path);
                if (!File.Exists(full)) { error = "Not a readable local file: " + full; return false; }
                var ext = Path.GetExtension(full);
                DroppedFileType kind;
                if (Subtitle.Contains(ext)) kind = DroppedFileType.Subtitle;
                else if (Video.Contains(ext)) kind = DroppedFileType.Video;
                else if (Audio.Contains(ext)) kind = DroppedFileType.Audio;
                else if (Metadata.Contains(ext))
                {
                    // .txt is shared by timecodes, keyframes and ordinary notes.
                    // Use the release format headers, never filename guesses.
                    using var reader = new StreamReader(full, new UTF8Encoding(false, true), true);
                    var header = (reader.ReadLine() ?? "").TrimStart('\uFEFF').Trim();
                    if (header is "# timecode format v1" or "# timecode format v2" ||
                        header.StartsWith("Assume ", StringComparison.Ordinal)) kind = DroppedFileType.Timecodes;
                    else if (header is "# keyframe format v1" ||
                        header.StartsWith("# XviD 2pass stat file", StringComparison.Ordinal) ||
                        header.StartsWith("# ffmpeg 2-pass log file, using xvid codec", StringComparison.Ordinal) ||
                        header.StartsWith("# avconv 2-pass log file, using xvid codec", StringComparison.Ordinal) ||
                        header.StartsWith("##map version", StringComparison.Ordinal) ||
                        header.StartsWith("#options:", StringComparison.Ordinal) ||
                        header.StartsWith("# WWXD log file, using qpfile format", StringComparison.Ordinal))
                        kind = DroppedFileType.Keyframes;
                    else { error = "Unrecognized timecode/keyframe file: " + Path.GetFileName(full); return false; }
                }
                else { error = "Unsupported dropped file: " + Path.GetFileName(full); return false; }
                if (!classified.Any(f => string.Equals(f.Path, full, OperatingSystem.IsWindows() ?
                        StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
                    classified.Add(new(full, kind));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            error = "Cannot read dropped files: " + ex.Message; return false;
        }
        if (classified.Count == 0) { error = "Drop one or more local files."; return false; }
        foreach (var type in new[] { DroppedFileType.Video, DroppedFileType.Audio, DroppedFileType.Timecodes, DroppedFileType.Keyframes })
            if (classified.Count(f => f.Type == type) > 1)
            { error = "Drop only one " + type + " file at a time."; return false; }
        if (classified.Any(f => f.Type == DroppedFileType.Video) && classified.Any(f => f.Type == DroppedFileType.Audio))
        {
            error = "Drop the video and replacement audio separately. A video drop always replaces both tracks.";
            return false;
        }
        // Subtitles become individual tabs; any media/metadata goes to the
        // last opened subtitle tab, regardless of Explorer's file ordering.
        files = classified.OrderBy(f => f.Type).ToArray();
        return true;
    }
}
