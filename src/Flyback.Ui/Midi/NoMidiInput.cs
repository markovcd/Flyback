using Flyback.Plugins.Hosting;
using Flyback.Plugins.Midi;

namespace Flyback.App.Midi;

/// <summary>
/// The input used when nothing installed can listen. It has no ports, so nothing is
/// ever opened and nothing downstream needs a null check.
/// </summary>
internal sealed class NoMidiInput : IMidiInput
{
    public static NoMidiInput Instance { get; } = new();

    private NoMidiInput()
    {
    }

    public string Id => "none";

    public string Name => "No MIDI";

    public int Priority => int.MinValue;

    public bool IsSupported => false;

    public IReadOnlyList<MidiPortInfo> Ports => [];

    public IMidiPort Open(string port, MidiCallback deliver) =>
        throw new InvalidOperationException("There is no MIDI input to open a port on.");
}

internal static class MidiInputs
{
    extension(PluginCatalog plugins)
    {
        /// <summary>What hears the instruments plugged in: the plugins' preferred input, or one that hears none.</summary>
        public IMidiInput MidiInput => plugins.PreferredMidiInput ?? NoMidiInput.Instance;
    }
}
