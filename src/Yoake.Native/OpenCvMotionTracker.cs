using System.Runtime.InteropServices;
using Yoake.Core.Media;

namespace Yoake.Native;

public enum MotionModel { Position, PositionRotation, PositionScale, PositionScaleRotation }

// The provider owns the native ABI. The editor only sees managed frames and samples.
public static partial class OpenCvMotionTracker
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Marker { public double X, Y, Size, SearchSize, Rotation; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Result { public Marker Marker; public double Confidence; public int State; }

    public static unsafe TrackingPoint Track(DecodedVideoFrame from, DecodedVideoFrame to, TrackingRegion region,
        TrackingPoint previous, int frame, MotionModel model, double threshold, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (from.Width != to.Width || from.Height != to.Height || from.Stride < from.Width * 4 || to.Stride < to.Width * 4 ||
            from.Pixels.LongLength < (long)from.Stride * from.Height || to.Pixels.LongLength < (long)to.Stride * to.Height)
            throw new ArgumentException("Invalid or mismatched tracking frames.");
        var size = region.Width * previous.Scale;
        var marker = new Marker { X = previous.X, Y = previous.Y, Size = size, SearchSize = region.SearchWidth, Rotation = previous.Rotation };
        Result result;
        fixed (byte* source = from.Pixels, destination = to.Pixels)
        {
            if (TrackNative(source, from.Stride, destination, to.Stride, from.Width, from.Height, &marker, &marker,
                frame, (int)model, threshold, 1, 1, &result) == 0)
                throw new InvalidOperationException("OpenCV could not process the tracking frame.");
        }
        token.ThrowIfCancellationRequested();
        var confidence = result.State == 4 ? 0 : result.Confidence;
        return new(frame, result.Marker.X, result.Marker.Y, confidence, result.Marker.Size / region.Width, result.Marker.Rotation);
    }

    [LibraryImport("yoake-motion.dll", EntryPoint = "yoake_motion_track")]
    private static unsafe partial int TrackNative(byte* from, int fromPitch, byte* to, int toPitch, int width, int height,
        Marker* pattern, Marker* search, int frame, int mode, double threshold, int normalize, int prepass, Result* result);
}
