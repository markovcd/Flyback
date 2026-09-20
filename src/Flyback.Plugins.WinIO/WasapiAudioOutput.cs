using Flyback.Plugins.Audio;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.WinIO;

/// <summary>
/// Offers the backend without opening anything. On Linux and macOS this is the
/// class that answers "no", which is what keeps the plugin loadable everywhere
/// even though the device is not.
/// </summary>
public sealed class WasapiAudioOutput : IAudioOutput
{
    /// <summary>
    /// What the chosen device is filed under. Stable, because it is in the settings
    /// file of everybody who has picked one.
    /// </summary>
    public const string DeviceKey = "device";

    /// <summary>
    /// The device id that means whatever Windows is playing through, and follows it
    /// when that changes. A word rather than an empty string, because an empty value
    /// reads back as nobody having chosen.
    /// </summary>
    public const string SystemDefault = "default";

    public string Id => "wasapi";

    public string Name => "WASAPI (shared mode)";

    /// <summary>
    /// Above the default of zero: where WASAPI works it is the lowest-latency
    /// path available, so a portable backend installed alongside it should lose.
    /// </summary>
    public int Priority => 100;

    public bool IsSupported => OperatingSystem.IsWindows();

    // The guard is written out at each call rather than shared, because the
    // platform analyser reads it there and cannot see through IsSupported.

    /// <summary>
    /// One question: which output plays. The list is what is plugged in and enabled
    /// right now, read afresh each time the form is drawn.
    /// </summary>
    /// <remarks>
    /// A saved device that has gone — headphones unplugged, a USB interface off —
    /// stays chosen rather than being quietly swapped for the default, so plugging it
    /// back in is all it takes; the note says what plays meanwhile. It is listed by a
    /// name of its own, because the id Windows gives an endpoint is not something to
    /// put in front of anybody.
    /// </remarks>
    public IReadOnlyList<SettingField> Form(SettingValues values)
    {
        if (!OperatingSystem.IsWindows()) return [];

        var endpoints = WasapiAudioDevice.Endpoints();
        var chosen = values.Text(DeviceKey, SystemDefault);

        var options = new List<SettingOption> { new(SystemDefault, "System default") };
        options.AddRange(endpoints);

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
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("WASAPI is only available on Windows.");

        var device = settings.Text(DeviceKey, SystemDefault);

        return new WasapiAudioDevice(format, device == SystemDefault ? null : device);
    }
}
