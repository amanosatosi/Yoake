namespace Yoake.Core.Media;

public enum FrameImageKind { Full, Raw, Subtitles }

/// <summary>Captured source identity; presentation transforms never enter extraction.</summary>
public sealed record FrameCaptureIdentity(object Provider, object FrameMap, int Frame, double Seconds, long PreviewRevision)
{
    public bool Matches(object? provider, object frameMap, int frame, long revision) =>
        ReferenceEquals(Provider, provider) && ReferenceEquals(FrameMap, frameMap) && Frame == frame && PreviewRevision == revision;
}
