using System.Diagnostics;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Audio;

/// <summary>
/// The JACK sound output against a real server, which a machine without JACK cannot
/// answer, and the shape of what it offers, which any machine can.
/// </summary>
public class JackOutputTests(JackServerFixture server) : IClassFixture<JackServerFixture>
{
    /// <summary>How long a test against the server may take; each takes about a second.</summary>
    private const int Cap = 60_000;

    private static readonly SettingValues Unconnected = SettingValues.None.With("connect", "none");

    [Fact]
    public void The_backend_is_shipped_and_outranks_alsa_where_a_server_runs()
    {
        var jack = server.Output;

        jack.Name.ShouldBe("JACK");
        jack.Priority.ShouldBeGreaterThan(ShippedPlugins.Loaded.AudioOutputs.Single(o => o.Id == "alsa").Priority);

        if (!OperatingSystem.IsLinux()) jack.IsSupported.ShouldBeFalse();
    }

    [Fact]
    public void Its_form_asks_where_the_ports_connect_and_starts_on_the_system_playback()
    {
        Assert.SkipUnless(server.Available, server.Why);

        var pick = server.Output.Form(SettingValues.None).ShouldHaveSingleItem().ShouldBeOfType<SettingField.Pick>();

        pick.Key.ShouldBe("connect");
        pick.Sane(null).ShouldBe("system");
        pick.Options.Select(o => o.Id).ShouldBe(["system", "none"]);
    }

    /// <summary>
    /// The only check that the server really calls us: blocks arrive, at the server's own
    /// rate and period, and stopping lets the client go.
    /// </summary>
    [Fact(Timeout = Cap)]
    public Task The_server_drives_the_callback_until_stopped() => Bounded(() =>
    {
        Assert.SkipUnless(server.Available, server.Why);

        using var device = server.Output.Create(AudioFormat.Default, Unconnected);
        using var heard = new ManualResetEventSlim();
        var samples = 0;

        device.SampleRate.ShouldBeGreaterThan(0);
        device.Latency.ShouldBeGreaterThan(TimeSpan.Zero);
        device.IsRunning.ShouldBeFalse();

        device.Start(buffer =>
        {
            buffer.Fill(0.25f);

            if (Interlocked.Add(ref samples, buffer.Length) >= device.SampleRate) heard.Set();
        });

        // A second of audio, drawn at the server's pace, takes a second.
        heard.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).ShouldBeTrue();
        device.IsRunning.ShouldBeTrue();

        device.Stop();

        device.IsRunning.ShouldBeFalse();
    });

    [Fact(Timeout = Cap)]
    public Task A_stopped_device_starts_again() => Bounded(() =>
    {
        Assert.SkipUnless(server.Available, server.Why);

        using var device = server.Output.Create(AudioFormat.Default, Unconnected);

        for (var round = 0; round < 2; round++)
        {
            using var heard = new ManualResetEventSlim();

            device.Start(buffer =>
            {
                buffer.Clear();
                heard.Set();
            });

            heard.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).ShouldBeTrue();

            device.Stop();
        }
    });

    /// <summary>
    /// Stopping while the callback is busy and collections are under way. A process thread that
    /// libjack cancels inside the runtime aborts the process or freezes it whole.
    /// </summary>
    [Fact(Timeout = Cap)]
    public Task Stopping_mid_callback_while_collecting_leaves_the_process_running() => Bounded(() =>
    {
        Assert.SkipUnless(server.Available, server.Why);

        using var done = new CancellationTokenSource();
        var churn = Task.Factory.StartNew(() =>
        {
            var kept = new List<byte[]>();

            while (!done.IsCancellationRequested)
            {
                kept.Add(new byte[32_000]);
                if (kept.Count > 1000) kept.Clear();
            }
        }, done.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        for (var round = 0; round < 20; round++)
        {
            using var device = server.Output.Create(AudioFormat.Default, Unconnected);
            using var inside = new ManualResetEventSlim();

            device.Start(buffer =>
            {
                buffer.Clear();
                inside.Set();

                var busy = Stopwatch.StartNew();
                while (busy.ElapsedMilliseconds < 20) { }
            });

            inside.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).ShouldBeTrue();

            device.Stop();
        }

        done.Cancel();
        churn.Wait(TestContext.Current.CancellationToken);
    });

    /// <summary>
    /// Runs a test that calls into libjack off the test's thread, so a call that never
    /// returns fails the test by name at <see cref="Cap"/> rather than hanging the run.
    /// </summary>
    private static Task Bounded(Action test) =>
        Task.Factory.StartNew(test, TestContext.Current.CancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
}
