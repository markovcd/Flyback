using Flyback.Plugins.Audio;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.MacIO;

/// <summary>
/// Offers the input backend without opening anything. The mirror of
/// <see cref="CoreAudioOutput"/>: it answers "no" off macOS, and lists what could listen
/// only when a form asks.
/// </summary>
public sealed class CoreAudioInput : IAudioInput
{
    /// <summary>What the chosen device is filed under, as in <see cref="CoreAudioOutput.DeviceKey"/>.</summary>
    public const string DeviceKey = CoreAudioOutput.DeviceKey;

    /// <summary>The device id that means the default input, as in <see cref="CoreAudioOutput.SystemDefault"/>.</summary>
    public const string SystemDefault = CoreAudioOutput.SystemDefault;

    public string Id => "coreaudio-capture";

    public string Name => "CoreAudio";

    public int Priority => 100;

    public bool IsSupported => OperatingSystem.IsMacOS();

    /// <summary>
    /// One question: which input listens. A saved device that is not listed stays chosen
    /// rather than being swapped for the default, and the note says what listens meanwhile.
    /// </summary>
    public IReadOnlyList<SettingField> Form(SettingValues values)
    {
        if (!IsSupported) return [];

        var chosen = values.Text(DeviceKey, SystemDefault);

        var options = new List<SettingOption> { new(SystemDefault, "System default") };
        options.AddRange(CoreAudioCapture.Inputs());

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
        if (!IsSupported)
            throw new PlatformNotSupportedException("CoreAudio is only available on macOS.");

        var device = settings.Text(DeviceKey, SystemDefault);

        return new CoreAudioCapture(format, device == SystemDefault ? null : device);
    }
}
