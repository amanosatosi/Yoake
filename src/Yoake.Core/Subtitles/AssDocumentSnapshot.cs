namespace Yoake.Core.Subtitles;

/// <summary>An exclusively owned copy of source structure, with no native or UI state.</summary>
public sealed class AssDocumentSnapshot
{
    private readonly AssDocument _copy;
    private readonly object _gate = new();
    public long Revision { get; }
    internal AssDocumentSnapshot(AssDocument copy, long revision) { _copy = copy; Revision = revision; }
    public int? OriginalCodePage=>SubtitleEncodings.IsUnicode(_copy.EncodingCodePage)?null:_copy.EncodingCodePage;
    internal byte[] SerializeRecoveryBytes()
    {
        lock(_gate)return OriginalCodePage is null?_copy.SerializeBytes():new System.Text.UTF8Encoding(false,true).GetBytes(_copy.Serialize());
    }
    public byte[] SerializeBytes() { lock (_gate) return _copy.SerializeBytes(); }
    public AssAttachment[] GetAttachments(CancellationToken token=default){lock(_gate)return _copy.GetAttachments(token);}
}
