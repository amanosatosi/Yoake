namespace Yoake.Core.Media;

/// <summary>A source stream index, independent of the displayed ordinal or subtitle track number.</summary>
public sealed record AudioTrackInfo(int Index, string Codec)
{
    public override string ToString() => $"Stream {Index + 1} — {Codec}";
}
