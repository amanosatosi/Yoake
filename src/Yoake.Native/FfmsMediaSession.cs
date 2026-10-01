using System.Runtime.InteropServices;

namespace Yoake.Native;

public sealed record MediaInfo(
    string Path,
    int Width,
    int Height,
    int FrameCount,
    double FramesPerSecond,
    double DurationSeconds,
    int SampleRate,
    int Channels,
    long AudioSamples);

public sealed record DecodedVideoFrame(int Width, int Height, int Stride, byte[] Pixels);

public sealed unsafe partial class FfmsMediaSession : IDisposable
{
    private const int VideoTrackType = 0;
    private const int AudioTrackType = 1;
    private const int IndexTrack = 1;
    private const int IndexErrorHandlingClearTrack = 1;
    private const int SeekNormal = 1;
    private const int DelayTimeZero = -2;
    private const int ResizerBicubic = 0x0004;
    private const int SampleFormatFloat = 3;
    private const int ErrorBufferSize = 2048;

    private static readonly object InitGate = new();
    private static bool _initialized;

    private readonly object _videoGate = new();
    private readonly object _audioGate = new();
    private nint _videoSource;
    private nint _audioSource;
    private bool _disposed;

    private FfmsMediaSession(string path, nint videoSource, nint audioSource, MediaInfo info)
    {
        SourcePath = path;
        _videoSource = videoSource;
        _audioSource = audioSource;
        Info = info;
    }

    public string SourcePath { get; }
    public MediaInfo Info { get; }
    public bool HasVideo => _videoSource != 0;
    public bool HasAudio => _audioSource != 0;

    public static FfmsMediaSession Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        path = System.IO.Path.GetFullPath(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("Media file does not exist.", path);

        EnsureInitialized();

        byte* errorBuffer = stackalloc byte[ErrorBufferSize];
        var error = CreateErrorInfo(errorBuffer);
        var indexer = Native.FFMS_CreateIndexer(path, &error);
        if (indexer == 0)
            throw CreateException("FFMS_CreateIndexer", errorBuffer);

        Native.FFMS_TrackTypeIndexSettings(indexer, VideoTrackType, IndexTrack, 0);
        Native.FFMS_TrackTypeIndexSettings(indexer, AudioTrackType, IndexTrack, 0);
        ResetError(ref error, errorBuffer);
        var index = Native.FFMS_DoIndexing2(indexer, IndexErrorHandlingClearTrack, &error);
        if (index == 0)
            throw CreateException("FFMS_DoIndexing2", errorBuffer);

        nint videoSource = 0;
        nint audioSource = 0;
        try
        {
            ResetError(ref error, errorBuffer);
            var videoTrack = Native.FFMS_GetFirstIndexedTrackOfType(index, VideoTrackType, &error);
            ResetError(ref error, errorBuffer);
            var audioTrack = Native.FFMS_GetFirstIndexedTrackOfType(index, AudioTrackType, &error);

            if (videoTrack >= 0)
            {
                ResetError(ref error, errorBuffer);
                videoSource = Native.FFMS_CreateVideoSource(path, videoTrack, index, 0, SeekNormal, &error);
                if (videoSource == 0)
                    throw CreateException("FFMS_CreateVideoSource", errorBuffer);
            }

            if (audioTrack >= 0)
            {
                ResetError(ref error, errorBuffer);
                var delayMode = videoTrack >= 0 ? videoTrack : DelayTimeZero;
                audioSource = Native.FFMS_CreateAudioSource(path, audioTrack, index, delayMode, &error);
                if (audioSource == 0)
                    throw CreateException("FFMS_CreateAudioSource", errorBuffer);
            }

            if (videoSource == 0 && audioSource == 0)
                throw new InvalidOperationException("FFMS2 found no indexed video or audio tracks in the selected file.");

            var width = 0;
            var height = 0;
            var frameCount = 0;
            var fps = 0d;
            var videoDuration = 0d;

            if (videoSource != 0)
            {
                var properties = (FfmsVideoPropertiesPrefix*)Native.FFMS_GetVideoProperties(videoSource);
                if (properties == null)
                    throw new InvalidOperationException("FFMS_GetVideoProperties returned null.");
                frameCount = properties->NumFrames;
                fps = properties->FPSDenominator != 0
                    ? (double)properties->FPSNumerator / properties->FPSDenominator
                    : 0d;
                if (fps > 0)
                    videoDuration = frameCount / fps;

                ResetError(ref error, errorBuffer);
                var firstFramePointer = Native.FFMS_GetFrame(videoSource, 0, &error);
                if (firstFramePointer == 0)
                    throw CreateException("FFMS_GetFrame(0)", errorBuffer);
                var firstFrame = (FfmsFramePrefix*)firstFramePointer;
                width = firstFrame->EncodedWidth;
                height = firstFrame->EncodedHeight;
                if (width <= 0 || height <= 0)
                    throw new InvalidOperationException($"FFMS2 returned invalid frame dimensions {width}x{height}.");

                var bgra = Native.FFMS_GetPixFmt("bgra");
                if (bgra < 0)
                    throw new InvalidOperationException("FFMS2 could not resolve the BGRA output pixel format.");
                int* targetFormats = stackalloc int[2];
                targetFormats[0] = bgra;
                targetFormats[1] = -1;
                ResetError(ref error, errorBuffer);
                if (Native.FFMS_SetOutputFormatV2(videoSource, targetFormats, width, height, ResizerBicubic, &error) != 0)
                    throw CreateException("FFMS_SetOutputFormatV2(bgra)", errorBuffer);
            }

            var sampleRate = 0;
            var channels = 0;
            long audioSamples = 0;
            var audioDuration = 0d;

            if (audioSource != 0)
            {
                var options = Native.FFMS_CreateResampleOptions(audioSource);
                if (options == null)
                    throw new InvalidOperationException("FFMS_CreateResampleOptions returned null.");
                try
                {
                    options->SampleFormat = SampleFormatFloat;
                    ResetError(ref error, errorBuffer);
                    if (Native.FFMS_SetOutputFormatA(audioSource, options, &error) != 0)
                        throw CreateException("FFMS_SetOutputFormatA(float)", errorBuffer);
                }
                finally
                {
                    Native.FFMS_DestroyResampleOptions(options);
                }

                var audioProperties = (FfmsAudioProperties*)Native.FFMS_GetAudioProperties(audioSource);
                if (audioProperties == null)
                    throw new InvalidOperationException("FFMS_GetAudioProperties returned null.");
                sampleRate = audioProperties->SampleRate;
                channels = audioProperties->Channels;
                audioSamples = audioProperties->NumSamples;
                if (sampleRate > 0)
                    audioDuration = (double)audioSamples / sampleRate;
            }

            var duration = Math.Max(videoDuration, audioDuration);
            var info = new MediaInfo(path, width, height, frameCount, fps, duration, sampleRate, channels, audioSamples);
            return new FfmsMediaSession(path, videoSource, audioSource, info);
        }
        catch
        {
            if (videoSource != 0)
                Native.FFMS_DestroyVideoSource(videoSource);
            if (audioSource != 0)
                Native.FFMS_DestroyAudioSource(audioSource);
            throw;
        }
        finally
        {
            Native.FFMS_DestroyIndex(index);
        }
    }

