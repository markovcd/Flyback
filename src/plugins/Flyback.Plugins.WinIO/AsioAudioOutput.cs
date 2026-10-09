using System.Runtime.Versioning;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.WinIO;

/// <summary>
/// Offers output through an installed ASIO driver, for somebody who picks it on the Sound tab.
/// </summary>
public sealed class AsioAudioOutput : IAudioOutput
{
    /// <summary>What the chosen driver is filed under: the key it registered under, which outlives a reinstall.</summary>
    public const string DriverKey = "driver";

    public string Id => "asio";

    public string Name => "ASIO";

    /// <summary>
    /// Below WASAPI's 100: sound cards' own driver packages install ASIO drivers nobody asked for,
    /// and a driver takes its card from every other program while it plays (ADR-0192).
    /// </summary>
    public int Priority => 50;

    public bool IsSupported => OperatingSystem.IsWindows() && AsioDrivers.Installed().Count > 0;

    /// <summary>One question, which driver plays. A chosen driver since uninstalled stays chosen and says so.</summary>
    public IReadOnlyList<SettingField> Form(SettingValues values)
    {
        if (!OperatingSystem.IsWindows()) return [];

        var drivers = AsioDrivers.Installed();

        if (drivers.Count == 0) return [];

        var options = drivers.ToList();
        var chosen = values.Text(DriverKey, drivers[0].Id);
        var missing = options.All(option => option.Id != chosen);

        if (missing) options.Add(new SettingOption(chosen, "A driver that is not installed"));

        return
        [
            new SettingField.Pick(DriverKey, "Driver", options, drivers[0].Id)
            {
                Note = missing
                    ? "Not installed, so the sound will not start until it is."
                    : "The driver's own control panel sets the block size. Flyback plays on its first two outputs.",
            },
        ];
    }

    public IAudioDevice Create(AudioFormat format, SettingValues settings)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("ASIO is only available on Windows.");

        var drivers = AsioDrivers.Installed();
        var name = settings.Text(DriverKey, drivers.Count > 0 ? drivers[0].Id : string.Empty);

        return new AsioAudioDevice(format, Loader(name));
    }

    [SupportedOSPlatform("windows")]
    private static Func<AsioDriver> Loader(string name) => () => AsioDrivers.Load(name);
}
