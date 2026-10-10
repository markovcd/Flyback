using Flyback.Plugins.Midi;

namespace Flyback.Ui.Tests.Midi;

/// <summary>A stand-in for a platform MIDI backend, with no platform behind it.</summary>
internal sealed class FakeMidiInput(params string[] names) : IMidiInput
{
    public List<FakeMidiPort> Opened { get; } = [];

    /// <summary>Whether opening fails, the way a device another program holds does.</summary>
    public bool Refuse { get; init; }

    public string Id => "fake";

    public string Name => "Stand-in";

    public int Priority => 1;

    public bool IsSupported => true;

    public IReadOnlyList<MidiPortInfo> Ports => MidiPorts.Named(names);

    public IMidiPort Open(string port, MidiCallback deliver)
    {
        if (Refuse) throw new InvalidOperationException("it is already in use.");

        var opened = new FakeMidiPort(port, deliver);

        Opened.Add(opened);

        return opened;
    }
}
