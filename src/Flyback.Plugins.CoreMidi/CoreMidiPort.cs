using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Flyback.Plugins.Midi;

namespace Flyback.Plugins.CoreMidi;

/// <summary>
/// One device, open and listening. The mirror of <c>WinMidiPort</c>: nothing
/// outside this assembly knows CoreMIDI exists, and nothing outside it is
/// macOS-only.
/// </summary>
/// <remarks>
/// <para>
/// The server calls us rather than the other way round, and it calls on a thread
/// of its own — a high-priority one it made for the purpose, which is why this
/// backend owns no thread the way the ALSA one has to. What happens on that
/// thread is kept to arithmetic on a few bytes and one delegate call, with no
/// allocation, no lock and no way out for an exception. Everything the note then
/// touches is the hub's problem, and the hub is written knowing which thread it
/// is on.
/// </para>
/// <para>
/// The callback is a static function pointer with the port handed to it as
/// context, so no delegate has to be kept alive by hand and no marshalling stub
/// sits between the server and the note — the same arrangement
/// <c>CoreAudioDevice</c> uses for its render callback, and <c>WinMidiPort</c>
/// for its driver's.
/// </para>
/// <para>
/// A client of its own per open device, rather than one shared between them.
/// CoreMIDI would allow either, and one per device is what makes closing a
/// device a matter of closing everything it owns — which is the shape every
/// other backend here has, and the reason none of them needs to know what else
/// is open.
/// </para>
/// <para>
/// A device pulled out of the machine goes quiet rather than reporting itself
/// gone: the server drops the connection and says nothing here, so
/// <see cref="IsOpen"/> stays true until somebody closes this. Finding out would
/// mean a notification callback and a run loop to deliver it on, and nothing
/// above this line asks — the hub opens and closes devices off what the compiled
/// programs read and never enquires after one it has not closed itself.
/// </para>
/// </remarks>
internal sealed unsafe class CoreMidiPort : IMidiPort
{
    /// <summary>What this program is called in Audio MIDI Setup, and in every other program's picker.</summary>
    private const string ClientName = "Flyback";

    private const string PortName = "MIDI In";

    private readonly MidiCallback deliver;

    /// <summary>Keeps this instance findable from the callback's context pointer.</summary>
    private GCHandle self;

    private uint client;

    private uint port;

    private uint source;

    /// <summary>
    /// Read by the server's thread and written by whichever thread closes, which
    /// is why it is volatile rather than merely a bool.
    /// </summary>
    private volatile bool open;

    public CoreMidiPort(string id, MidiSource device, MidiCallback deliver)
    {
        ArgumentNullException.ThrowIfNull(deliver);

        Id = id;
        this.deliver = deliver;
        source = device.Endpoint;
        self = GCHandle.Alloc(this);

        try
        {
            // Each name is made, used and let go on the spot: the calls copy
            // what they need, so nothing here outlives the lines it is made on.
            var clientName = CoreFoundation.NewText(ClientName);

            try
            {
                Check(
                    MidiServices.CreateClient(clientName, IntPtr.Zero, IntPtr.Zero, out client),
                    $"introduce ourselves to the MIDI server to hear '{id}'");
            }
            finally
            {
                CoreFoundation.ReleaseIfAny(clientName);
            }

            var portName = CoreFoundation.NewText(PortName);

            try
            {
                Check(
                    MidiServices.CreateInputPort(
                        client,
                        portName,
                        (IntPtr)(delegate* unmanaged[Cdecl]<byte*, IntPtr, IntPtr, void>)&Receive,
                        GCHandle.ToIntPtr(self),
                        out port),
                    $"make a port to hear '{id}' on");
            }
            finally
            {
                CoreFoundation.ReleaseIfAny(portName);
            }

            Check(MidiServices.ConnectSource(port, source, IntPtr.Zero), $"listen to '{id}'");

            open = true;
        }
        catch
        {
            // Half an open device is worse than none: leave nothing running and
            // nothing allocated, so a second attempt starts where the first did.
            Dispose();
            throw;
        }
    }

    public string Id { get; }

    public bool IsOpen => open;

    public void Dispose()
    {
        // First, so that a callback already on its way delivers nothing. It does
        // not close the window entirely — see Receive, which is written to
        // survive arriving late.
        open = false;

        // Disconnect, then the port, then the client, in that order — each is
        // held by the one after it, and the client alone would take all three.
        // Saying all of it is what makes the keyboard free for whatever else
        // wants it the moment this returns, rather than whenever a finalizer
        // gets round to it.
        if (port != 0)
        {
            if (source != 0) MidiServices.DisconnectSource(port, source);

            MidiServices.DisposePort(port);
            port = 0;
        }

        source = 0;

        if (client != 0)
        {
            MidiServices.DisposeClient(client);
            client = 0;
        }

        // After the disposals rather than before them, because disposing is what
        // stops the callbacks — one arriving after this would be looking up a
        // handle that had already gone.
        if (self.IsAllocated) self.Free();
    }

