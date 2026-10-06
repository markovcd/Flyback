using Flyback.Plugins.Settings;

namespace Flyback.Plugins.Audio;

/// <summary>
/// A backend a plugin offers for hearing a microphone or a line, before any device
/// exists. The mirror of <see cref="IAudioOutput"/>, and kept separate from
/// <see cref="IAudioCapture"/> for the same reason: the host can list what is
/// available, and choose between backends, without opening hardware.
/// </summary>
/// <remarks>
/// Nothing is opened until a running patch holds a Line In, and the device is let go
/// when it no longer does, so a machine whose patch never listens is never listened to.
/// </remarks>
public interface IAudioInput
{
    /// <summary>Stable identifier, e.g. <c>alsa-capture</c>.</summary>
    string Id { get; }

    /// <summary>What a person should see, e.g. <c>ALSA</c>.</summary>
    string Name { get; }

    /// <summary>Higher wins when several backends are available. Ties break on <see cref="Id"/>.</summary>
    int Priority { get; }

    /// <summary>
    /// Whether this backend can run here at all. Must be answerable without opening a
    /// device and without throwing.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// What this backend lets somebody set — which device listens, say — declared rather
    /// than drawn (ADR-0085). Empty for a backend with nothing to ask. Must not open a
    /// device or throw.
    /// </summary>
    /// <param name="values">What the form holds now, as the settings file keeps it.</param>
    IReadOnlyList<SettingField> Form(SettingValues values) => [];

    /// <summary>Makes a capture that opens nothing until it is started.</summary>
    /// <param name="format">What the host needs: its sample rate and latency.</param>
    /// <param name="settings">What <see cref="Form"/> was last answered with.</param>
    IAudioCapture Create(AudioFormat format, SettingValues settings);
}
