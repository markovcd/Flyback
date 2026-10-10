using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Flyback.Core;
using Flyback.Plugins.Midi;

namespace Flyback.Plugins.MacIO;

/// <summary>
/// One device, open and listening. The mirror of <c>WinMidiPort</c>: nothing
/// outside this assembly knows CoreMIDI exists.
/// </summary>
/// <remarks>
/// The server calls us, on a high-priority thread it made for the purpose, which
/// is why this backend owns no thread the way the ALSA one has to. What happens
/// there is arithmetic on a few bytes and one delegate call, with no allocation,
/// no lock and no way out for an exception.
/// <para>
/// The callback is a static function pointer with the port handed to it as
/// context, so no delegate has to be kept alive by hand — the same arrangement
/// <c>CoreAudioDevice</c> uses. A client of its own per open device, which is what
/// makes closing a device a matter of closing everything it owns.
/// </para>
/// <para>
/// A device pulled out goes quiet rather than reporting itself gone: finding out
/// would mean a notification callback and a run loop to deliver it on, and nothing
/// above this line asks.
/// </para>
/// </remarks>
internal sealed unsafe class CoreMidiPort : IMidiPort
{
    /// <summary>What this program is called in Audio MIDI Setup, and in every other program's picker.</summary>
    private const string ClientName = GlobalConstants.ApplicationName;

    private const string PortName = "MIDI In";

    /// <summary>Cuts each packet's bytes into messages and delivers them.</summary>
    private readonly MidiStream stream;

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
        stream = new MidiStream(deliver);
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
            if (source != 0) _ = MidiServices.DisconnectSource(port, source);

            _ = MidiServices.DisposePort(port);
            port = 0;
        }

        source = 0;

        if (client != 0)
        {
            _ = MidiServices.DisposeClient(client);
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
    /// <remarks>
    /// Apple's rule is that every message in a packet is complete and carries its own
    /// status byte, and that a packet holding a system-exclusive message holds nothing
    /// else, so the stream reader never has to carry half a message from one packet to
    /// the next; it reads the packet's bytes as it would any wire's.
    /// </remarks>
    private void Read(byte* packets)
    {
        var count = MidiServices.PacketCount(packets);
        var packet = MidiServices.FirstPacket(packets);

        for (var index = 0u; index < count; index++)
        {
            stream.Feed(new ReadOnlySpan<byte>(MidiServices.PacketData(packet), MidiServices.PacketLength(packet)));

            packet = MidiServices.NextPacket(packet);
        }
    }

    private static void Check(int status, string what)
    {
        if (status == MidiServices.NoError) return;

        throw new InvalidOperationException($"could not {what}: {MidiServices.Describe(status)}.");
    }
}