    /// <summary>
    /// Called by the server on its own thread, with everything that arrived
    /// since it last called.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Receive(byte* packets, IntPtr instance, IntPtr connection)
    {
        try
        {
            if (GCHandle.FromIntPtr(instance).Target is not CoreMidiPort port || !port.open) return;

            port.Read(packets);
        }
        catch
        {
            // An exception unwinding into C would take the process with it, and
            // there is nobody on this thread to tell. A dropped note is the only
            // answer that leaves the program alive.
        }
    }

    /// <summary>
    /// One list, packet by packet. A packet is a run of messages that arrived at
    /// the same instant, and there may be several packets in a list because the
    /// server hands over everything it has been holding at once.
    /// </summary>
    private void Read(byte* packets)
    {
        var count = MidiServices.PacketCount(packets);
        var packet = MidiServices.FirstPacket(packets);

        for (var index = 0u; index < count; index++)
        {
            Decode(MidiServices.PacketData(packet), MidiServices.PacketLength(packet));

            packet = MidiServices.NextPacket(packet);
        }
    }

    /// <summary>
    /// One packet's bytes, cut into messages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The part that is CoreMIDI's alone. winmm delivers exactly one message per
    /// callback and the ALSA sequencer has a decoder that produces one; a packet
    /// here is a run of them — three keys struck in the same millisecond arrive
    /// together — so somebody has to say where each ends. Apple's rule is what
    /// makes that possible without a parser: every message in a packet is
    /// complete and carries its own status byte, running status never appears,
    /// and a packet holding a system-exclusive message holds nothing else.
    /// </para>
    /// <para>
    /// What the bytes then mean is <see cref="MidiMessages.Of"/>, in the contract
    /// rather than here, because it is the same on every platform and the other
    /// two backends already read them that way.
    /// </para>
    /// </remarks>
    private void Decode(byte* data, int length)
    {
        var at = 0;

        while (at < length)
        {
            var status = data[at];

            // A data byte where a message should have begun: the tail of a
            // system-exclusive message split across packets. Nothing else can be
            // read out of what follows it.
            if (status < 0x80) return;

            var size = MessageBytes(status);

            // System-exclusive, which runs to the end of the packet and is the
            // only message this cannot step over.
            if (size == 0) return;

            // A message cut short by the end of the packet, which Apple says
            // cannot happen. Believing the length rather than the promise costs
            // one comparison.
            if (at + size > length) return;

            var read = MidiMessages.Of(
                status,
                size > 1 ? data[at + 1] : (byte)0,
                size > 2 ? data[at + 2] : (byte)0);

            at += size;

            if (read is not { } message) continue;

            try
            {
                deliver(message);
            }
            catch
            {
                // Whoever is listening threw. One dropped note is the answer
                // that leaves the rest of the packet playing.
            }
        }
    }

    /// <summary>
    /// How long a message is, from its status byte. Nought for system-exclusive,
    /// which has no length until its end byte turns up.
    /// </summary>
    /// <remarks>
    /// Here rather than in the contract beside <see cref="MidiMessages.Of"/>,
    /// even though it is a fact about MIDI and not about macOS: it is the answer
    /// to a question only this backend asks, because it is the only one handed
    /// more than one message at a time. A second backend that needed it would be
    /// the moment to move it.
    /// </remarks>
    private static int MessageBytes(byte status) => status switch
    {
        // Clock, start, stop, sensing and the rest of real time, which may
        // appear between any two bytes of anything else.
        >= 0xF8 => 1,

        0xF0 => 0,

        // Quarter frame and song select carry one byte; song position carries
        // two; the rest of system common carries none.
        0xF1 or 0xF3 => 2,
        0xF2 => 3,
        >= 0xF4 => 1,

        // Program change and channel pressure, the two channel messages with a
        // single byte after them.
        >= 0xC0 and <= 0xDF => 2,

        // Notes, aftertouch, controllers and the wheel.
        _ => 3,
    };

    private static void Check(int status, string what)
    {
        if (status == MidiServices.NoError) return;

        throw new InvalidOperationException($"could not {what}: {MidiServices.Describe(status)}.");
    }
}
