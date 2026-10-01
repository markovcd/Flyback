using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The knob panel's fixed grid, set from the settings' MIDI tab and read off where the knobs stand.</summary>
[Binding]
public sealed class KnobGridSteps(Editor editor)
{
    [When("the knobs are kept in a grid of {int} columns and {int} rows")]
    public void WhenGridded(int columns, int rows)
    {
        editor.OpenSettings("MIDI");
        editor.Tick("Keep the knobs in a fixed grid", on: true);
        editor.Type("knobColumns", columns);
        editor.Type("knobRows", rows);
        editor.Answer("Save");
    }

    [Then("the knob panel's rows are {string}")]
    public void ThenOneRow(string row) => editor.KnobRows.ShouldBe([row]);

    [Then("the knob panel's rows are {string} and {string}")]
    public void ThenTwoRows(string first, string second) => editor.KnobRows.ShouldBe([first, second]);
}
