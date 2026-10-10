using Android.Media.Midi;
using Flyback.Plugins.Midi;

namespace Flyback.Plugins.AndroidIO;

/// <summary>
/// One device, open and listening. The mirror of <c>WinMidiPort</c>: nothing outside this
/// assembly knows <c>android.media.midi</c> exists.
/// </summary>
/// <remarks>
/// Android opens a device on a thread of its own and says so through a listener, so opening
/// waits for the answer, a few seconds at most. What the device then sends arrives on
/// Android's thread as a stream of bytes, which <see cref="MidiStream"/> cuts into messages.
/// A device pulled out goes quiet rather than reporting itself gone, as on macOS.
/// </remarks>
internal sealed class MidiManagerPort : IMidiPort
{
    /// <summary>Longer than Android takes to open a device that will open at all.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly MidiStream stream;

    private readonly Receiver receiver;

    private MidiDevice? device;

    private MidiOutputPort? port;

    /// <summary>Read by Android's thread and written by whichever thread closes.</summary>
    private volatile bool open;

    public MidiManagerPort(string id, MidiManager manager, MidiSource source, MidiCallback deliver)
    {
        ArgumentNullException.ThrowIfNull(deliver);

        Id = id;
        stream = new MidiStream(deliver);
        receiver = new Receiver(this);

        try
        {
            device = Opened(manager, source.Device) ?? throw new InvalidOperationException($"'{id}' would not open.");

            // A device talks from its output ports, so that is where the app listens.
            port = device.OpenOutputPort(source.Port) ?? throw new InvalidOperationException($"'{id}' would not open port {source.Port}.");
            port.Connect(receiver);
            open = true;
        }
        catch
        {
            // Half an open device is worse than none: leave nothing running, so a second
            // attempt starts where the first did.
            Dispose();
            throw;
        }
    }

    public string Id { get; }

    public bool IsOpen => open;

    public void Dispose()
    {
        // First, so that bytes already on their way deliver nothing.
        open = false;

        if (port is { } listening)
        {
            port = null;

            try
            {
                listening.Disconnect(receiver);
                listening.Close();
            }
            catch
            {
                // A port that will not close cleanly is nothing anybody can act on.
            }

            listening.Dispose();
        }

        if (device is { } opened)
        {
            device = null;

            try
            {
                opened.Close();
            }
            catch
            {
                // As above: the device is let go either way.
            }

            opened.Dispose();
        }

        receiver.Dispose();
    }

    /// <summary>The device once Android has opened it, or null where it would not within <see cref="Patience"/>.</summary>
    private static MidiDevice? Opened(MidiManager manager, MidiDeviceInfo info)
    {
        var opening = new Opening();
        manager.OpenDevice(info, opening, null);

        return opening.Wait(Patience);
    }

    /// <summary>Called by Android on its own thread with whatever bytes have arrived.</summary>
    private sealed class Receiver(MidiManagerPort owner) : MidiReceiver
    {
        public override void OnSend(byte[]? msg, int offset, int count, long timestamp)
        {
            try
            {
                if (msg is null || !owner.open) return;

                owner.stream.Feed(msg.AsSpan(offset, count));
            }
            catch
            {
                // An exception unwinding into Java would take the process with it, and
                // there is nobody on this thread to tell.
            }
        }
    }

    /// <summary>
    /// Android's answer to opening a device, waited for once. An answer that comes after
    /// the wait gave up closes the device, since nobody will listen to it.
    /// </summary>
    private sealed class Opening : Java.Lang.Object, MidiManager.IOnDeviceOpenedListener
    {
        private readonly TaskCompletionSource<MidiDevice?> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void OnDeviceOpened(MidiDevice? device)
        {
            if (answer.TrySetResult(device) || device is null) return;

            try
            {
                device.Close();
            }
            catch
            {
                // Too late to be listened to, and too late to be closed.
            }
        }

        public MidiDevice? Wait(TimeSpan patience)
        {
            if (!answer.Task.Wait(patience)) answer.TrySetResult(null);

            return answer.Task.Result;
        }
    }
}
