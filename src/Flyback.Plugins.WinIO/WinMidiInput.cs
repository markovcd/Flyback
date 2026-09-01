using Flyback.Plugins.Midi;

namespace Flyback.Plugins.WinIO;

/// <summary>
/// Offers the backend without opening anything. On macOS and Linux this is the
/// class that answers "no", which is what keeps the plugin loadable everywhere
/// even though the devices are not.
/// </summary>
public sealed class WinMidiInput : IMidiInput
{
    public string Id => "winmm";

    public string Name => "Windows MIDI";

    /// <summary>
    /// The 100 a native backend claims here, matching the sound plugins. There
    /// is nothing for it to compete with yet — CoreMIDI and the ALSA sequencer
    /// are the other two thirds of this, and neither would be supported on a
    /// machine where this one is.
    /// </summary>
    public int Priority => 100;

    public bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>
    /// What is plugged in right now, asked of winmm each time.
    /// </summary>
    /// <remarks>
    /// Total, whatever the drivers do. A device half-installed, one pulled out
    /// between two calls, a driver that refuses to describe itself — none of it
    /// is a reason for a picker not to draw, and there is always the computer's
    /// own keyboard behind this list.
    /// </remarks>
    public IReadOnlyList<MidiPortInfo> Ports
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return [];

            try
            {
                return MidiPorts.Named(WinMm.DeviceNames());
            }
            catch
            {
                return [];
            }
        }
    }

    /// <summary>
    /// Opens one device by the id a patch stored.
    /// </summary>
    /// <remarks>
    /// The id is resolved by looking it up in the current device list, so a
    /// device can move without the patch breaking.
    /// </remarks>
    public IMidiPort Open(string port, MidiCallback deliver)
    {
        ArgumentNullException.ThrowIfNull(deliver);

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows MIDI input is only available on Windows.");

        var ports = Ports;

        for (var index = 0; index < ports.Count; index++)
            if (string.Equals(ports[index].Id, port, StringComparison.Ordinal))
                return new WinMidiPort(port, (uint)index, deliver);

        throw new InvalidOperationException($"'{port}' is not plugged in.");
    }
}
