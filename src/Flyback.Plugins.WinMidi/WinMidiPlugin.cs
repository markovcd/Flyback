using Flyback.Plugins.Midi;

namespace Flyback.Plugins.WinMidi;

/// <summary>
/// Entry point of the Windows MIDI plugin. Loading this assembly must not call
/// into winmm — the library is only reached when devices are listed or one is
/// opened, so the plugin lists itself harmlessly on a machine that has no such
/// library at all.
/// </summary>
public sealed class WinMidiPlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new(
        "winmm.midi",
        "Windows MIDI input",
        "Plays a patch from a MIDI keyboard, through the multimedia library Windows already has.");

    public void Register(IPluginRegistry registry) => registry.AddMidiInput(new WinMidiInput());
}

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
    /// The id is turned back into winmm's device number by finding it in the
    /// list again, rather than by remembering what it was when the picker was
    /// last drawn. That is the whole point of an id that is not a number: the
    /// device may have moved since, and something else may be at the number it
    /// used to be.
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
