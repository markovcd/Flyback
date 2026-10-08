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

    private readonly StandIn machine = new();

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
    public void WhenStart() => machine.Press(MidiAction.Start);

    [When("the drum machine presses Stop")]
    public void WhenStop() => machine.Press(MidiAction.Stop);

    [When("the drum machine presses Continue")]
    public void WhenContinue() => machine.Press(MidiAction.Continue);

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
        editor.WaitForClock(_ => DateTime.UtcNow > machine.PressedAt.AddMilliseconds(300), TimeSpan.FromSeconds(2));

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

    /// <summary>A MIDI backend with one drum machine plugged in, and nothing behind it.</summary>
    private sealed class StandIn : IMidiInput
    {
        private Pressed? Port { get; set; }

        public DateTime PressedAt { get; private set; }

        public string Id => "stand-in";

        public string Name => "Stand-in";

        public int Priority => 1;

        public bool IsSupported => true;

        public IReadOnlyList<MidiPortInfo> Ports => MidiPorts.Named(["Drum Machine"]);

        public IMidiPort Open(string id, MidiCallback deliver) => Port = new Pressed(id, deliver);

        /// <summary>Sends a transport button, as the driver would, on this thread.</summary>
        public void Press(MidiAction action)
        {
            PressedAt = DateTime.UtcNow;
            (Port ?? throw new InvalidOperationException("Nothing opened the drum machine: the patch does not listen to it.")).Send(new MidiMessage(action, 0, 0f));
        }
    }

    /// <summary>The drum machine's port, sending on the caller's thread what a driver would send on its own.</summary>
    private sealed class Pressed(string id, MidiCallback deliver) : IMidiPort
    {
        public string Id => id;

        public bool IsOpen { get; private set; } = true;

        public void Send(MidiMessage message) => deliver(message);

        public void Dispose() => IsOpen = false;
    }
}