    public DecodedVideoFrame GetFrameAtTime(double seconds)
    {
        lock (_videoGate)
        {
            ThrowIfDisposed();
            if (_videoSource == 0)
                throw new InvalidOperationException("The opened media has no video track.");

            byte* errorBuffer = stackalloc byte[ErrorBufferSize];
            var error = CreateErrorInfo(errorBuffer);
            var time = Info.DurationSeconds > 0 ? Math.Clamp(seconds, 0, Info.DurationSeconds) : Math.Max(0, seconds);
            var framePointer = Native.FFMS_GetFrameByTime(_videoSource, time, &error);
            if (framePointer == 0)
                throw CreateException($"FFMS_GetFrameByTime({time:0.###})", errorBuffer);

            var frame = (FfmsFramePrefix*)framePointer;
            var width = frame->ScaledWidth > 0 ? frame->ScaledWidth : frame->EncodedWidth;
            var height = frame->ScaledHeight > 0 ? frame->ScaledHeight : frame->EncodedHeight;
            var sourceStride = frame->LineSize0;
            var source = frame->Data0;
            if (source == null || width <= 0 || height <= 0 || sourceStride == 0)
                throw new InvalidOperationException("FFMS2 returned an incomplete BGRA frame.");

            var destinationStride = checked(width * 4);
            var pixels = GC.AllocateUninitializedArray<byte>(checked(destinationStride * height));
            for (var y = 0; y < height; y++)
            {
                var sourceRow = source + y * sourceStride;
                var destinationRow = pixels.AsSpan(y * destinationStride, destinationStride);
                new ReadOnlySpan<byte>(sourceRow, destinationStride).CopyTo(destinationRow);
            }
            return new DecodedVideoFrame(width, height, destinationStride, pixels);
        }
    }

