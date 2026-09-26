namespace Flyback.Plugins.Midi;

/// <summary>
/// A MIDI backend a plugin offers, before any device is open. Kept separate from
/// <see cref="IMidiPort"/> for the reason ADR-0025 split the sound pair: the host
/// can ask what is there, and choose between backends, without opening hardware
/// to find out.
/// </summary>
public interface IMidiInput
{
    /// <summary>Stable identifier, e.g. <c>winmm</c>.</summary>
    string Id { get; }

    /// <summary>What a person should see, e.g. <c>Windows MIDI</c>.</summary>
    string Name { get; }

    /// <summary>Higher wins when several backends are available. Ties break on <see cref="Id"/>.</summary>
    int Priority { get; }

    /// <summary>
    /// Whether this backend can run here at all. Must be answerable without
    /// opening a device and without throwing — a backend for another operating
    /// system reports <c>false</c> rather than failing in <see cref="Open"/>.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// What is plugged in right now.
    /// </summary>
    /// <remarks>
    /// Asked afresh every time rather than listed once, because devices are
    /// plugged in and pulled out while the program runs. Enumerating must not
    /// open anything and must not throw: a driver that is half-installed or a
    /// device that vanished between two calls is a shorter list, not a failure,
    /// and there is always the computer's own keyboard behind this.
    /// </remarks>
    IReadOnlyList<MidiPortInfo> Ports { get; }

    /// <summary>
    /// Opens one device and starts listening. Throws if it cannot — a device
    /// another program has taken, or one that was unplugged since
    /// <see cref="Ports"/> was read.
    /// </summary>
    IMidiPort Open(string port, MidiCallback deliver);
}