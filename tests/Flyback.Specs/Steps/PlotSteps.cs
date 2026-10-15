using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Language;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>
/// The Picture plugin's Plot, in a box two units square with pitch 0 to 4 up it,
/// read at the point a beat and a pitch land on.
/// </summary>
[Binding]
public sealed class PlotSteps
{
    private const float Low = 0f;
    private const float High = 4f;

    private static ModuleCatalog Modules => ShippedPlugins.Loaded.Modules;

    private CompiledPatch? program;
    private float span;

    [Given("a Plot of {string} turning a page every {int} beats at {int} bpm")]
    public void GivenAPage(string pitch, int beats, int bpm) => Build(pitch, beats, bpm, future: 1f);

    [Given("a Plot of {string} turning a page every {int} beats at {int} bpm, with what is to come hidden")]
    public void GivenAPageWithoutTheFuture(string pitch, int beats, int bpm) => Build(pitch, beats, bpm, future: 0f);

    [Then("{float} second(s) in, beat {float} is drawn at pitch {float}")]
    public void ThenDrawn(float seconds, float beat, float pitch) =>
        Ink(seconds, beat, pitch).ShouldBeGreaterThan(0.9f);

    [Then("{float} second(s) in, nothing is drawn at beat {float} and pitch {float}")]
    public void ThenNothing(float seconds, float beat, float pitch) =>
        Ink(seconds, beat, pitch).ShouldBe(0f, 0.01f);

    private void Build(string pitch, int beats, int bpm, float future)
    {
        span = beats;

        var built = PatchLanguage.Build(
            $"plot(in: {pitch}, bpm: {bpm}, span: {beats}, page: 1, future: {future}, low: {Low}, high: {High}, "
            + "left: -1, right: 1, bottom: -1, top: 1, glow: 0, dot: 0) |> out.color",
            Modules);
        built.Issues.ShouldBeEmpty();

        program = built.Patch.CompileForVideo(Modules).Program;
    }

    /// <summary>
    /// The screen's red at the beat's column and the pitch's row. The beat is read
    /// on the page it falls on, which is the page showing at that second.
    /// </summary>
    private float Ink(float seconds, float beat, float pitch)
    {
        var compiled = program.ShouldNotBeNull();

        var x = (beat % span) / span * 2f - 1f;
        var y = (pitch - Low) / (High - Low) * 2f - 1f;

        var bank = compiled.AllocateRegisters();
        compiled.Evaluate(x, y, seconds, bank, default);

        return (float)bank[compiled.OutputBase];
    }
}
