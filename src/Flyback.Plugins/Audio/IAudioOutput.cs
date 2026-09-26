using Flyback.Plugins.Settings;

namespace Flyback.Plugins.Audio;

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

    /// <summary>
    /// What this backend lets somebody set — which device plays, say — declared
    /// rather than drawn, and asked for again after every change (ADR-0085).
    /// </summary>
    /// <remarks>
    /// Empty for a backend with nothing to ask, which is the default. Only asked of
    /// a backend that <see cref="IsSupported"/>, and like it must not open a device
    /// or throw: listing what is plugged in is fine, playing through it is not.
    /// </remarks>
    /// <param name="values">What the form holds now, as the settings file keeps it.</param>
    IReadOnlyList<SettingField> Form(SettingValues values) => [];

    /// <param name="format">What the host needs the device to play.</param>
    /// <param name="settings">
    /// What <see cref="Form"/> was last answered with, read back through the
    /// fields' own <see cref="SettingField.Sane"/> by the backend itself — the host
    /// stores these and never looks inside.
    /// </param>
    IAudioDevice Create(AudioFormat format, SettingValues settings);
}