using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Engine.Render;
using Flyback.Specs.Support;
using Flyback.Tests;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>A Hits listening to the Line In, and how often each of its gates opens.</summary>
[Binding]
public sealed class HitsSteps(PatchContext context)
{
    private readonly Dictionary<string, int> opened = [];

    [Given("a Hits listens to a Line In")]
    public void GivenAHits()
    {
        context.Add("line", SourceModules.LineInTypeId);
        context.Add("hits", HitsModule.TypeId);
        context.Wire("line", "left", "hits", "in");
    }

    [When("it hears four seconds of a drum loop at 120 BPM")]
    public void WhenItHearsALoop() => Hear(DrumLoop.Drums(4));

    [When("it hears four seconds of a drum loop at 120 BPM, at a thirtieth of the volume")]
    public void WhenItHearsAQuietLoop() => Hear(DrumLoop.Drums(4, 1f / 30));

    [When("it hears a {int} Hz tone held for three seconds")]
    public void WhenItHearsATone(int hertz) => Hear(DrumLoop.Tone(hertz, 0.5f, 3));

    [Then("its {word} gate opens {int} times")]
    public void ThenItOpens(string drum, int times) => opened[drum].ShouldBe(times);

    [Then("its {word} gate opens at most once")]
    public void ThenItOpensAtMostOnce(string drum) => opened[drum].ShouldBeLessThanOrEqualTo(1);

    /// <summary>Plays the sound into the Line In three times, a gate to each speaker's left, and counts each gate's rises.</summary>
    private void Hear(float[] sound)
    {
        context.Add("screen", OutputModule.TypeId);
        context.SetInput("screen", "volume", 1f);

        foreach (var drum in (string[])["kick", "snare", "hats"])
        {
            context.Wire("hits", drum, "screen", "left");
            var heard = context.RenderAudio(sound.Length, new RecordedLineIn(sound));
            opened[drum] = Rises([.. heard.Where((_, i) => i % 2 == 0)]);
        }
    }

    private static int Rises(float[] gate)
    {
        var rises = 0;

        for (var i = 1; i < gate.Length; i++)
            if (gate[i - 1] <= 0.5f && gate[i] > 0.5f)
                rises++;

        return rises;
    }
}
