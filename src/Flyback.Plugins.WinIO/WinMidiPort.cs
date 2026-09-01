using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Flyback.Plugins.Midi;

namespace Flyback.Plugins.WinIO;

/// <summary>
/// One device, open and listening. The mirror of the WASAPI device: nothing
/// outside this assembly knows winmm exists, and nothing outside it is
/// Windows-only.
/// </summary>
/// <remarks>
/// <para>
/// The driver calls us rather than the other way round, and it calls on a thread
/// of its own. That thread is documented as a restricted one — the rule is that
/// almost nothing may be called from inside it — so what happens there is kept to
/// arithmetic on five bytes and one delegate call, with no allocation, no lock
/// and no way out for an exception. Everything the note then touches is the
/// hub's problem, and the hub is written knowing which thread it is on.
/// </para>
/// <para>
/// The callback is a static function pointer with the port handed to it as
/// context, so no delegate has to be kept alive by hand and no marshalling stub
/// sits between the driver and the note — the same arrangement
/// <c>CoreAudioDevice</c> uses for its render callback, and for the same reasons.
/// </para>
/// </remarks>
internal sealed unsafe class WinMidiPort : IMidiPort
{
    private readonly MidiCallback deliver;

    /// <summary>Keeps this instance findable from the callback's context pointer.</summary>
    private GCHandle self;

    private IntPtr device;

    /// <summary>
    /// Read by the driver's thread and written by whichever thread closes, which
    /// is why it is volatile rather than merely a bool.
    /// </summary>
    private volatile bool open;

    public WinMidiPort(string id, uint index, MidiCallback deliver)
    {
        ArgumentNullException.ThrowIfNull(deliver);

        Id = id;
        this.deliver = deliver;
        self = GCHandle.Alloc(this);

        try
        {
            Check(
                WinMm.Open(
                    out device,
                    index,
                    (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, nuint, nuint, void>)&Receive,
                    GCHandle.ToIntPtr(self),
                    WinMm.CallbackFunction),
                $"open '{id}'");

            Check(WinMm.Start(device), $"start listening to '{id}'");

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

        if (device != IntPtr.Zero)
        {
            // Stop, hand back, close, in that order. Reset is what makes the
            // close succeed rather than being refused for a device that is still
            // holding something.
            WinMm.Stop(device);
            WinMm.Reset(device);
            WinMm.Close(device);

            device = IntPtr.Zero;
        }

        // After the close rather than before it, because the close is what
        // delivers the last callback — one that would otherwise be looking up a
        // handle that had already gone.
        if (self.IsAllocated) self.Free();
    }

    /// <summary>
    /// Called by the driver on its own thread, for every message the device
    /// sends. Anything that is not a short message is ignored here rather than
    /// decoded and discarded a layer down.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void Receive(IntPtr device, uint message, IntPtr instance, nuint first, nuint second)
    {
        try
        {
            if (message != WinMm.DataMessage) return;

            if (GCHandle.FromIntPtr(instance).Target is not WinMidiPort port || !port.open) return;

            port.Decode((uint)first);
        }
        catch
        {
            // An exception unwinding into C would take the process with it, and
            // there is nobody on this thread to tell. A dropped note is the only
            // answer that leaves the program alive.
        }
    }

    /// <summary>
    /// One short message, unpacked. Windows delivers all three bytes in a single
    /// word — status lowest, then the two data bytes — and the unpacking is the
    /// whole of what is Windows-specific about it.
    /// </summary>
    /// <remarks>
    /// What the bytes then mean is <see cref="MidiMessages.Of"/>, in the contract
    /// rather than here, because it is the same on every platform and the two
    /// backends still to be written will read them the same way.
    /// </remarks>
    private void Decode(uint packed)
    {
        if (MidiMessages.Of((byte)packed, (byte)(packed >> 8), (byte)(packed >> 16)) is { } message)
            deliver(message);
    }

    private static void Check(uint status, string what)
    {
        if (status == WinMm.Ok) return;

        throw new InvalidOperationException($"could not {what}: {WinMm.Describe(status)}.");
    }
}
