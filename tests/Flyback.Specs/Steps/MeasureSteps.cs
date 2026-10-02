using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>Measure in the editor: Ctrl+M, and what is pinned beside an output.</summary>
[Binding]
public sealed class MeasureSteps(PatchContext context, EditorDriver editor)
{
    [Given("an LFO turning {int} times a second, wired to nothing")]
    public void GivenAnLfo(int hz)
    {
        context.Add("lfo", "osc.sine");
        context.SetInput("lfo", "freq", hz);
    }

    [When("the patch is measured")]
    public void WhenMeasured() => editor.Measure();

    [When("the LFO is turned to {int} times a second")]
    public void WhenTurned(int hz) =>
        editor.Do(canvas =>
        {
            canvas.History.Patch.Find(context.Node("lfo").Id)!.InputValues[1] = hz;
            canvas.History.Record();
        });

    [Then("the LFO is pinned as swinging from {int} to {int}, {int} times a second")]
    public void ThenPinned(int low, int high, int hz)
    {
        var sound = editor.Measured(context.Node("lfo").Id, 0).ShouldNotBeNull().Sound.Single();

        sound.Min.ShouldBe(low, 1e-3);
        sound.Max.ShouldBe(high, 1e-3);
        sound.Hz!.Value.ShouldBe(hz, 0.01);
    }

    [Then("the measurement is marked out of date")]
    public void ThenStale() => editor.MeasurementStale.ShouldBeTrue();
}
