using Flyback.Plugins.Midi;

namespace Flyback.Plugins.AlsaMidi;

/// <summary>
/// Entry point of the Linux MIDI plugin. Loading this assembly must not call
/// into libasound — the library is only reached when devices are listed or one
/// is opened, so the plugin lists itself harmlessly on a machine that has no
/// sound library at all.
/// </summary>
public sealed class AlsaMidiPlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new(
        "alsa.midi",
        "ALSA MIDI input",
        "Plays a patch from a MIDI keyboard, through the ALSA sequencer every Linux machine routes MIDI over.");

    public void Register(IPluginRegistry registry) => registry.AddMidiInput(new AlsaMidiInput());
}

/// <summary>
/// One thing on the machine that could be played: which client, which port, and
/// what to call it.
/// </summary>
/// <remarks>
/// The pair of numbers is the sequencer's address and is exactly what a patch
/// must not store — client numbers are handed out in the order things were
/// plugged in, so the keyboard that was 24 this morning is 28 after a reboot.
/// The name is what survives, which is what <see cref="MidiPorts.Named"/> turns
/// into an id.
/// </remarks>
internal readonly record struct SequencerPort(int Client, int Port, string Name);

/// <summary>
/// Offers the backend without opening anything. On Windows and macOS this is the
/// class that answers "no", and on Linux it is where a machine with no sound
/// library at all is found out.
/// </summary>
public sealed class AlsaMidiInput : IMidiInput
{
    public string Id => "alsaseq";

    public string Name => "ALSA MIDI";

    /// <summary>
    /// The same 100 the Windows backend claims, and the sound plugins with it.
    /// The three never compete: each is supported only where the others are not.
    /// </summary>
    public int Priority => 100;

    /// <summary>
    /// Two questions, because on Linux the right operating system is not enough:
    /// a container or a headless server frequently has no libasound, and this is
    /// the last moment the answer can be "no" rather than an exception.
    /// </summary>
    public bool IsSupported => OperatingSystem.IsLinux() && LibAsoundSeq.IsInstalled;

    /// <summary>
    /// What is plugged in right now, asked of the sequencer each time.
    /// </summary>
    /// <remarks>
    /// Total, whatever the machine is doing. A card half-initialised, a device
    /// pulled out between two calls, a sequencer that is not there because the
    /// kernel module was never loaded — none of it is a reason for a picker not
    /// to draw, and there is always the computer's own keyboard behind this list.
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
    /// The id is resolved against the current ALSA state, so unplugged devices can
    /// be reconnected without stale references.
    /// </remarks>
    public IMidiPort Open(string port, MidiCallback deliver)
    {
        ArgumentNullException.ThrowIfNull(deliver);

        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("ALSA MIDI input is only available on Linux.");

        if (!LibAsoundSeq.IsInstalled)
            throw new PlatformNotSupportedException("this machine has no libasound to hear a keyboard through.");

        // One walk, named once: the ids below have to be the ones this very list
        // would have produced, and asking twice would be asking about two
        // different moments.
        var sources = Sources();
        var ports = MidiPorts.Named(sources.Select(source => source.Name));

        for (var index = 0; index < ports.Count; index++)
            if (string.Equals(ports[index].Id, port, StringComparison.Ordinal))
                return new AlsaMidiPort(port, sources[index], deliver);

        throw new InvalidOperationException($"'{port}' is not plugged in.");
    }

    /// <summary>
    /// Every port on the machine that will play us something, in the order the
    /// sequencer keeps them — which is by client and then by port number, and is
    /// therefore the same order twice running unless something was plugged in
    /// between.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Opening the sequencer to ask is not opening a device. A sequencer client
    /// is a row in the kernel's routing table: it takes no card, blocks nobody,
    /// and is closed again before this returns. Nothing is subscribed to, which
    /// is the call that would actually claim a keyboard.
    /// </para>
    /// <para>
    /// The two info blocks are stack buffers of a size libasound is asked for,
    /// which is what its own <c>_alloca</c> macros do. They are opaque either
    /// way — every field goes through an accessor — so the size is the only fact
    /// about them this file has, and it comes from the library rather than from
    /// a header copied into a comment.
    /// </para>
    /// </remarks>
    private static unsafe IReadOnlyList<SequencerPort> Sources()
    {
        if (LibAsoundSeq.Open(
                out var seq, LibAsoundSeq.DefaultSequencer, LibAsoundSeq.InputStream, LibAsoundSeq.Blocking) < 0)
            return [];

        try
        {
            var found = new List<SequencerPort>();
            var ours = LibAsoundSeq.ClientId(seq);

            var client = stackalloc byte[(int)LibAsoundSeq.ClientInfoSize()];
            var port = stackalloc byte[(int)LibAsoundSeq.PortInfoSize()];

            LibAsoundSeq.ClientInfoSetClient(client, -1);

            while (LibAsoundSeq.QueryNextClient(seq, client) >= 0)
            {
                var number = LibAsoundSeq.ClientInfoGetClient(client);

                // The sequencer's own timer and announcements, and whatever
                // ports this program made for itself. Neither is an instrument.
                if (number == LibAsoundSeq.SystemClient || number == ours) continue;

                var made = LibAsoundSeq.Text(LibAsoundSeq.ClientInfoGetName(client));

                LibAsoundSeq.PortInfoSetClient(port, number);
                LibAsoundSeq.PortInfoSetPort(port, -1);

                while (LibAsoundSeq.QueryNextPort(seq, port) >= 0)
                {
                    var capability = LibAsoundSeq.PortInfoGetCapability(port);

                    if ((capability & LibAsoundSeq.Readable) != LibAsoundSeq.Readable) continue;
                    if ((capability & LibAsoundSeq.Private) != 0) continue;

                    found.Add(
                        new SequencerPort(
                            number,
                            LibAsoundSeq.PortInfoGetPort(port),
                            Display(made, LibAsoundSeq.Text(LibAsoundSeq.PortInfoGetName(port)))));
                }
            }

            return found;
        }
        finally
        {
            LibAsoundSeq.Close(seq);
        }
    }

    /// <summary>
    /// What a person should see for one port, out of the two names the sequencer
    /// has for it.
    /// </summary>
    /// <remarks>
    /// A device is a client with ports under it — "Launchkey Mini MK3" holding
    /// "Launchkey Mini MK3 MIDI 1" — and the kernel usually puts the card's name
    /// into the port's already. So the two are joined only when the port's name
    /// does not begin with the client's, which keeps the common case from
    /// reading as a stutter and keeps a port called plain "MIDI 1" from being
    /// unidentifiable.
    /// </remarks>
    private static string Display(string client, string port)
    {
        if (string.IsNullOrWhiteSpace(port)) return client;
        if (string.IsNullOrWhiteSpace(client)) return port;

        return port.StartsWith(client, StringComparison.OrdinalIgnoreCase) ? port : $"{client}: {port}";
    }
}
