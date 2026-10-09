using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Flyback.Plugins.Audio;

namespace Flyback.Plugins.Testing;

/// <summary>
/// A JACK server for the tests that need one: the one already running, or else a
/// <c>jackd</c> with the dummy driver started here and stopped with this object. A machine
/// with neither has <see cref="Available"/> false. Shared by the plugin tests and the specs.
/// </summary>
/// <remarks>
/// A server takes about 38 MB of <c>/dev/shm</c> whatever its port table, and a container's
/// is 64 MB, so a second one dies of SIGBUS on its first client. The test assemblies run side
/// by side, so each holds <see cref="LockPath"/> while its server runs.
/// <para>
/// Every test server has the one name <see cref="ServerName"/>: jackd frees a dead server's
/// slot only for a new server of the same name, and has eight slots.
/// </para>
/// </remarks>
public sealed partial class JackDaemon : IDisposable
{
    private static readonly TimeSpan StartupLimit = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LockLimit = TimeSpan.FromMinutes(1);
    private static readonly string LockPath = Path.Combine(Path.GetTempPath(), "flyback-jackd.lock");

    private const string ServerVariable = "JACK_DEFAULT_SERVER";
    private const string ServerName = "flyback-test";
    private const int SigTerm = 15;

    private readonly IAudioOutput jack;
    private readonly FileStream? held;
    private readonly Process? started;
    private readonly string? before;
    private readonly List<string> said = [];

    public JackDaemon(IAudioOutput jack)
    {
        this.jack = jack;

        if (!OperatingSystem.IsLinux() || jack.IsSupported) return;

        held = TakeLock();

        before = Environment.GetEnvironmentVariable(ServerVariable);
        Environment.SetEnvironmentVariable(ServerVariable, ServerName);

        try
        {
            started = Process.Start(new ProcessStartInfo("jackd", $"-n {ServerName} --no-realtime --port-max 32 -d dummy -r 48000 -p 256")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
        }
        catch (Win32Exception)
        {
            Environment.SetEnvironmentVariable(ServerVariable, before);
            held.Dispose();
            return;
        }

        // Both pipes are drained, so a talkative server never blocks on a full one.
        started.OutputDataReceived += (_, _) => { };
        started.ErrorDataReceived += (_, line) =>
        {
            if (!string.IsNullOrWhiteSpace(line.Data) && !line.Data.Contains("buffer overruns", StringComparison.Ordinal))
                lock (said) said.Add(line.Data.Trim());
        };
        started.BeginOutputReadLine();
        started.BeginErrorReadLine();

        var waited = Stopwatch.StartNew();

        while (!jack.IsSupported && waited.Elapsed < StartupLimit && started is { HasExited: false })
            Thread.Sleep(50);
    }

    public bool Available => jack.IsSupported;

    /// <summary>Why there is no server, in jackd's words where it started and gave up.</summary>
    public string Why
    {
        get
        {
            if (Available) return string.Empty;
            if (started is null) return "no JACK server here";

            lock (said)
                return said.Count == 0 ? "jackd started and never answered" : $"jackd: {string.Join("; ", said)}";
        }
    }

    /// <summary>
    /// SIGTERM lets jackd free its shared memory; SIGKILL leaves it in <c>/dev/shm</c> until the
    /// next server reclaims it.
    /// </summary>
    public void Dispose()
    {
        if (started is null) return;

        try
        {
            if (!started.HasExited && (Kill(started.Id, SigTerm) != 0 || !started.WaitForExit(5000))) started.Kill();

            started.WaitForExit(5000);
        }
        finally
        {
            started.Dispose();
            Environment.SetEnvironmentVariable(ServerVariable, before);
            held?.Dispose();
        }
    }

    private static FileStream TakeLock()
    {
        var waited = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                return new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (waited.Elapsed < LockLimit)
            {
                Thread.Sleep(100);
            }
        }
    }

    [LibraryImport("libc", EntryPoint = "kill")]
    private static partial int Kill(int pid, int signal);
}
