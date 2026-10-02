using Flyback.Plugins.Audio;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.MacIO;

/// <summary>
/// Offers the backend without opening anything. On Windows and Linux this is
/// the class that answers "no", which is what keeps the plugin loadable
/// everywhere even though the device is not.
/// </summary>
public sealed class CoreAudioOutput : IAudioOutput
{
    /// <summary>
    /// What the chosen device is filed under — the same key the other two native
    /// backends use, though each files its own kind of id under it.
    /// </summary>
    public const string DeviceKey = "device";

    /// <summary>
    /// The device id that means whatever the system is playing through, and follows
    /// it when that changes. A word rather than an empty string, because an empty
    /// value reads back as nobody having chosen.
    /// </summary>
    public const string SystemDefault = "default";

    public string Id => "coreaudio";

    public string Name => "CoreAudio";

    /// <summary>
    /// The same 100 the WASAPI backend claims, and for the same reason: where it
    /// works it is the native path, and a portable backend installed alongside
    /// it should lose. The two never compete — each is supported only where the
    /// other is not — so the equal priority costs nothing.
    /// </summary>
    public int Priority => 100;

    public bool IsSupported => OperatingSystem.IsMacOS();

    /// <summary>
    /// One question: which output plays. The list is every device with an output
    /// stream right now, by the UID macOS keeps for it across a reboot.
    /// </summary>
    /// <remarks>
    /// A saved device that has gone stays chosen rather than being quietly swapped
    /// for the default, so plugging it back in is all it takes; the note says what
    /// plays meanwhile.
    /// </remarks>
    public IReadOnlyList<SettingField> Form(SettingValues values)
    {
        if (!IsSupported) return [];

        var chosen = values.Text(DeviceKey, SystemDefault);

        var options = new List<SettingOption> { new(SystemDefault, "System default") };
        options.AddRange(CoreAudioDevice.Outputs());

        var missing = options.All(option => option.Id != chosen);

        if (missing) options.Add(new SettingOption(chosen, "A device that is not connected"));

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

        return new CoreAudioDevice(format, device == SystemDefault ? null : device);
    }
}
