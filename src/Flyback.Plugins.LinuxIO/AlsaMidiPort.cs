using Flyback.Core;
using Flyback.Plugins.Midi;

namespace Flyback.Plugins.LinuxIO;

/// <summary>
/// One device, open and listening. The mirror of <c>WinMidiPort</c>: nothing
/// outside this assembly knows the ALSA sequencer exists.
/// </summary>
/// <remarks>
/// The odd one of the backends. winmm and CoreMIDI call us on a thread the driver
/// owns; the sequencer is a file that has events in it, so this owns a thread —
/// which changes nothing in the contract, since <see cref="MidiCallback"/> only
/// ever said it is not the window's thread.
/// <para>
/// <b>The reader polls rather than blocks.</b> A device is opened and handed back
/// whenever the patch is rewired, and a thread parked inside a read on a handle we
/// are about to close is a crash, while one waiting for a note that may not come
/// for an hour never sees that it was asked to stop. So the sequencer is opened
/// non-blocking and the reader wakes every millisecond — a fortieth of the block
/// the sound backend already asks for.
/// </para>
/// <para>
/// Every call on the handle after the constructor is made from that thread, which
/// is what alsa-lib asks for, so closing waits for the reader to finish. A device
/// pulled out goes quiet rather than reporting itself gone: nothing above this
/// line asks, since the hub opens and closes devices off what the compiled
/// programs read.
/// </para>
/// </remarks>
internal sealed unsafe class AlsaMidiPort : IMidiPort
{
    /// <summary>
    /// How long the reader sleeps between drains. The whole of the latency this
    /// backend adds, and see the note above for why it is not nought.
    /// </summary>
    private const int QuietMilliseconds = 1;

    /// <summary>
    /// Long enough that a reader merely descheduled is not mistaken for one that
    /// has stopped: it checks the flag every millisecond and blocks on nothing.
    /// </summary>
    private const int ReaderExitMilliseconds = 2000;

    /// <summary>
    /// Three bytes is every message this program reads, and a little room over
    /// so that a longer one decodes and is ignored rather than failing to decode
    /// and being retried.
    /// </summary>
    private const int MessageBytes = 12;

    private readonly MidiCallback deliver;

    private readonly Thread? reader;

    private IntPtr seq;

    private IntPtr decoder;

    /// <summary>
    /// Read by the reader thread and written by whichever thread closes, which
    /// is why it is volatile rather than merely a bool.
    /// </summary>
    private volatile bool open;

    public AlsaMidiPort(string id, SequencerPort source, MidiCallback deliver)
    {
        ArgumentNullException.ThrowIfNull(deliver);

        Id = id;
        this.deliver = deliver;

        try
        {
            Check(
                LibAsoundSeq.Open(
                    out seq, LibAsoundSeq.DefaultSequencer, LibAsoundSeq.InputStream, LibAsoundSeq.NonBlocking),
                $"open the sequencer to hear '{id}'");

            // What this program is called in everyone else's patch bay. Failing
            // to be named is not failing to hear, so it is not checked.
            LibAsoundSeq.SetClientName(seq, GlobalConstants.ApplicationName);

            var port = LibAsoundSeq.CreateSimplePort(
                seq, "MIDI In", LibAsoundSeq.Writable, LibAsoundSeq.GenericMidi | LibAsoundSeq.Application);

            Check(port, $"make a port to hear '{id}' on");

            Check(
                LibAsoundSeq.ConnectFrom(seq, port, source.Client, source.Port),
                $"listen to '{id}'");

            Check(LibAsoundSeq.NewDecoder(MessageBytes, out decoder), $"decode what '{id}' sends");

            LibAsoundSeq.NoRunningStatus(decoder, 1);

            open = true;

            reader = new Thread(Listen) { IsBackground = true, Name = $"{GlobalConstants.ApplicationName} MIDI ({id})" };
            reader.Start();
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
        // First, so that the reader stops delivering and comes back within a
        // millisecond of being asked.
        open = false;

        if (reader is { IsAlive: true } thread && !thread.Join(ReaderExitMilliseconds))
        {
            // A reader that has stopped answering, which nothing here can
            // explain. Leaking the handle is the lesser fault: closing it under
            // a thread that is still reading from it would take the program with
            // it, and this is exactly the bargain AlsaAudioDevice makes with its
            // writer.
            seq = IntPtr.Zero;
            decoder = IntPtr.Zero;
            return;
        }

        if (decoder != IntPtr.Zero)
        {
            LibAsoundSeq.FreeDecoder(decoder);
            decoder = IntPtr.Zero;
        }

        // Closing the client takes the port and the subscription with it, so
        // the keyboard is free for whatever else wants it the moment this
        // returns.
        if (seq != IntPtr.Zero)
        {
            LibAsoundSeq.Close(seq);
            seq = IntPtr.Zero;
        }
    }

    /// <summary>
    /// The reader thread. Empties whatever the sequencer has, sleeps, and does
    /// it again for as long as the port is wanted.
    /// </summary>
    private void Listen()
    {
        try
        {
            while (open)
            {
                if (!Drain()) break;

                Thread.Sleep(QuietMilliseconds);
            }
        }
        catch
        {
            // Nothing on this thread can report anything, and an exception
            // leaving it would end the process rather than the music.
        }

        open = false;
    }

    /// <summary>
    /// Everything queued, delivered. False means the sequencer has gone and the
    /// loop should end — a hot core reading an error over and over is worse than
    /// a silent instrument.
    /// </summary>
    private bool Drain()
    {
        while (open)
        {
            var status = LibAsoundSeq.EventInput(seq, out var message);

            if (status == LibAsoundSeq.Nothing) return true;

            // The queue overflowed and has already been emptied for us. Notes
            // were lost, which cannot be undone, and the next one will arrive
            // normally.
            if (status == LibAsoundSeq.Overrun) continue;

            if (status < 0) return false;

            Decode(message);
        }

        return true;
    }

    /// <summary>
    /// One event, unpacked. The sequencer hands over a decoded struct rather than
    /// the bytes a cable carried, so libasound's own converter puts them back —
    /// shorter than reading the struct, and the only version that cannot be wrong
    /// about where a union began.
    /// </summary>
    /// <remarks>
    /// What the bytes mean is <see cref="MidiMessages.Of"/>, in the contract rather
    /// than here, because it is the same on every platform. Everything that is not
    /// a note comes back as a negative count here or as null there.
    /// </remarks>
    private void Decode(IntPtr message)
    {
        var bytes = stackalloc byte[MessageBytes];

        var count = LibAsoundSeq.Decode(decoder, bytes, MessageBytes, message);

        // Under two bytes cannot be a note either way, and a negative count is
        // an event that was never MIDI to begin with.
        if (count < 2) return;

        var read = MidiMessages.Of(bytes[0], bytes[1], count > 2 ? bytes[2] : (byte)0);

        if (read is not { } note) return;

        try
        {
            deliver(note);
        }
        catch
        {
            // Whoever is listening threw. One dropped note is the answer that
            // leaves the rest of the performance playing.
        }
    }

    private static void Check(int status, string what)
    {
        if (status >= 0) return;

        throw new InvalidOperationException($"could not {what}: {LibAsound.Describe(status)}.");
    }
}
