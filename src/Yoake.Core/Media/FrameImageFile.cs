using System.Globalization;

namespace Yoake.Core.Media;

/// <summary>Same-directory preparation followed by an explicit, guarded atomic publication.</summary>
public sealed class PreparedFrameImage : IDisposable
{
    private readonly string _temporary;
    public string Destination { get; }
    private PreparedFrameImage(string temporary, string destination) { _temporary = temporary; Destination = destination; }
    public static PreparedFrameImage Create(string destination, Action<Stream> encode, CancellationToken token)
    {
        var full = Path.GetFullPath(destination); var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            token.ThrowIfCancellationRequested();
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { encode(stream); token.ThrowIfCancellationRequested(); stream.Flush(true); }
            return new(temporary, full);
        }
        catch { if (File.Exists(temporary)) File.Delete(temporary); throw; }
    }
    public void Commit(Func<bool> isCurrent, CancellationToken token, bool overwrite = false)
    {
        token.ThrowIfCancellationRequested();
        if (!isCurrent()) throw new OperationCanceledException("The video frame changed before the PNG could be saved.");
        File.Move(_temporary, Destination, overwrite);
    }
    public void Dispose() { if (File.Exists(_temporary)) File.Delete(_temporary); }
}

public static class FrameImageFile
{
    public static string NextName(string source, bool dummy, string? script, int frame, string home)
    {
        var directory = dummy ? script is null ? home : Path.GetDirectoryName(Path.GetFullPath(script))! : Path.GetDirectoryName(Path.GetFullPath(source))!;
        var stem = dummy ? "dummy" : Path.GetFileNameWithoutExtension(source);
        for (var counter = 1; ; counter++)
        {
            var path = Path.Combine(directory, stem + "_" + counter.ToString("D3", CultureInfo.InvariantCulture) + "_" + frame.ToString(CultureInfo.InvariantCulture) + ".png");
            if (!File.Exists(path)) return path;
        }
    }
}
