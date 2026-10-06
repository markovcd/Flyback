using Flyback.Plugins.Audio;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.LinuxIO;

/// <summary>
/// Offers the input backend without opening anything. The mirror of
/// <see cref="AlsaAudioOutput"/>: it answers "no" where libasound is not, and lists what
/// could listen only when a form asks.
/// </summary>
public sealed class AlsaAudioInput : IAudioInput
{
    /// <summary>What the chosen device is filed under, as in <see cref="AlsaAudioOutput.DeviceKey"/>.</summary>
    public const string DeviceKey = "device";

    public string Id => "alsa-capture";

    public string Name => "ALSA";

    public int Priority => 100;

    public bool IsSupported => OperatingSystem.IsLinux() && LibAsound.IsInstalled;

    /// <summary>
    /// One question: which input listens. A saved device that is not listed stays chosen
    /// rather than being swapped for the default, and the note says what listens meanwhile.
    /// </summary>
    public IReadOnlyList<SettingField> Form(SettingValues values)
    {
        if (!IsSupported) return [];

        var chosen = values.Text(DeviceKey, LibAsound.DefaultDevice);

        var options = new List<SettingOption> { new(LibAsound.DefaultDevice, "System default") };
        options.AddRange(AlsaAudioCapture.Inputs());

        var missing = options.All(option => option.Id != chosen);

        if (missing) options.Add(new SettingOption(chosen, $"{chosen} (not connected)"));

        return
        [
            new SettingField.Pick(DeviceKey, "Input", options, LibAsound.DefaultDevice)
            {
                Note = missing ? "Not connected, so the system default listens until it is." : null,
            },
        ];
    }

    public IAudioCapture Create(AudioFormat format, SettingValues settings)
    {
        var device = settings.Text(DeviceKey, LibAsound.DefaultDevice);

        return new AlsaAudioCapture(format, device == LibAsound.DefaultDevice ? null : device);
    }
}
