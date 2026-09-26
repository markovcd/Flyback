namespace Flyback.Plugins.Midi;

/// <summary>
/// One device that could be played, before anything is open.
/// </summary>
/// <param name="Id">
/// Stable, and what a saved patch stores. Deliberately not the port number: plug
/// the same keyboard into the other socket and a patch keyed by number would be
/// pointing at nothing. <see cref="MidiPorts.Named"/> is how a backend gets one
/// that survives being unplugged.
/// </param>
/// <param name="Name">What the picker shows — the device's own name, unaltered.</param>
public readonly record struct MidiPortInfo(string Id, string Name);