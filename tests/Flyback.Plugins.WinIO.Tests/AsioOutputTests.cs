using Flyback.Plugins.Audio;
using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.WinIO.Tests;

/// <summary>
/// The ASIO output against a fake driver laid out as a real one is, which every machine can
/// answer; only finding the installed drivers needs Windows.
/// </summary>
public sealed class AsioOutputTests : IDisposable
{
    private static readonly AudioFormat Format = new(48_000, 2, 30);

    private readonly FakeAsioDriver driver = new();
    private readonly AsioAudioDevice device;

    public AsioOutputTests() => device = new AsioAudioDevice(Format, driver.Load);

    public void Dispose()
    {
        device.Dispose();
        driver.Dispose();
    }

    [Fact]
    public void It_ranks_below_wasapi_and_answers_no_off_windows()
    {
        var asio = new AsioAudioOutput();

        asio.Id.ShouldBe("asio");
        asio.Priority.ShouldBeLessThan(new WasapiAudioOutput().Priority);

        if (OperatingSystem.IsWindows()) return;

        asio.IsSupported.ShouldBeFalse();
        asio.Form(SettingValues.None).ShouldBeEmpty();
    }

    [Fact]
    public void Nothing_is_loaded_until_the_sound_starts()
    {
        driver.Loads.ShouldBe(0);
        device.IsRunning.ShouldBeFalse();
        device.SampleRate.ShouldBe(48_000);
    }

    [Fact]
    public void Each_half_of_the_buffer_takes_one_block_of_the_left_and_right()
    {
        device.Start(Stereo(0.5f, -0.25f));

        driver.Started.ShouldBeTrue();
        device.IsRunning.ShouldBeTrue();
        driver.Halves.GetLength(1).ShouldBe(2);

        driver.Switch(1);

        driver.Ints(1, 0).ShouldAllBe(sample => sample == (int)Math.Round(0.5 * int.MaxValue));
        driver.Ints(1, 1).ShouldAllBe(sample => sample == (int)Math.Round(-0.25 * int.MaxValue));
        driver.Ints(0, 0).ShouldAllBe(sample => sample == 0);
    }

    [Fact]
    public void The_latency_is_the_drivers_output_latency()
    {
        device.Start(Stereo(0f, 0f));

        device.Latency.ShouldBe(TimeSpan.FromSeconds(300 / 48_000.0));
    }

    [Fact]
    public void A_driver_that_listens_is_told_each_block_is_ready()
    {
        device.Start(Stereo(0f, 0f));
        var before = driver.ReadyCalls;

        driver.Switch(0);
        driver.Switch(1);

        driver.ReadyCalls.ShouldBe(before + 2);
    }

    [Fact]
    public void The_driver_is_set_to_the_engines_rate()
    {
        driver.Rate = 44_100;

        device.Start(Stereo(0f, 0f));

        driver.Rate.ShouldBe(48_000);
    }

    [Fact]
    public void A_driver_that_can_take_the_engines_rate_is_opened_at_it_and_let_go()
    {
        driver.Rate = 44_100;

        using var opened = AsioAudioDevice.Open(Format, driver.Load);

        opened.SampleRate.ShouldBe(48_000);
        driver.Released.ShouldBeTrue();
        driver.BuffersMade.ShouldBe(0);
        opened.IsRunning.ShouldBeFalse();
    }

    /// <summary>A driver clocked from outside cannot change rate.</summary>
    [Fact]
    public void A_driver_held_at_another_rate_is_opened_and_played_at_its_own()
    {
        driver.Rate = 44_100;
        driver.AcceptsAnyRate = false;

        using var opened = AsioAudioDevice.Open(Format, driver.Load);
        opened.Start(Stereo(0f, 0f));

        opened.SampleRate.ShouldBe(44_100);
        opened.IsRunning.ShouldBeTrue();
        opened.Latency.ShouldBe(TimeSpan.FromSeconds(300 / 44_100.0));
    }

    /// <summary>Playing at its rate would play at another pitch than the engine renders at.</summary>
    [Fact]
    public void A_driver_moved_to_a_rate_it_cannot_leave_refuses_to_start_saying_both_and_is_let_go()
    {
        driver.Rate = 44_100;
        driver.AcceptsAnyRate = false;

        var message = Should.Throw<InvalidOperationException>(() => device.Start(Stereo(0f, 0f))).Message;

        message.ShouldContain("44100 Hz");
        message.ShouldContain("48000 Hz");
        device.IsRunning.ShouldBeFalse();
        driver.Released.ShouldBeTrue();
    }

    [Fact]
    public void A_driver_already_playing_answers_for_the_rate_without_being_loaded_again()
    {
        driver.Rate = 44_100;
        driver.AcceptsAnyRate = false;

        using var playing = AsioAudioDevice.Open(Format, driver.Load);
        playing.Start(Stereo(0f, 0f));

        using var next = AsioAudioDevice.Open(Format, driver.Load);

        next.SampleRate.ShouldBe(44_100);
        driver.Loads.ShouldBe(2);
    }

