using Flyback.Core;
using Flyback.Core.Render;

namespace Flyback.Plugins.Audio;

/// <summary>
/// Fills an interleaved stereo buffer. Called on the audio thread, so it must
/// not block, allocate or throw.
/// </summary>
/// <remarks>
/// A named delegate rather than <c>Action&lt;Span&lt;float&gt;&gt;</c>, because
/// a ref struct cannot be a generic type argument.
/// </remarks>
public delegate void AudioCallback(Span<float> interleavedStereo);

/// <summary>
/// What the host asks a backend to open. A backend that cannot honour the
/// request exactly is free to open the nearest thing it can and report the
/// truth through <see cref="IAudioDevice.SampleRate"/>.
/// </summary>
public readonly record struct AudioFormat(int SampleRate, int Channels, int LatencyMilliseconds)
{
    public static AudioFormat Default => new(GlobalConstants.SampleRate, 2, 30);
}

/// <summary>
/// A sound output. The whole platform-specific surface of the program: the
/// engine produces sample buffers with no dependencies at all, and this is the
/// seam where they meet an actual device.
/// </summary>
public interface IAudioDevice : IDisposable
{
    /// <summary>The rate the device actually opened at, which may not be the one asked for.</summary>
    int SampleRate { get; }

    bool IsRunning { get; }

    void Start(AudioCallback fill);

    void Stop();
}

/// <summary>
/// A backend a plugin offers, before any device exists. Kept separate from
/// <see cref="IAudioDevice"/> so the host can list what is available, and pick
/// between backends, without opening hardware to find out.
/// </summary>
public interface IAudioOutput
{
    /// <summary>Stable identifier, e.g. <c>wasapi</c>.</summary>
    string Id { get; }

    /// <summary>What a person should see, e.g. <c>WASAPI (shared mode)</c>.</summary>
    string Name { get; }

    /// <summary>Higher wins when several backends are available. Ties break on <see cref="Id"/>.</summary>
    int Priority { get; }

    /// <summary>
    /// Whether this backend can run here at all. Must be answerable without
    /// opening a device and without throwing — a backend for another operating
    /// system reports <c>false</c> rather than failing in <see cref="Create"/>.
    /// </summary>
    bool IsSupported { get; }

    IAudioDevice Create(AudioFormat format);
}
