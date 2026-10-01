using System.Runtime.InteropServices;

namespace Yoake.Native;

public sealed record DecodedAudioFrames(
    int SampleRate,
    int Channels,
    long StartFrame,
    float[] Samples)
{
    public int FrameCount => Channels > 0 ? Samples.Length / Channels : 0;
}

public sealed unsafe partial class FfmsMediaSession
{
    public long TimeToAudioFrame(double seconds)
    {
        if (Info.SampleRate <= 0 || Info.AudioSamples <= 0)
            return 0;
        var clamped = Info.DurationSeconds > 0
            ? Math.Clamp(seconds, 0, Info.DurationSeconds)
            : Math.Max(0, seconds);
        return Math.Clamp((long)Math.Floor(clamped * Info.SampleRate), 0, Info.AudioSamples);
    }

    public DecodedAudioFrames ReadAudioFrames(long startFrame, int frameCount)
    {
        lock (_audioGate)
        {
            ThrowIfDisposed();
            if (_audioSource == 0 || Info.SampleRate <= 0 || Info.Channels <= 0 || Info.AudioSamples <= 0)
                throw new InvalidOperationException("The opened media has no usable audio track.");
            if (frameCount < 0)
                throw new ArgumentOutOfRangeException(nameof(frameCount));

            var start = Math.Clamp(startFrame, 0, Info.AudioSamples);
            var count = (int)Math.Min(frameCount, Info.AudioSamples - start);
            if (count <= 0)
                return new DecodedAudioFrames(Info.SampleRate, Info.Channels, start, []);

            var samples = GC.AllocateUninitializedArray<float>(checked(count * Info.Channels));
            byte* errorBuffer = stackalloc byte[ErrorBufferSize];
            var error = CreateErrorInfo(errorBuffer);
            ResetError(ref error, errorBuffer);
            fixed (float* destination = samples)
            {
                if (Native.FFMS_GetAudio(_audioSource, destination, start, count, &error) != 0)
                    throw CreateException($"FFMS_GetAudio({start}, {count})", errorBuffer);
            }

            return new DecodedAudioFrames(Info.SampleRate, Info.Channels, start, samples);
        }
    }
}
