using System.Text;

namespace Flyback.Plugins.WinIO;

/// <summary>
/// One loaded ASIO driver, called through its vtable. <c>IASIO</c> is a COM object in shape only:
/// it has no proxy, so every call is made on the thread that created it (<see cref="AsioThread"/>).
/// </summary>
/// <remarks>
/// Its methods are C++ virtuals with no calling convention named, so thiscall on 32-bit Windows
/// and the platform's own everywhere else.
/// </remarks>
/// <param name="instance">The <c>IASIO*</c>, owned from here on.</param>
internal sealed unsafe class AsioDriver(IntPtr instance) : IDisposable
{
    private IntPtr instance = instance;

    private void* Slot(int index) => (*(void***)instance)[index];

    public bool Init(IntPtr window) => ((delegate* unmanaged[Thiscall]<IntPtr, IntPtr, int>)Slot(3))(instance, window) != 0;

    /// <summary>What the driver says went wrong last, which it may leave empty.</summary>
    public string ErrorMessage
    {
        get
        {
            var text = stackalloc byte[256];
            new Span<byte>(text, 256).Clear();

            ((delegate* unmanaged[Thiscall]<IntPtr, byte*, void>)Slot(6))(instance, text);

            return Text(new ReadOnlySpan<byte>(text, 255));
        }
    }

    public int Start() => Call(7);

    public int Stop() => Call(8);

    public (int Inputs, int Outputs) Channels()
    {
        int inputs = 0, outputs = 0;
        ((delegate* unmanaged[Thiscall]<IntPtr, int*, int*, int>)Slot(9))(instance, &inputs, &outputs);
        return (inputs, outputs);
    }

    public (int Input, int Output) Latencies()
    {
        int input = 0, output = 0;
        ((delegate* unmanaged[Thiscall]<IntPtr, int*, int*, int>)Slot(10))(instance, &input, &output);
        return (input, output);
    }

    /// <summary>The block size the driver prefers, in frames, which its own control panel sets.</summary>
    public int PreferredBufferSize()
    {
        int least = 0, most = 0, preferred = 0, step = 0;
        ((delegate* unmanaged[Thiscall]<IntPtr, int*, int*, int*, int*, int>)Slot(11))(instance, &least, &most, &preferred, &step);
        return preferred;
    }

    public bool CanSampleRate(double rate) => ((delegate* unmanaged[Thiscall]<IntPtr, double, int>)Slot(12))(instance, rate) == Asio.Ok;

    /// <summary>The rate the driver runs at, or 0 where it has none to give.</summary>
    public int SampleRate()
    {
        var rate = 0.0;
        var status = ((delegate* unmanaged[Thiscall]<IntPtr, double*, int>)Slot(13))(instance, &rate);
        return status == Asio.Ok && rate > 0 ? (int)Math.Round(rate) : 0;
    }

    public int SetSampleRate(double rate) => ((delegate* unmanaged[Thiscall]<IntPtr, double, int>)Slot(14))(instance, rate);

    /// <summary>The sample type an output channel is written in.</summary>
    public int OutputType(int channel)
    {
        var info = new Asio.ChannelInfo { Channel = channel, IsInput = 0 };
        var status = ((delegate* unmanaged[Thiscall]<IntPtr, Asio.ChannelInfo*, int>)Slot(18))(instance, &info);

        return status == Asio.Ok ? info.Type : -1;
    }

    public int CreateBuffers(Span<Asio.BufferInfo> buffers, int frames, Asio.Callbacks* callbacks)
    {
        fixed (Asio.BufferInfo* first = buffers)
            return ((delegate* unmanaged[Thiscall]<IntPtr, Asio.BufferInfo*, int, int, Asio.Callbacks*, int>)Slot(19))(instance, first, buffers.Length, frames, callbacks);
    }

    public int DisposeBuffers() => Call(20);

    /// <summary>Tells the driver the block it is about to play is written; <see cref="Asio.Ok"/> where it listens.</summary>
    public int OutputReady() => Call(23);

    /// <summary>Releases the driver, which unloads it once nothing else holds it.</summary>
    public void Dispose()
    {
        if (instance == IntPtr.Zero) return;

        ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Slot(2))(instance);
        instance = IntPtr.Zero;
    }

    private int Call(int slot) => ((delegate* unmanaged[Thiscall]<IntPtr, int>)Slot(slot))(instance);

    private static string Text(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);

        return Encoding.Latin1.GetString(end < 0 ? bytes : bytes[..end]).Trim();
    }
}