    public float[] BuildWaveform(int buckets = 1200, int samplesPerBucket = 1024)
    {
        lock (_audioGate)
        {
            ThrowIfDisposed();
            if (_audioSource == 0 || Info.AudioSamples <= 0 || Info.Channels <= 0)
                return [];
            if (buckets <= 0)
                throw new ArgumentOutOfRangeException(nameof(buckets));
            if (samplesPerBucket <= 0)
                throw new ArgumentOutOfRangeException(nameof(samplesPerBucket));

            var result = new float[buckets];
            var channels = Info.Channels;
            var buffer = new float[checked(samplesPerBucket * channels)];
            byte* errorBuffer = stackalloc byte[ErrorBufferSize];
            var error = CreateErrorInfo(errorBuffer);

            for (var bucket = 0; bucket < buckets; bucket++)
            {
                var center = (long)((bucket + 0.5) * Info.AudioSamples / buckets);
                var start = Math.Max(0, center - samplesPerBucket / 2L);
                var count = (int)Math.Min(samplesPerBucket, Info.AudioSamples - start);
                if (count <= 0)
                    continue;

                Array.Clear(buffer, 0, count * channels);
                ResetError(ref error, errorBuffer);
                fixed (float* destination = buffer)
                {
                    if (Native.FFMS_GetAudio(_audioSource, destination, start, count, &error) != 0)
                        throw CreateException($"FFMS_GetAudio({start}, {count})", errorBuffer);
                }

                var peak = 0f;
                var sampleCount = count * channels;
                for (var i = 0; i < sampleCount; i++)
                    peak = Math.Max(peak, Math.Abs(buffer[i]));
                result[bucket] = Math.Min(1f, peak);
            }

            return result;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        lock (_videoGate)
        {
            if (_videoSource != 0)
            {
                Native.FFMS_DestroyVideoSource(_videoSource);
                _videoSource = 0;
            }
        }
        lock (_audioGate)
        {
            if (_audioSource != 0)
            {
                Native.FFMS_DestroyAudioSource(_audioSource);
                _audioSource = 0;
            }
        }
    }

    private static void EnsureInitialized()
    {
        if (_initialized)
            return;
        lock (InitGate)
        {
            if (_initialized)
                return;
            Native.FFMS_Init(0, 0);
            _initialized = true;
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    private static FfmsErrorInfo CreateErrorInfo(byte* buffer)
    {
        buffer[0] = 0;
        return new FfmsErrorInfo { BufferSize = ErrorBufferSize, Buffer = buffer };
    }

    private static void ResetError(ref FfmsErrorInfo error, byte* buffer)
    {
        error.ErrorType = 0;
        error.SubType = 0;
        error.BufferSize = ErrorBufferSize;
        error.Buffer = buffer;
        buffer[0] = 0;
    }

    private static Exception CreateException(string operation, byte* errorBuffer)
    {
        var length = 0;
        while (length < ErrorBufferSize && errorBuffer[length] != 0)
            length++;
        var message = length == 0
            ? "FFMS2 did not provide an error message."
            : System.Text.Encoding.UTF8.GetString(new ReadOnlySpan<byte>(errorBuffer, length));
        return new InvalidOperationException($"{operation} failed: {message}");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FfmsErrorInfo
    {
        public int ErrorType;
        public int SubType;
        public int BufferSize;
        public byte* Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FfmsVideoPropertiesPrefix
    {
        public int FPSDenominator;
        public int FPSNumerator;
        public int RFFDenominator;
        public int RFFNumerator;
        public int NumFrames;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FfmsAudioProperties
    {
        public int SampleFormat;
        public int SampleRate;
        public int BitsPerSample;
        public int Channels;
        public long ChannelLayout;
        public long NumSamples;
        public double FirstTime;
        public double LastTime;
        public double LastEndTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FfmsResampleOptions
    {
        public long ChannelLayout;
        public int SampleFormat;
        public int SampleRate;
        public int MixingCoefficientType;
        public double CenterMixLevel;
        public double SurroundMixLevel;
        public double LFEMixLevel;
        public int Normalize;
        public int ForceResample;
        public int ResampleFilterSize;
        public int ResamplePhaseShift;
        public int LinearInterpolation;
        public double CutoffFrequencyRatio;
        public int MatrixedStereoEncoding;
        public int FilterType;
        public int KaiserBeta;
        public int DitherMethod;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FfmsFramePrefix
    {
        public byte* Data0;
        public byte* Data1;
        public byte* Data2;
        public byte* Data3;
        public int LineSize0;
        public int LineSize1;
        public int LineSize2;
        public int LineSize3;
        public int EncodedWidth;
        public int EncodedHeight;
        public int EncodedPixelFormat;
        public int ScaledWidth;
        public int ScaledHeight;
        public int ConvertedPixelFormat;
    }

    private static partial class Native
    {
        private const string Library = "ffms2.dll";

        [LibraryImport(Library, EntryPoint = "FFMS_Init")]
        internal static partial void FFMS_Init(int unused, int unused2);

        [LibraryImport(Library, EntryPoint = "FFMS_CreateIndexer", StringMarshalling = StringMarshalling.Utf8)]
        internal static partial nint FFMS_CreateIndexer(string sourceFile, FfmsErrorInfo* errorInfo);

        [LibraryImport(Library, EntryPoint = "FFMS_TrackTypeIndexSettings")]
        internal static partial void FFMS_TrackTypeIndexSettings(nint indexer, int trackType, int index, int unused);

        [LibraryImport(Library, EntryPoint = "FFMS_DoIndexing2")]
        internal static partial nint FFMS_DoIndexing2(nint indexer, int errorHandling, FfmsErrorInfo* errorInfo);

        [LibraryImport(Library, EntryPoint = "FFMS_GetFirstIndexedTrackOfType")]
        internal static partial int FFMS_GetFirstIndexedTrackOfType(nint index, int trackType, FfmsErrorInfo* errorInfo);

        [LibraryImport(Library, EntryPoint = "FFMS_CreateVideoSource", StringMarshalling = StringMarshalling.Utf8)]
        internal static partial nint FFMS_CreateVideoSource(string sourceFile, int track, nint index, int threads, int seekMode, FfmsErrorInfo* errorInfo);

        [LibraryImport(Library, EntryPoint = "FFMS_CreateAudioSource", StringMarshalling = StringMarshalling.Utf8)]
        internal static partial nint FFMS_CreateAudioSource(string sourceFile, int track, nint index, int delayMode, FfmsErrorInfo* errorInfo);

        [LibraryImport(Library, EntryPoint = "FFMS_DestroyIndex")]
        internal static partial void FFMS_DestroyIndex(nint index);

        [LibraryImport(Library, EntryPoint = "FFMS_DestroyVideoSource")]
        internal static partial void FFMS_DestroyVideoSource(nint videoSource);

        [LibraryImport(Library, EntryPoint = "FFMS_DestroyAudioSource")]
        internal static partial void FFMS_DestroyAudioSource(nint audioSource);

        [LibraryImport(Library, EntryPoint = "FFMS_GetVideoProperties")]
        internal static partial nint FFMS_GetVideoProperties(nint videoSource);

        [LibraryImport(Library, EntryPoint = "FFMS_GetAudioProperties")]
        internal static partial nint FFMS_GetAudioProperties(nint audioSource);

        [LibraryImport(Library, EntryPoint = "FFMS_GetFrame")]
        internal static partial nint FFMS_GetFrame(nint videoSource, int frame, FfmsErrorInfo* errorInfo);

        [LibraryImport(Library, EntryPoint = "FFMS_GetFrameByTime")]
        internal static partial nint FFMS_GetFrameByTime(nint videoSource, double time, FfmsErrorInfo* errorInfo);

        [LibraryImport(Library, EntryPoint = "FFMS_GetPixFmt", StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int FFMS_GetPixFmt(string name);

        [LibraryImport(Library, EntryPoint = "FFMS_SetOutputFormatV2")]
        internal static partial int FFMS_SetOutputFormatV2(nint videoSource, int* targetFormats, int width, int height, int resizer, FfmsErrorInfo* errorInfo);

        [LibraryImport(Library, EntryPoint = "FFMS_CreateResampleOptions")]
        internal static partial FfmsResampleOptions* FFMS_CreateResampleOptions(nint audioSource);

        [LibraryImport(Library, EntryPoint = "FFMS_SetOutputFormatA")]
        internal static partial int FFMS_SetOutputFormatA(nint audioSource, FfmsResampleOptions* options, FfmsErrorInfo* errorInfo);

        [LibraryImport(Library, EntryPoint = "FFMS_DestroyResampleOptions")]
        internal static partial void FFMS_DestroyResampleOptions(FfmsResampleOptions* options);

        [LibraryImport(Library, EntryPoint = "FFMS_GetAudio")]
        internal static partial int FFMS_GetAudio(nint audioSource, float* buffer, long start, long count, FfmsErrorInfo* errorInfo);
    }
}
