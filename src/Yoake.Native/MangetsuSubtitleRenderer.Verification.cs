using System.Runtime.InteropServices;

namespace Yoake.Native;

public sealed unsafe partial class MangetsuSubtitleRenderer
{
    // Only the packaged provider verifier uses this snapshot. Native tile pointers
    // remain here; normal playback never traverses or copies the RGBA tiles.
    // 0 = outside every tile, 1 = transparent in every covering tile, 2 = painted.
    internal byte[] CaptureCoverageForVerification(double seconds)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            var coverage = new byte[checked(_width * _height)];
            var changed = 0;
            var images = Native.ass_render_frame_rgba(_renderer, _track,
                (long)Math.Round(Math.Max(0, seconds) * 1000), &changed);
            try
            {
                for (var image = (ImageRgba*)images; image != null; image = image->Next)
                {
                    for (var y = Math.Max(0, image->Y); y < Math.Min(_height, image->Y + image->Height); y++)
                    {
                        var source = image->Rgba + (y - image->Y) * image->Stride;
                        for (var x = Math.Max(0, image->X); x < Math.Min(_width, image->X + image->Width); x++)
                        {
                            var index = y * _width + x;
                            var value = source[(x - image->X) * 4 + 3] == 0 ? (byte)1 : (byte)2;
                            coverage[index] = Math.Max(coverage[index], value);
                        }
                    }
                }
                return coverage;
            }
            finally { Native.ass_free_images_rgba(images); }
        }
    }

    // ASS_ImageRGBA prefix from libassmod's mangetsu branch, libass/ass.h.
    // Sequential layout retains the native pointer alignment on Windows x64.
    [StructLayout(LayoutKind.Sequential)]
    private struct ImageRgba
    {
        public int Width, Height, Stride;
        public byte* Rgba;
        public int X, Y, Type;
        public ImageRgba* Next;
    }
}
