using System.ComponentModel;
using System.Diagnostics;
using Flyback.Plugins.Audio;

namespace Flyback.Plugins.Testing;

/// <summary>
/// A JACK server for the tests that need one: the one already running, or else a
/// <c>jackd</c> with the dummy driver started here and stopped with this object. A machine
/// with neither has <see cref="Available"/> false. Shared by the plugin tests and the specs.
/// </summary>
public sealed class JackDaemon : IDisposable
{
    private static readonly TimeSpan StartupLimit = TimeSpan.FromSeconds(10);

    private readonly IAudioOutput jack;
    private readonly Process? started;

    public JackDaemon(IAudioOutput jack)
    {
        this.jack = jack;

        if (!OperatingSystem.IsLinux() || jack.IsSupported) return;

        // A small port table keeps the server's shared memory inside a container's /dev/shm.
        try
        {
            started = Process.Start(new ProcessStartInfo("jackd", "--no-realtime --port-max 32 -d dummy -r 48000 -p 256")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
        }
        catch (Win32Exception)
        {
            return;
        }

        var waited = Stopwatch.StartNew();

        while (!jack.IsSupported && waited.Elapsed < StartupLimit && started is { HasExited: false })
            Thread.Sleep(50);
    }

    public bool Available => jack.IsSupported;

    public void Dispose()
    {
        if (started is null) return;

        try
        {
            if (!started.HasExited) started.Kill();

            started.WaitForExit(5000);
        }
        finally
        {
            started.Dispose();
        }
    }
}
