using Flyback.Plugins.Midi;

namespace Flyback.Plugins.MacIO;

/// <summary>
/// One thing on the machine that could be played: the server's reference for it, and
/// what to call it. The reference is what a patch must not store — it is a number the
/// MIDI server made up and is a different number tomorrow — so the name is what
/// survives.
/// </summary>
internal readonly record struct MidiSource(uint Endpoint, string Name);
