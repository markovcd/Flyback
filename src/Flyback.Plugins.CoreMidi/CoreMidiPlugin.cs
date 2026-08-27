using Flyback.Plugins.Midi;

namespace Flyback.Plugins.CoreMidi;

/// <summary>
/// Entry point of the macOS MIDI plugin. Loading this assembly must not call
/// into CoreMIDI — the framework is only reached when devices are listed or one
/// is opened, so the plugin lists itself harmlessly on a machine that has no
/// such framework at all.
/// </summary>
public sealed class CoreMidiPlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new(
        "coremidi.midi",
        "CoreMIDI input",
        "Plays a patch from a MIDI keyboard, through the framework every Mac already routes MIDI over.");

    public void Register(IPluginRegistry registry) => registry.AddMidiInput(new CoreMidiInput());
}

/// <summary>
/// One thing on the machine that could be played: the server's reference for it,
/// and what to call it.
/// </summary>
/// <remarks>
/// The reference is exactly what a patch must not store — it is a number the
/// MIDI server made up when it noticed the device, and it is a different number
/// tomorrow. The name is what survives, which is what
/// <see cref="MidiPorts.Named"/> turns into an id.
/// </remarks>
internal readonly record struct MidiSource(uint Endpoint, string Name);

/// <summary>
/// Offers the backend without opening anything. On Windows and Linux this is the
/// class that answers "no", which is what keeps the plugin loadable everywhere
/// even though the devices are not.
/// </summary>
public sealed class CoreMidiInput : IMidiInput
{
    public string Id => "coremidi";

    public string Name => "CoreMIDI";

    /// <summary>
    /// The same 100 the Windows and Linux backends claim, and the sound plugins
    /// with them. The three never compete: each is supported only where the
    /// others are not.
    /// </summary>
    public int Priority => 100;

    /// <summary>
    /// One question, unlike the Linux backend: CoreMIDI is part of macOS and is
    /// on every Mac there is, so there is no second case where the operating
    /// system is right and the framework is missing.
    /// </summary>
    public bool IsSupported => OperatingSystem.IsMacOS();

    /// <summary>
    /// What is plugged in right now, asked of the MIDI server each time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Total, whatever the machine is doing. A device half-noticed, one pulled
    /// out between two calls, a server that has not started — none of it is a
    /// reason for a picker not to draw, and there is always the computer's own
    /// keyboard behind this list.
    /// </para>
    /// <para>
    /// What it lists is everything that plays notes rather than only hardware,
    /// which is the same bargain the ALSA sequencer makes: another program's
    /// virtual source and the IAC bus macOS ships for exactly this purpose
    /// appear here beside the keyboard, because to the server they are the same
    /// kind of thing.
    /// </para>
    /// </remarks>
    public IReadOnlyList<MidiPortInfo> Ports
    {
        get
        {
            if (!IsSupported) return [];

            try
            {
                return MidiPorts.Named(Sources().Select(source => source.Name));
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
    /// The id is turned back into an endpoint by asking the server again, rather
    /// than by remembering what it was when the picker was last drawn. That is
    /// the whole point of an id that is not a number: the device may have been
    /// unplugged and put back since, and something else may hold the reference
    /// it used to have.
    /// </remarks>
    public IMidiPort Open(string port, MidiCallback deliver)
    {
        ArgumentNullException.ThrowIfNull(deliver);

        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("CoreMIDI input is only available on macOS.");

        // One walk, named once: the ids below have to be the ones this very list
        // would have produced, and asking twice would be asking about two
        // different moments.
        var sources = Sources();
        var ports = MidiPorts.Named(sources.Select(source => source.Name));

        for (var index = 0; index < ports.Count; index++)
            if (string.Equals(ports[index].Id, port, StringComparison.Ordinal))
                return new CoreMidiPort(port, sources[index], deliver);

        throw new InvalidOperationException($"'{port}' is not plugged in.");
    }

    /// <summary>
    /// Every source the server has, in the order it keeps them — which is the
    /// same order twice running unless something was plugged in between.
    /// </summary>
    /// <remarks>
    /// Asking is not opening. Counting the sources and naming them reads the
    /// server's own list and touches no hardware; connecting a port to one is
    /// the call that would claim a keyboard, and only <see cref="Open"/> makes
    /// it.
    /// </remarks>
    private static IReadOnlyList<MidiSource> Sources()
    {
        var count = MidiServices.SourceCount();
        var found = new List<MidiSource>((int)count);

        for (nuint index = 0; index < count; index++)
        {
            var endpoint = MidiServices.Source(index);

            // Nought is the server saying there is nothing at that place after
            // all, which happens when a device is pulled out mid-walk. A device
            // that cannot be referred to cannot be opened either, and a picker
            // offering it would be offering a failure.
            if (endpoint == 0) continue;

            found.Add(new MidiSource(endpoint, MidiServices.NameOf(endpoint)));
        }

        return found;
    }
}
