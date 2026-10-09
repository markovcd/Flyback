using System.Runtime.InteropServices;

namespace Flyback.Plugins.WinIO.Tests;

/// <summary>
/// An ASIO driver in memory: a vtable in the slots a real driver's are, so the device calls it
/// exactly as it calls one loaded from a DLL. The test plays the driver's audio thread by calling
/// <see cref="Switch"/>.
/// </summary>
internal sealed unsafe class FakeAsioDriver : IDisposable
{
    private static readonly void** Table = Vtable();

    private readonly IntPtr instance;
    private GCHandle self;
    private readonly List<IntPtr> allocated = [];

    public FakeAsioDriver()
    {
        self = GCHandle.Alloc(this);
        instance = (IntPtr)NativeMemory.AllocZeroed(2, (nuint)sizeof(IntPtr));
        ((void**)instance)[0] = Table;
        ((IntPtr*)instance)[1] = GCHandle.ToIntPtr(self);
    }

    public double Rate { get; set; } = 48_000;

    public bool AcceptsAnyRate { get; set; } = true;

    public int Outputs { get; set; } = 8;

    public int Frames { get; set; } = 256;

    public int Type { get; set; } = 18;

    public int OutputLatency { get; set; } = 300;

    public bool ListensForReady { get; set; } = true;

    public int Loads { get; private set; }

    public bool Started { get; private set; }

    public bool Released { get; private set; }

    public int ReadyCalls { get; private set; }

    public int BuffersMade { get; private set; }

    public int BuffersDisposed { get; private set; }

    /// <summary>The two halves of each output buffer the device asked for: [half][channel].</summary>
    public IntPtr[,] Halves { get; private set; } = new IntPtr[2, 0];

    private Asio.Callbacks* callbacks;

    /// <summary>What the device loads: this driver, as though created afresh.</summary>
    public AsioDriver Load()
    {
        Loads++;
        Released = false;

        return new AsioDriver(instance);
    }

    /// <summary>Asks the device to fill one half, as the driver's audio thread does.</summary>
    public void Switch(int half) => callbacks->BufferSwitch(half, 1);

    public int Message(int selector, int value) => callbacks->Message(selector, value, IntPtr.Zero, null);

    public float[] Floats(int half, int channel) => new ReadOnlySpan<float>((void*)Halves[half, channel], Frames).ToArray();

    public int[] Ints(int half, int channel) => new ReadOnlySpan<int>((void*)Halves[half, channel], Frames).ToArray();

    public void Dispose()
    {
        foreach (var block in allocated) NativeMemory.Free((void*)block);

        NativeMemory.Free((void*)instance);
        self.Free();
    }

    private static FakeAsioDriver Of(IntPtr instance) => (FakeAsioDriver)GCHandle.FromIntPtr(((IntPtr*)instance)[1]).Target!;

    private static void** Vtable()
    {
        var table = (void**)NativeMemory.AllocZeroed(24, (nuint)sizeof(IntPtr));

        table[2] = (delegate* unmanaged<IntPtr, uint>)&Release;
        table[3] = (delegate* unmanaged<IntPtr, IntPtr, int>)&Init;
        table[6] = (delegate* unmanaged<IntPtr, byte*, void>)&ErrorMessage;
        table[7] = (delegate* unmanaged<IntPtr, int>)&Start;
        table[8] = (delegate* unmanaged<IntPtr, int>)&Stop;
        table[9] = (delegate* unmanaged<IntPtr, int*, int*, int>)&Channels;
        table[10] = (delegate* unmanaged<IntPtr, int*, int*, int>)&Latencies;
        table[11] = (delegate* unmanaged<IntPtr, int*, int*, int*, int*, int>)&BufferSize;
        table[12] = (delegate* unmanaged<IntPtr, double, int>)&CanSampleRate;
        table[13] = (delegate* unmanaged<IntPtr, double*, int>)&SampleRate;
        table[14] = (delegate* unmanaged<IntPtr, double, int>)&SetSampleRate;
        table[18] = (delegate* unmanaged<IntPtr, Asio.ChannelInfo*, int>)&ChannelInfo;
        table[19] = (delegate* unmanaged<IntPtr, Asio.BufferInfo*, int, int, Asio.Callbacks*, int>)&CreateBuffers;
        table[20] = (delegate* unmanaged<IntPtr, int>)&DisposeBuffers;
        table[23] = (delegate* unmanaged<IntPtr, int>)&OutputReady;

        return table;
    }

    [UnmanagedCallersOnly]
    private static uint Release(IntPtr instance)
    {
        Of(instance).Released = true;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int Init(IntPtr instance, IntPtr window) => 1;

    [UnmanagedCallersOnly]
    private static void ErrorMessage(IntPtr instance, byte* text) => "Fake driver says no"u8.CopyTo(new Span<byte>(text, 124));

    [UnmanagedCallersOnly]
    private static int Start(IntPtr instance)
    {
        Of(instance).Started = true;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int Stop(IntPtr instance)
    {
        Of(instance).Started = false;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int Channels(IntPtr instance, int* inputs, int* outputs)
    {
        *inputs = 2;
        *outputs = Of(instance).Outputs;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int Latencies(IntPtr instance, int* input, int* output)
    {
        *input = 0;
        *output = Of(instance).OutputLatency;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int BufferSize(IntPtr instance, int* least, int* most, int* preferred, int* step)
    {
        *least = 64;
        *most = 2048;
        *preferred = Of(instance).Frames;
        *step = -1;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int CanSampleRate(IntPtr instance, double rate) => Of(instance).AcceptsAnyRate ? 0 : -997;

    [UnmanagedCallersOnly]
    private static int SampleRate(IntPtr instance, double* rate)
    {
        *rate = Of(instance).Rate;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int SetSampleRate(IntPtr instance, double rate)
    {
        Of(instance).Rate = rate;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int ChannelInfo(IntPtr instance, Asio.ChannelInfo* info)
    {
        info->Type = Of(instance).Type;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int CreateBuffers(IntPtr instance, Asio.BufferInfo* infos, int count, int frames, Asio.Callbacks* callbacks)
    {
        var driver = Of(instance);
        var width = Asio.Width(driver.Type);

        driver.Halves = new IntPtr[2, count];
        driver.callbacks = callbacks;
        driver.BuffersMade++;

        for (var channel = 0; channel < count; channel++)
        {
            for (var half = 0; half < 2; half++)
            {
                var block = (IntPtr)NativeMemory.AllocZeroed((nuint)(frames * Math.Max(width, 1)));
                driver.allocated.Add(block);
                driver.Halves[half, channel] = block;
            }

            infos[channel].First = driver.Halves[0, channel];
            infos[channel].Second = driver.Halves[1, channel];
        }

        return 0;
    }

    [UnmanagedCallersOnly]
    private static int DisposeBuffers(IntPtr instance)
    {
        Of(instance).BuffersDisposed++;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int OutputReady(IntPtr instance)
    {
        var driver = Of(instance);
        driver.ReadyCalls++;
        return driver.ListensForReady ? 0 : -1000;
    }
}
