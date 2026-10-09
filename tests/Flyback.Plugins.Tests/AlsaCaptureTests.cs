using Flyback.Plugins.Audio;
using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The Linux sound input against a real card, which a machine with no microphone
/// cannot answer, and the shape of what it offers, which any machine can.
/// </summary>
public class AlsaCaptureTests
{
    private static IAudioInput Alsa() =>
        ShippedPlugins.Loaded.AudioInputs.Single(i => i.Id == "alsa-capture");

    [Fact]
    public void The_input_backend_is_shipped_and_supported_only_where_libasound_is()
    {
        var alsa = Alsa();

        alsa.Name.ShouldBe("ALSA");

        if (!OperatingSystem.IsLinux()) alsa.IsSupported.ShouldBeFalse();
    }

    [Fact]
    public void Its_form_asks_which_input_and_always_offers_the_system_default()
    {
        Assert.SkipUnless(Alsa().IsSupported, "libasound is not installed here");

        var form = Alsa().Form(SettingValues.None);

        var pick = form.ShouldHaveSingleItem().ShouldBeOfType<SettingField.Pick>();
        pick.Options.Select(o => o.Id).ShouldContain("default");
    }

    [Fact]
    public void A_saved_input_that_is_gone_stays_chosen_and_says_so()
    {
        Assert.SkipUnless(Alsa().IsSupported, "libasound is not installed here");

        var form = Alsa().Form(new SettingValues(new Dictionary<string, string> { ["device"] = "plughw:CARD=Gone,DEV=0" }));

        var pick = form.ShouldHaveSingleItem().ShouldBeOfType<SettingField.Pick>();
        pick.Options.Select(o => o.Id).ShouldContain("plughw:CARD=Gone,DEV=0");
        pick.Note.ShouldNotBeNull();
    }

    /// <summary>
    /// The only check that the device is really read: frames arrive, stereo, at the
    /// rate asked for, and the device is let go again.
    /// </summary>
    [Fact]
    public void The_default_input_delivers_frames_until_stopped()
    {
        Assert.SkipUnless(Alsa().IsSupported, "libasound is not installed here");

        using var capture = Alsa().Create(AudioFormat.Default, SettingValues.None);
        using var heard = new ManualResetEventSlim();
        var frames = 0;

        try
        {
            capture.Start(buffer =>
            {
                Interlocked.Add(ref frames, buffer.Length / 2);
                heard.Set();
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("could not open", StringComparison.Ordinal))
        {
            // Only no card to open: one that opens and then will not configure is a failure.
            Assert.Skip($"no input could be opened here: {ex.Message}");
        }

        heard.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ShouldBeTrue();

        capture.IsRunning.ShouldBeTrue();
        capture.Channels.ShouldBe(2);
        capture.SampleRate.ShouldBe(AudioFormat.Default.SampleRate);
        Volatile.Read(ref frames).ShouldBeGreaterThan(0);

        capture.Stop();

        capture.IsRunning.ShouldBeFalse();
    }
}
