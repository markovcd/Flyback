using Flyback.Plugins.Midi;

namespace Flyback.Ui.Tests.Midi;

/// <summary>An open port on a <see cref="FakeMidiInput"/>, sending on the test's thread what a driver would send on its own.</summary>
internal sealed class FakeMidiPort(string id, MidiCallback deliver) : IMidiPort
{
    public string Id => id;

    public bool IsOpen { get; private set; } = true;

    /// <summary>How many times it was closed, because closing twice is its own bug.</summary>
    public int Closed { get; private set; }

    public void Send(MidiMessage message) => deliver(message);

    public void Dispose()
    {
        IsOpen = false;
        Closed++;
    }
}
