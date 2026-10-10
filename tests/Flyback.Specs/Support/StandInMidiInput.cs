using Flyback.Plugins.Midi;

namespace Flyback.Specs.Support;

/// <summary>A MIDI backend with one device plugged in, called <paramref name="portName"/>, and nothing behind it.</summary>
internal sealed class StandInMidiInput(string portName) : IMidiInput
{
    /// <summary>The device's port once the patch has opened it, and null while nothing listens to it.</summary>
    public StandInMidiPort? Port { get; private set; }

    public string Id => "stand-in";

    public string Name => "Stand-in";

    public int Priority => 1;

    public bool IsSupported => true;

    public IReadOnlyList<MidiPortInfo> Ports => MidiPorts.Named([portName]);

    public IMidiPort Open(string port, MidiCallback deliver) => Port = new StandInMidiPort(port, deliver);
}
