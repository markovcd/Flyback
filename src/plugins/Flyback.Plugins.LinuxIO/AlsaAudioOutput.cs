using Flyback.Plugins.Audio;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.LinuxIO;

/// <summary>
/// Offers the backend without opening anything. On Windows and macOS this is
/// the class that answers "no", and on Linux it is where a machine with no
/// sound library at all is found out.
/// </summary>
public sealed class AlsaAudioOutput : IAudioOutput
{
    /// <summary>
    /// What the chosen device is filed under — the same key the other two native
    /// backends use, though each files its own kind of id under it.
    /// </summary>
    public const string DeviceKey = "device";

    /// <summary>
    /// ALSA's own name for the device that follows the system, so what is stored is
    /// exactly what is opened.
    /// </summary>
    public const string SystemDefault = LibAsound.DefaultDevice;

    public string Id => "alsa";

    public string Name => "ALSA";

    /// <summary>
    /// The same 100 the other two native backends claim. None of the three ever
    /// competes with another — each is supported only where the others are not.
    /// </summary>
    public int Priority => 100;

    /// <summary>
    /// Two questions, because on Linux the right operating system is not enough:
    /// a container or a headless server frequently has no libasound, and this is
    /// the last moment the answer can be "no" rather than an exception.
    /// </summary>
    public bool IsSupported => OperatingSystem.IsLinux() && LibAsound.IsInstalled;

    /// <summary>
    /// One question: which output plays. The list is what ALSA's configuration names
    /// for playback, which on a desktop includes the sound server as well as each
    /// card's own routes.
    /// </summary>
    /// <remarks>
    /// A saved device that is not listed stays chosen rather than being quietly
    /// swapped for the default, so plugging it back in is all it takes; the note says
    /// what plays meanwhile. Unlike the other two backends, its name is readable, so
    /// it is shown as it is.
    /// </remarks>
    public IReadOnlyList<SettingField> Form(SettingValues values)
    {
        if (!IsSupported) return [];

        var chosen = values.Text(DeviceKey, SystemDefault);

        var options = new List<SettingOption> { new(SystemDefault, "System default") };
        options.AddRange(AlsaAudioDevice.Outputs());

        var missing = options.All(option => option.Id != chosen);

        if (missing) options.Add(new SettingOption(chosen, $"{chosen} (not connected)"));

        return
        [
            new SettingField.Pick(DeviceKey, "Device", options, SystemDefault)
            {
                Note = missing ? "Not connected, so the system default plays until it is." : null,
            },
        ];
    }

    public IAudioDevice Create(AudioFormat format, SettingValues settings)
    {
        var device = settings.Text(DeviceKey, SystemDefault);

        return new AlsaAudioDevice(format, device == SystemDefault ? null : device);
    }
}
