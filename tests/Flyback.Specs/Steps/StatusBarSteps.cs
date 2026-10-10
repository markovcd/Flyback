using Flyback.Core;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Settings;
using Flyback.Specs.Support;
using Flyback.Ui.Audio;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using Shouldly;
using Flyback.Plugins.Testing;

namespace Flyback.Specs.Steps;

/// <summary>The editor's status bar and toolbar Volume, with speakers the scenario plays by pulling buffers from them.</summary>
[Binding]
public sealed class StatusBarSteps(EditorDriver editor) : IDisposable
{
    private const int Frames = 512;

    private readonly LoopbackDevice speakers = new();

    [Given("the speakers play whatever they are handed")]
    public void GivenTheSpeakers() => editor.Services += services => services.AddSingleton(new AudioSetup(speakers, new LoopbackOutput(speakers)));

    /// <summary>The sound waits for its compiled program, which is the one timed, so this plays until it has been.</summary>
    [When("the speakers have played long enough to time the sound")]
    public void WhenTimed()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (editor.SoundSpeed <= 0 && DateTime.UtcNow < deadline)
            for (var i = 0; i < 8; i++) speakers.Pump(Frames);

        editor.SoundSpeed.ShouldBeGreaterThan(0, "the sound was never timed");
    }

    [When("the speakers have played for {float} second(s)")]
    public void WhenPlayed(float seconds)
    {
        editor.Open();

        // A patch with no sound starts no device, and the scenario is about what is then said.
        for (var at = 0; at < seconds * GlobalConstants.SampleRate; at += Frames)
            if (speakers.IsRunning) speakers.Pump(Frames);
    }

    [Then("the status bar says how many times real time the sound renders at")]
    public void ThenItSaysTheSpeed() => editor.StatusCount.ShouldMatch(@"sound renders at \d+\.\d\d×");

    [Then("the status bar says nothing of how fast the sound renders")]
    public void ThenItSaysNothingOfTheSpeed() => editor.StatusCount.ShouldNotContain("sound renders at");

    [Then("the status bar says the sound plays through {string}")]
    public void ThenItNamesTheBackend(string backend) => editor.StatusCount.ShouldEndWith($"   |   {backend}");

    [Then("the status bar says nothing of oversampling")]
    public void ThenItSaysNothingOfOversampling() => editor.StatusCount.ShouldNotContain("oversampl");

    [Then("the status bar counts no modules or wires")]
    public void ThenNoCounts()
    {
        var said = editor.StatusCount;

        said.ShouldNotContain("modules");
        said.ShouldNotContain("wires");
    }

    [Then("the toolbar shows the Output's Volume")]
    public void ThenTheVolumeShows() => editor.ShowsVolume.ShouldBeTrue();

    [Then("the toolbar shows no Volume")]
    public void ThenNoVolume() => editor.ShowsVolume.ShouldBeFalse();

    public void Dispose() => speakers.Dispose();

}
