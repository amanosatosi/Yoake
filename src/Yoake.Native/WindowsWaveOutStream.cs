using System.Runtime.InteropServices;
using Yoake.Core.Audio;

namespace Yoake.Native;

public sealed partial class WindowsWaveOutStream : IDisposable
{
    private const uint WaveMapper = 0xFFFFFFFF;
    private const uint CallbackNull = 0;
    private const uint WhdrDone = 0x00000001;
    private const ushort WaveFormatPcm = 1;
    private const uint TimeSamples = 0x00000002;
    private const int QueueDepth = 4;

    private readonly object _gate = new();
    private readonly List<BufferSlot> _slots = [];
    private nint _device;
    private nint _positionBuffer;
    private bool _disposed;
    private bool _stopRequested;
    private int _sampleRate;
    private int _channels;
    private long _mediaStartFrame;
    private double _volume=0.8;
    private bool _muted;
    public void SetVolume(double volume,bool muted){Volatile.Write(ref _volume,Math.Clamp(volume,0,1));Volatile.Write(ref _muted,muted);}

    public async Task PlayAsync(
        FfmsMediaSession media,
        double startSeconds,
        Action<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(media);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Yoake's initial audio output backend uses Windows waveOut.");
        if (!media.HasAudio || media.Info.SampleRate <= 0 || media.Info.Channels <= 0)
            return;

        var startFrame = media.TimeToAudioFrame(startSeconds);
        if (startFrame >= media.Info.AudioSamples)
            return;

        StartDevice(media.Info.SampleRate, media.Info.Channels, startFrame);

        var chunkFrames = Math.Max(512, media.Info.SampleRate / 50);
        var nextFrame = startFrame;
        try
        {
            for (var i = 0; i < QueueDepth && nextFrame < media.Info.AudioSamples; i++)
            {
                var queued = QueueDecodedChunk(media, nextFrame, chunkFrames);
                if (queued <= 0)
                    break;
                nextFrame += queued;
            }

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                List<BufferSlot> completed;
                long playedFrames;
                bool stopped;
                lock (_gate)
                {
                    stopped = _device == 0 || _stopRequested;
                    if (stopped)
                        break;

                    playedFrames = GetPlayedFramesLocked();
                    completed = _slots.Where(IsDone).ToList();
                    foreach (var slot in completed)
                    {
                        ReleaseSlotLocked(slot);
                        _slots.Remove(slot);
                    }
                }

                progress?.Invoke((double)(_mediaStartFrame + playedFrames) / _sampleRate);

                foreach (var _ in completed)
                {
                    if (nextFrame >= media.Info.AudioSamples)
                        break;
                    var queued = QueueDecodedChunk(media, nextFrame, chunkFrames);
                    if (queued <= 0)
                        break;
                    nextFrame += queued;
                }

                lock (_gate)
                {
                    if (_slots.Count == 0 && nextFrame >= media.Info.AudioSamples)
                        break;
                }

                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }

            if (!cancellationToken.IsCancellationRequested)
                progress?.Invoke((double)Math.Min(media.Info.AudioSamples, nextFrame) / _sampleRate);
        }
        finally
        {
            Stop();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _stopRequested = true;
            if (_device != 0)
                _ = Native.waveOutReset(_device);
            CleanupLocked();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }
        Stop();
    }

    private void StartDevice(int sampleRate, int channels, long mediaStartFrame)
    {
        if (channels <= 0 || channels > ushort.MaxValue)
            throw new InvalidOperationException($"Unsupported waveOut channel count: {channels}.");

        lock (_gate)
        {
            ThrowIfDisposed();
            CleanupLocked();
            _stopRequested = false;
            _sampleRate = sampleRate;
            _channels = channels;
            _mediaStartFrame = mediaStartFrame;

            var format = new WaveFormat
            {
                FormatTag = WaveFormatPcm,
                Channels = checked((ushort)channels),
                SamplesPerSec = checked((uint)sampleRate),
                BitsPerSample = 16,
                BlockAlign = checked((ushort)(channels * sizeof(short))),
            };
            format.AvgBytesPerSec = checked(format.SamplesPerSec * format.BlockAlign);

            var formatSize = Marshal.SizeOf<WaveFormat>();
            var formatBuffer = Marshal.AllocHGlobal(formatSize);
            try
            {
                Marshal.StructureToPtr(format, formatBuffer, false);
                var result = Native.waveOutOpen(out _device, WaveMapper, formatBuffer, 0, 0, CallbackNull);
                ThrowIfMmError(result, "waveOutOpen");
            }
            finally
            {
                Marshal.FreeHGlobal(formatBuffer);
            }

            _positionBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<MmTime>());
        }
    }

    private int QueueDecodedChunk(FfmsMediaSession media, long startFrame, int requestedFrames)
    {
        var decoded = media.ReadAudioFrames(startFrame, requestedFrames);
        if (decoded.FrameCount <= 0)
            return 0;

        var pcm = new short[decoded.Samples.Length];
        var volume=Volatile.Read(ref _volume);var muted=Volatile.Read(ref _muted);
        for (var i = 0; i < pcm.Length; i++)
            pcm[i] = PlaybackGain.Pcm16(decoded.Samples[i],volume,muted);

        lock (_gate)
        {
            if (_device == 0 || _stopRequested)
                return 0;

            var bufferBytes = checked(pcm.Length * sizeof(short));
            var slot = new BufferSlot
            {
                Buffer = Marshal.AllocHGlobal(bufferBytes),
                Header = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>()),
                FrameCount = decoded.FrameCount,
            };

            try
            {
                Marshal.Copy(pcm, 0, slot.Buffer, pcm.Length);
                var header = new WaveHeader
                {
                    Data = slot.Buffer,
                    BufferLength = checked((uint)bufferBytes),
                };
                Marshal.StructureToPtr(header, slot.Header, false);

                var headerSize = checked((uint)Marshal.SizeOf<WaveHeader>());
                ThrowIfMmError(Native.waveOutPrepareHeader(_device, slot.Header, headerSize), "waveOutPrepareHeader");
                slot.Prepared = true;
                ThrowIfMmError(Native.waveOutWrite(_device, slot.Header, headerSize), "waveOutWrite");
                _slots.Add(slot);
                return decoded.FrameCount;
            }
            catch
            {
                if (slot.Prepared)
                    _ = Native.waveOutUnprepareHeader(_device, slot.Header, checked((uint)Marshal.SizeOf<WaveHeader>()));
                Marshal.FreeHGlobal(slot.Header);
                Marshal.FreeHGlobal(slot.Buffer);
                throw;
            }
        }
    }

    private long GetPlayedFramesLocked()
    {
        if (_device == 0 || _positionBuffer == 0)
            return 0;

        Marshal.StructureToPtr(new MmTime { Type = TimeSamples }, _positionBuffer, false);
        var result = Native.waveOutGetPosition(_device, _positionBuffer, checked((uint)Marshal.SizeOf<MmTime>()));
        if (result != 0)
            return 0;
        return Marshal.PtrToStructure<MmTime>(_positionBuffer).Sample;
    }

    private static bool IsDone(BufferSlot slot)
        => (Marshal.PtrToStructure<WaveHeader>(slot.Header).Flags & WhdrDone) != 0;

    private void ReleaseSlotLocked(BufferSlot slot)
    {
        if (_device != 0 && slot.Prepared)
            _ = Native.waveOutUnprepareHeader(_device, slot.Header, checked((uint)Marshal.SizeOf<WaveHeader>()));
        if (slot.Header != 0)
            Marshal.FreeHGlobal(slot.Header);
        if (slot.Buffer != 0)
            Marshal.FreeHGlobal(slot.Buffer);
        slot.Header = 0;
        slot.Buffer = 0;
        slot.Prepared = false;
    }

    private void CleanupLocked()
    {
        foreach (var slot in _slots)
            ReleaseSlotLocked(slot);
        _slots.Clear();

        if (_device != 0)
            _ = Native.waveOutClose(_device);
        _device = 0;

        if (_positionBuffer != 0)
        {
            Marshal.FreeHGlobal(_positionBuffer);
            _positionBuffer = 0;
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static void ThrowIfMmError(uint result, string operation)
    {
        if (result != 0)
            throw new InvalidOperationException($"{operation} failed with multimedia error {result}.");
    }

    private sealed class BufferSlot
    {
        public nint Buffer;
        public nint Header;
        public int FrameCount;
        public bool Prepared;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormat
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSec;
        public uint AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort ExtraSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public nint Data;
        public uint BufferLength;
        public uint BytesRecorded;
        public nuint User;
        public uint Flags;
        public uint Loops;
        public nint Next;
        public nuint Reserved;
    }

    [StructLayout(LayoutKind.Explicit, Size = 12)]
    private struct MmTime
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(4)] public uint Sample;
    }

    private static partial class Native
    {
        [LibraryImport("winmm.dll")]
        internal static partial uint waveOutOpen(out nint device, uint deviceId, nint format, nint callback, nuint instance, uint flags);

        [LibraryImport("winmm.dll")]
        internal static partial uint waveOutPrepareHeader(nint device, nint header, uint size);

        [LibraryImport("winmm.dll")]
        internal static partial uint waveOutWrite(nint device, nint header, uint size);

        [LibraryImport("winmm.dll")]
        internal static partial uint waveOutReset(nint device);

        [LibraryImport("winmm.dll")]
        internal static partial uint waveOutUnprepareHeader(nint device, nint header, uint size);

        [LibraryImport("winmm.dll")]
        internal static partial uint waveOutClose(nint device);

        [LibraryImport("winmm.dll")]
        internal static partial uint waveOutGetPosition(nint device, nint time, uint size);
    }
}
