using Flyback.Plugins.Audio;
using Flyback.Plugins.WinIO;
using Flyback.Plugins.WinIO.Tests;
using Reqnroll;
using Shouldly;
using Xunit;

namespace Flyback.Specs.Steps;

/// <summary>The ASIO output against a driver held in memory, so the scenarios run on any machine.</summary>
[Binding]
public sealed class AsioSteps : IDisposable
{
    private readonly FakeAsioDriver driver = new();
    private AsioAudioDevice? device;
    private Exception? refusal;

    [Given("an ASIO driver is installed")]
    public static void GivenADriver()
    {
    }

    [Given("an ASIO driver running at {int} Hz")]
    public void GivenADriverAt(int rate) => driver.Rate = rate;

    [Given("an ASIO driver held at {int} Hz by an outside clock")]
    public void GivenADriverHeldAt(int rate)
    {
        driver.Rate = rate;
        driver.AcceptsAnyRate = false;
    }

    [When("the sound plays through it")]
    public void WhenItPlays()
    {
        device = new AsioAudioDevice(new AudioFormat(48_000, 2, 30), driver.Load);
        device.Start(buffer =>
        {
            for (var i = 0; i < buffer.Length; i += 2)
            {
                buffer[i] = 0.5f;
                buffer[i + 1] = -0.5f;
            }
        });
    }

    [When("the sound is started through it")]
    public void WhenItIsStarted() => refusal = Record.Exception(WhenItPlays);

    [Then("each block the driver plays carries the left and the right on its first two outputs")]
    public void ThenEachBlockCarriesBoth()
    {
        for (var half = 0; half < 2; half++)
        {
            driver.Switch(half);

            driver.Ints(half, 0).ShouldAllBe(sample => sample > 0);
            driver.Ints(half, 1).ShouldAllBe(sample => sample < 0);
        }
    }

    [Then("the driver runs at {int} Hz")]
    public void ThenTheDriverRunsAt(int rate) => driver.Rate.ShouldBe(rate);

    [Then("it refuses, saying it cannot play at {int} Hz")]
    public void ThenItRefuses(int rate)
    {
        refusal.ShouldNotBeNull().Message.ShouldContain($"{rate} Hz");
        device!.IsRunning.ShouldBeFalse();
    }

    public void Dispose()
    {
        device?.Dispose();
        driver.Dispose();
    }
}
