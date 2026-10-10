using Flyback.Plugins.Midi;

namespace Flyback.Plugins.LinuxIO;

/// <summary>
/// One thing on the machine that could be played: which client, which port, and what
/// to call it.
/// </summary>
/// <remarks>
/// The pair of numbers is the sequencer's address and is exactly what a patch must not
/// store — client numbers are handed out in the order things were plugged in. The name
/// is what survives, which is what <see cref="MidiPorts.Named"/> turns into an id.
/// </remarks>
internal readonly record struct SequencerPort(int Client, int Port, string Name);
