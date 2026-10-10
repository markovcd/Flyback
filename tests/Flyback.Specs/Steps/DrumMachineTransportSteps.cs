using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using Shouldly;
using Flyback.Ui;
using Flyback.Plugins.Midi;
using Flyback.Specs.Support;
using Flyback.Host;

namespace Flyback.Specs.Steps;

/// <summary>
/// A drum machine's Start, Continue and Stop pressed into the editor, and what its
/// transport does about them.
/// </summary>
/// <remarks>
/// The drum machine is a stand-in device behind the editor's own hub, because the
/// question is what the transport does, not what a driver makes of a cable.
/// </remarks>
[Binding]
public sealed class DrumMachineTransportSteps(EditorDriver editor) : IDisposable
{
    /// <summary>How near the top a patch played from the top has to be when it is looked at.</summary>
    private const double Slack = 1;

    private readonly StandInMidiInput machine = new("Drum Machine");

    private DateTime pressedAt;

    private readonly DirectoryInfo settings = Directory.CreateTempSubdirectory("flyback-drum-machine-specs");

    [Given("the drum machine is plugged in")]
    public void GivenPluggedIn()
    {
        var before = editor.Services;

        editor.Services = services =>
        {
            before?.Invoke(services);
            services.AddSingleton<IMidiInput>(machine);
        };
    }

    [Given("the settings say not to play and pause with an instrument")]
    public void GivenNotFollowing()
    {
        var path = Path.Combine(settings.FullName, "settings.json");

        new OutputSettings { FollowTransport = false }.Save(path);
        editor.Setup = editor.Setup with { Folders = editor.Setup.Folders with { SettingsPath = path } };
    }

    [When("the drum machine presses Start")]
    public void WhenStart() => Press(MidiAction.Start);

    [When("the drum machine presses Stop")]
    public void WhenStop() => Press(MidiAction.Stop);

    [When("the drum machine presses Continue")]
    public void WhenContinue() => Press(MidiAction.Continue);

    [Then("the patch plays from the top")]
    public void ThenPlaysFromTheTop() =>
        editor.WaitForClock(seconds => seconds < Slack && !editor.Paused, TimeSpan.FromSeconds(2));

    [Then("the patch plays on from about {int} seconds")]
    public void ThenPlaysOn(int seconds) =>
        editor.WaitForClock(at => at > seconds - Slack && at < seconds + Slack && !editor.Paused, TimeSpan.FromSeconds(2));

    [Then("the patch pauses")]
    public void ThenPauses() => editor.WaitForStop(TimeSpan.FromSeconds(2));

    [Then("the patch stays paused at about {int} seconds")]
    public void ThenStaysPaused(int seconds)
    {
        // Nothing arriving is proved only by waiting; a followed Start is posted to
        // the window at once, so a few frames' worth is plenty.
        editor.WaitForClock(_ => DateTime.UtcNow > pressedAt.AddMilliseconds(300), TimeSpan.FromSeconds(2));

        editor.Paused.ShouldBeTrue();
        editor.Clock.ShouldBeInRange(seconds - Slack, seconds + Slack);
    }

    public void Dispose()
    {
        try
        {
            settings.Delete(recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Sends a transport button, as the driver would, on this thread.</summary>
    private void Press(MidiAction action)
    {
        pressedAt = DateTime.UtcNow;
        (machine.Port ?? throw new InvalidOperationException("Nothing opened the drum machine: the patch does not listen to it.")).Send(new MidiMessage(action, 0, 0f));
    }
}
