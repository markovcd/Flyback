using Flyback.Plugins.Midi;

namespace Flyback.Specs.Support;

/// <summary>The device's port, sending on the caller's thread what a driver would send on its own.</summary>
internal sealed class StandInMidiPort(string id, MidiCallback deliver) : IMidiPort
{
    public string Id => id;

    public bool IsOpen { get; private set; } = true;

    public void Send(MidiMessage message) => deliver(message);

    public void Dispose() => IsOpen = false;
}
