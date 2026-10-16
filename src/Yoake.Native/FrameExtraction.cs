using Yoake.Core.Media;

namespace Yoake.Native;

public static class FrameExtraction
{
    public static DecodedVideoFrame Extract(IVideoProvider provider, FrameCaptureIdentity capture, FrameImageKind kind,
        Func<DecodedVideoFrame, double, DecodedVideoFrame> render, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!ReferenceEquals(provider, capture.Provider)) throw new ArgumentException("Frame capture belongs to another provider.");
        var info = provider.SourceInfo;
        var frame = kind == FrameImageKind.Subtitles
            ? new DecodedVideoFrame(info.Width, info.Height, checked(info.Width * 4), new byte[checked(info.Width * info.Height * 4)])
            : provider.GetFrame(capture.Frame);
        token.ThrowIfCancellationRequested();
        if (kind != FrameImageKind.Raw) frame = render(frame, capture.Seconds);
        token.ThrowIfCancellationRequested();
        return frame;
    }
}
