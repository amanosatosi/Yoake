namespace Yoake.Core.Subtitles;

/// <summary>An exclusively owned copy of source structure, with no native or UI state.</summary>
public sealed class AssDocumentSnapshot
{
    private readonly AssDocument _copy;
    private readonly object _gate = new();
    public long Revision { get; }
    internal AssDocumentSnapshot(AssDocument copy, long revision) { _copy = copy; Revision = revision; }
    public byte[] SerializeBytes() { lock (_gate) return _copy.SerializeBytes(); }
}
