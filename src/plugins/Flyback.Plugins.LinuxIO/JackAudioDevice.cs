using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Flyback.Core;
using Flyback.Plugins.Audio;

namespace Flyback.Plugins.LinuxIO;

/// <summary>
/// Output to a JACK server as two ports, <c>out_left</c> and <c>out_right</c>.
/// </summary>
/// <remarks>
/// JACK calls us, once per cycle, on a thread of its own, so there is no writer thread. The
/// server fixes the sample rate and the block size, and tells a client the rate only once
/// it is connected, so the client is opened when the device is made; no port exists and
/// nothing is called until <see cref="Start"/>.
/// </remarks>
/// <param name="connectToSystem">Whether to patch the ports into the first two physical playback ports.</param>
public sealed unsafe class JackAudioDevice : IAudioDevice
{
    private readonly bool connectToSystem;

    private IntPtr client;
    private IntPtr left;
    private IntPtr right;
    private GCHandle self;
    private float[] block = [];
    private AudioCallback? fill;
    private volatile bool running;

    public JackAudioDevice(bool connectToSystem)
    {
        this.connectToSystem = connectToSystem;

        Open();
    }

    /// <summary>The server's rate, which the engine then renders at.</summary>
    public int SampleRate { get; private set; }

    /// <summary>One server period: the least a block takes to reach the driver.</summary>
    public TimeSpan Latency { get; private set; }

    public bool IsRunning => running;

    public void Start(AudioCallback fill)
    {
        if (running) return;

        // A server that went away leaves a dead client behind; start over.
        if (left != IntPtr.Zero) Teardown();

        Open();

        try
        {
            this.fill = fill;
            block = new float[Math.Max(64, (int)LibJack.BufferSize(client)) * 2];
            self = GCHandle.Alloc(this);

            left = Register("out_left");
            right = Register("out_right");

            Check(LibJack.SetProcessCallback(client, &Process, GCHandle.ToIntPtr(self)), "set the process callback");
            LibJack.OnShutdown(client, &Shutdown, GCHandle.ToIntPtr(self));

            running = true;
            Check(LibJack.Activate(client), "activate the client");

            if (connectToSystem) ConnectToSystem();
        }
        catch
        {
            Teardown();
            throw;
        }
    }

    public void Stop() => Teardown();

    public void Dispose() => Teardown();

    private void Open()
    {
        if (client != IntPtr.Zero) return;

        var opened = LibJack.Open(GlobalConstants.ApplicationName, LibJack.NoStartServer | LibJack.ServerName, out var status, JackServer.Name);

        if (opened == IntPtr.Zero)
            throw new InvalidOperationException($"could not connect to the JACK server (status {status}).");

        client = opened;
        SampleRate = (int)LibJack.SampleRate(opened);
        Latency = TimeSpan.FromSeconds(LibJack.BufferSize(opened) / (double)SampleRate);
    }

    private IntPtr Register(string name)
    {
        var port = LibJack.RegisterPort(client, name, LibJack.AudioType, LibJack.PortIsOutput, 0);

        if (port == IntPtr.Zero) throw new InvalidOperationException($"could not register the '{name}' port.");

        return port;
    }

    private void ConnectToSystem()
    {
        var playback = LibJack.AudioPorts(client, LibJack.PortIsInput | LibJack.PortIsPhysical);

        if (playback.Count == 0) return;

        _ = LibJack.Link(client, LibJack.PortName(left), playback[0]);
        _ = LibJack.Link(client, LibJack.PortName(right), playback[Math.Min(1, playback.Count - 1)]);
    }

    /// <summary>
    /// Deactivating returns only once the process callback has finished, so nothing below runs
    /// under it.
    /// </summary>
    private void Teardown()
    {
        running = false;

        if (client != IntPtr.Zero)
        {
            _ = LibJack.Deactivate(client);
            _ = LibJack.Close(client);
            client = IntPtr.Zero;
        }

        left = right = IntPtr.Zero;
        fill = null;

        if (self.IsAllocated) self.Free();
    }

    /// <summary>Runs one cycle on JACK's thread: renders blocks into the two port buffers.</summary>
    private int Cycle(uint frames)
    {
        var outLeft = LibJack.PortBuffer(left, frames);
        var outRight = LibJack.PortBuffer(right, frames);
        var done = 0;

        try
        {
            if (fill is { } deliver)
            {
                var capacity = block.Length / 2;

                while (done < frames)
                {
                    var count = Math.Min(capacity, (int)frames - done);
                    var chunk = block.AsSpan(0, count * 2);

                    deliver(chunk);

                    for (var i = 0; i < count; i++)
                    {
                        outLeft[done + i] = chunk[2 * i];
                        outRight[done + i] = chunk[2 * i + 1];
                    }

                    done += count;
                }
            }
        }
        catch
        {
            // Nothing on this thread can report anything; the rest of the cycle is silence.
        }

        for (var i = done; i < frames; i++)
        {
            outLeft[i] = 0f;
            outRight[i] = 0f;
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Process(uint frames, IntPtr arg)
    {
        try
        {
            return GCHandle.FromIntPtr(arg).Target is JackAudioDevice device ? device.Cycle(frames) : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>The server is going or gone; JACK allows no calls into it from here.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Shutdown(IntPtr arg)
    {
        try
        {
            if (GCHandle.FromIntPtr(arg).Target is JackAudioDevice device) device.running = false;
        }
        catch
        {
            // The device is already gone.
        }
    }

    private static void Check(int status, string what)
    {
        if (status == 0) return;

        throw new InvalidOperationException($"could not {what} (status {status}).");
    }
}