    [Fact]
    public void A_driver_with_one_output_plays_the_left()
    {
        driver.Outputs = 1;

        device.Start(Stereo(0.5f, -0.5f));
        driver.Switch(0);

        driver.Halves.GetLength(1).ShouldBe(1);
        driver.Ints(0, 0).ShouldAllBe(sample => sample > 0);
    }

    [Fact]
    public void A_sample_type_flyback_cannot_write_is_refused_by_name()
    {
        driver.Type = 2;

        Should.Throw<InvalidOperationException>(() => device.Start(Stereo(0f, 0f))).Message.ShouldContain("type 2");
        driver.Released.ShouldBeTrue();
    }

    [Fact]
    public void Stopping_stops_the_driver_and_lets_it_go_and_starting_again_loads_it_afresh()
    {
        device.Start(Stereo(0f, 0f));
        device.Stop();

        driver.Started.ShouldBeFalse();
        driver.BuffersDisposed.ShouldBe(1);
        driver.Released.ShouldBeTrue();
        device.IsRunning.ShouldBeFalse();

        device.Start(Stereo(0f, 0f));

        driver.Loads.ShouldBe(2);
        device.IsRunning.ShouldBeTrue();
    }

    /// <summary>A driver asks for this after its block size or rate was changed in its own control panel.</summary>
    [Fact]
    public void A_reset_request_plays_the_same_sound_through_the_driver_loaded_again()
    {
        device.Start(Stereo(0.5f, 0.5f));

        driver.Message(Asio.ResetRequest, 0).ShouldBe(1);

        SpinWait.SpinUntil(() => driver.Loads == 2 && driver.Started, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        driver.BuffersMade.ShouldBe(2);

        driver.Switch(0);
        driver.Ints(0, 0).ShouldAllBe(sample => sample > 0);
    }

    [Fact]
    public void It_says_which_messages_it_answers()
    {
        device.Start(Stereo(0f, 0f));

        driver.Message(Asio.SelectorSupported, Asio.ResetRequest).ShouldBe(1);
        driver.Message(Asio.SelectorSupported, 7).ShouldBe(0);
        driver.Message(Asio.EngineVersion, 0).ShouldBe(2);
    }

    [Fact]
    public void A_second_driver_cannot_play_while_one_does()
    {
        using var other = new FakeAsioDriver();
        using var second = new AsioAudioDevice(Format, other.Load);

        device.Start(Stereo(0f, 0f));

        Should.Throw<InvalidOperationException>(() => second.Start(Stereo(0f, 0f))).Message.ShouldContain("already playing");
        other.Loads.ShouldBe(0);
        device.IsRunning.ShouldBeTrue();
    }

    [Theory]
    [InlineData(Asio.Int16, 0.5f, 16_384)]
    [InlineData(Asio.Int16, 2f, 32_767)]
    [InlineData(Asio.Int32In16, -1f, -32_767)]
    [InlineData(Asio.Int32In24, 0.5f, 4_194_304)]
    [InlineData(Asio.Int32, -2f, -int.MaxValue)]
    public unsafe void Every_integer_type_is_scaled_to_its_own_width_and_clipped(int type, float sample, int expected)
    {
        var buffer = stackalloc byte[8];
        new Span<byte>(buffer, 8).Clear();

        AsioAudioDevice.Write((IntPtr)buffer, type, [sample, 0f], 0);

        var written = type == Asio.Int16 ? *(short*)buffer : *(int*)buffer;
        written.ShouldBe(expected);
    }

    [Fact]
    public unsafe void Packed_24_bit_samples_take_three_bytes_each()
    {
        var buffer = stackalloc byte[6];

        AsioAudioDevice.Write((IntPtr)buffer, Asio.Int24, [-1f, 0f, 0.5f, 0f], 0);

        new ReadOnlySpan<byte>(buffer, 6).ToArray().ShouldBe(new byte[] { 0x01, 0x00, 0x80, 0x00, 0x00, 0x40 });
    }

    [Fact]
    public unsafe void Floating_types_are_written_as_they_are()
    {
        var single = stackalloc float[1];
        var wide = stackalloc double[1];

        AsioAudioDevice.Write((IntPtr)single, Asio.Float32, [0.125f, 0f], 0);
        AsioAudioDevice.Write((IntPtr)wide, Asio.Float64, [0f, -0.75f], 1);

        single[0].ShouldBe(0.125f);
        wide[0].ShouldBe(-0.75);
    }

    private static AudioCallback Stereo(float left, float right) => buffer =>
    {
        for (var i = 0; i < buffer.Length; i += 2)
        {
            buffer[i] = left;
            buffer[i + 1] = right;
        }
    };
}
