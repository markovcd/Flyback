using Flyback.Plugins.Audio;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.WinIO;

/// <summary>
/// Offers the input backend without opening anything. The mirror of
/// <see cref="WasapiAudioOutput"/>: it answers "no" off Windows, and lists what could
/// listen only when a form asks.
/// </summary>
public sealed class WasapiAudioInput : IAudioInput
{
    /// <summary>What the chosen device is filed under, as in <see cref="WasapiAudioOutput.DeviceKey"/>.</summary>
    public const string DeviceKey = WasapiAudioOutput.DeviceKey;

    /// <summary>The device id that means the default recording device, as in <see cref="WasapiAudioOutput.SystemDefault"/>.</summary>
    public const string SystemDefault = WasapiAudioOutput.SystemDefault;

    public string Id => "wasapi-capture";

    public string Name => "WASAPI (shared mode)";

    public int Priority => 100;

    public bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>
    /// One question: which input listens. A saved device that is not listed stays chosen
    /// rather than being swapped for the default, and the note says what listens meanwhile.
    /// </summary>
    public IReadOnlyList<SettingField> Form(SettingValues values)
    {
        if (!OperatingSystem.IsWindows()) return [];

        var chosen = values.Text(DeviceKey, SystemDefault);

        var options = new List<SettingOption> { new(SystemDefault, "System default") };
        options.AddRange(WasapiAudioCapture.Endpoints());

        var missing = options.All(option => option.Id != chosen);

        if (missing) options.Add(new SettingOption(chosen, "A device that is not connected"));

        return
        [
            new SettingField.Pick(DeviceKey, "Input", options, SystemDefault)
            {
                Note = missing ? "Not connected, so the system default listens until it is." : null,
            },
        ];
    }

    public IAudioCapture Create(AudioFormat format, SettingValues settings)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("WASAPI is only available on Windows.");

        var device = settings.Text(DeviceKey, SystemDefault);

        return new WasapiAudioCapture(format, device == SystemDefault ? null : device);
    }
}
