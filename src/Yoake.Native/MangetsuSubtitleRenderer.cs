using System.Runtime.InteropServices;
using System.Text;

namespace Yoake.Native;

public sealed unsafe partial class MangetsuSubtitleRenderer : IDisposable
{
    private const int FontProviderAutodetect = 1;

    private readonly object _gate = new();
    private nint _library;
    private nint _renderer;
    private nint _track;
    private int _width;
    private int _height;
    private byte[] _alphaScratch = [];
    private bool _disposed;

    public MangetsuSubtitleRenderer(string assText, int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        _library = Native.ass_library_init();
        if (_library == 0)
            throw new InvalidOperationException("Mangetsu ass_library_init failed.");

        try
        {
            _renderer = Native.ass_renderer_init(_library);
            if (_renderer == 0)
                throw new InvalidOperationException("Mangetsu ass_renderer_init failed.");

            ConfigureSize(width, height);
            Native.ass_set_fonts(_renderer, 0, 0, FontProviderAutodetect, 0, 0);
            ReplaceTrackLocked(assText);
        }
        catch
        {
            CleanupLocked();
            throw;
        }
    }

    public void UpdateTrack(string assText)
    {
        ArgumentNullException.ThrowIfNull(assText);
        lock (_gate)
        {
            ThrowIfDisposed();
            ReplaceTrackLocked(assText);
        }
    }

    public void Composite(DecodedVideoFrame frame, double seconds)
    {
        ArgumentNullException.ThrowIfNull(frame);
        lock (_gate)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(frame.Pixels);
            if (frame.Width <= 0 || frame.Height <= 0)
                throw new ArgumentException("Video frame dimensions must be positive.", nameof(frame));
            var rowBytes = checked(frame.Width * 4);
            var pixelCount = checked(frame.Width * frame.Height);
            if (frame.Stride < rowBytes || frame.Pixels.LongLength < (long)(frame.Height - 1) * frame.Stride + rowBytes)
                throw new ArgumentException("Video frame buffer/stride does not contain the BGRA rows.", nameof(frame));
            if (_track == 0)
                return;
            if (frame.Width != _width || frame.Height != _height)
                ConfigureSize(frame.Width, frame.Height);

            var timestamp = (long)Math.Round(Math.Max(0, seconds) * 1000);
            var changed = 0;
            var images = Native.ass_render_frame_rgba(_renderer, _track, timestamp, &changed);
            if (images == 0)
                return;

            try
            {
                // Mangetsu composites RGB but writes zero to destination alpha
                // throughout each RGBA tile, including transparent source pixels.
                // Preserve the host frame's alpha exactly, as mpv's BGRA host does.
                // Grow only when needed; the renderer lock also owns this scratch.
                if (_alphaScratch.Length < pixelCount)
                    _alphaScratch = new byte[pixelCount];
                fixed (byte* destination = frame.Pixels)
                {
                    var alphaIndex = 0;
                    for (var y = 0; y < frame.Height; y++)
                    {
                        var row = destination + y * frame.Stride;
                        for (var x = 0; x < frame.Width; x++)
                            _alphaScratch[alphaIndex++] = row[x * 4 + 3];
                    }
                    try
                    {
                        if (Native.ass_composite_images_bgra(
                            images,
                            destination,
                            frame.Width,
                            frame.Height,
                            frame.Stride) != 0)
                        {
                            throw new InvalidOperationException("Mangetsu failed to composite subtitles into the video frame.");
                        }
                    }
                    finally
                    {
                        // Restore even on a reported compositor failure. RGB and
                        // row padding remain entirely under the native compositor.
                        alphaIndex = 0;
                        for (var y = 0; y < frame.Height; y++)
                        {
                            var row = destination + y * frame.Stride;
                            for (var x = 0; x < frame.Width; x++)
                                row[x * 4 + 3] = _alphaScratch[alphaIndex++];
                        }
                    }
                }
            }
            finally
            {
                Native.ass_free_images_rgba(images);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            CleanupLocked();
        }
    }

    private void ConfigureSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        Native.ass_set_storage_size(_renderer, width, height);
        Native.ass_set_frame_size(_renderer, width, height);
        _width = width;
        _height = height;
    }

    private void ReplaceTrackLocked(string assText)
    {
        var bytes = Encoding.UTF8.GetBytes(assText);
        nint replacement;
        fixed (byte* buffer = bytes)
        {
            replacement = Native.ass_read_memory(_library, buffer, (nuint)bytes.Length, "UTF-8");
        }
        if (replacement == 0)
            throw new InvalidOperationException("Mangetsu could not parse the active ASS document.");

        if (_track != 0)
            Native.ass_free_track(_track);
        _track = replacement;
    }

    private void CleanupLocked()
    {
        _alphaScratch = [];
        if (_track != 0)
        {
            Native.ass_free_track(_track);
            _track = 0;
        }
        if (_renderer != 0)
        {
            Native.ass_renderer_done(_renderer);
            _renderer = 0;
        }
        if (_library != 0)
        {
            Native.ass_library_done(_library);
            _library = 0;
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static partial class Native
    {
        private const string Library = "mangetsu.dll";

        [LibraryImport(Library, EntryPoint = "ass_library_init")]
        internal static partial nint ass_library_init();

        [LibraryImport(Library, EntryPoint = "ass_library_done")]
        internal static partial void ass_library_done(nint library);

        [LibraryImport(Library, EntryPoint = "ass_renderer_init")]
        internal static partial nint ass_renderer_init(nint library);

        [LibraryImport(Library, EntryPoint = "ass_renderer_done")]
        internal static partial void ass_renderer_done(nint renderer);

        [LibraryImport(Library, EntryPoint = "ass_set_storage_size")]
        internal static partial void ass_set_storage_size(nint renderer, int width, int height);

        [LibraryImport(Library, EntryPoint = "ass_set_frame_size")]
        internal static partial void ass_set_frame_size(nint renderer, int width, int height);

        [LibraryImport(Library, EntryPoint = "ass_set_fonts")]
        internal static partial void ass_set_fonts(
            nint renderer,
            nint defaultFont,
            nint defaultFamily,
            int fontProvider,
            nint config,
            int update);

        [LibraryImport(Library, EntryPoint = "ass_read_memory", StringMarshalling = StringMarshalling.Utf8)]
        internal static partial nint ass_read_memory(nint library, byte* buffer, nuint size, string codepage);

        [LibraryImport(Library, EntryPoint = "ass_free_track")]
        internal static partial void ass_free_track(nint track);

        [LibraryImport(Library, EntryPoint = "ass_render_frame_rgba")]
        internal static partial nint ass_render_frame_rgba(nint renderer, nint track, long now, int* detectChange);

        [LibraryImport(Library, EntryPoint = "ass_composite_images_bgra")]
        internal static partial int ass_composite_images_bgra(
            nint images,
            byte* destination,
            int width,
            int height,
            int stride);

        [LibraryImport(Library, EntryPoint = "ass_free_images_rgba")]
        internal static partial void ass_free_images_rgba(nint images);
    }
}
