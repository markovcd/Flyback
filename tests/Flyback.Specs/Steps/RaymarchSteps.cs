using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Language;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>
/// The Picture plugin's Raymarch with no twist, read through its 'hit' output on
/// a coarse grid of the screen.
/// </summary>
[Binding]
public sealed class RaymarchSteps
{
    private const float Spacing = 3f;

    private static ModuleCatalog Modules => ShippedPlugins.Loaded.Modules;

    private string scene = "lattice";

    [Given("a Raymarch flying through the {word}")]
    public void GivenAScene(string name) => scene = name;

    [Then("the middle of the screen is sky")]
    public void ThenSky() => Hit(Program(), 0f, 0f).ShouldBe(0d);

    [Then("some of the screen is a surface")]
    public void ThenSurface() => Covered(Program()).ShouldBeGreaterThan(0.1d);

    [Then("the shapes cover more of the screen with the pulse turned up")]
    public void ThenSwollen() => Covered(Program(pulse: 1f)).ShouldBeGreaterThan(Covered(Program()));

    [Then("flying on by its spacing shows the same picture")]
    public void ThenRepeats()
    {
        var here = Program(travel: 0f);
        var there = Program(travel: Spacing);
        var halfway = Program(travel: Spacing / 2f);

        Grid().All(p => Hit(here, p.X, p.Y) == Hit(there, p.X, p.Y)).ShouldBeTrue();
        Grid().Any(p => Hit(here, p.X, p.Y) != Hit(halfway, p.X, p.Y)).ShouldBeTrue();
    }

    private CompiledPatch Program(float pulse = 0f, float travel = 0f)
    {
        var built = PatchLanguage.Build(
            FormattableString.Invariant(
                $"let view = raymarch(travel: {travel}, twist: 0, pulse: {pulse}, spacing: {Spacing}, scene: \"{scene}\", steps: \"64\")\n")
                + "view.hit |> out.color",
            Modules);
        built.Issues.ShouldBeEmpty();

        return built.Patch.CompileForVideo(Modules).Program;
    }

    private static double Covered(CompiledPatch program) => Grid().Average(p => Hit(program, p.X, p.Y));

    private static double Hit(CompiledPatch program, float x, float y)
    {
        var bank = program.AllocateRegisters();
        program.Evaluate(x, y, 0d, bank, default);

        return bank[program.OutputBase];
    }

    /// <summary>Points across a 16:9 screen, off the middle.</summary>
    private static IEnumerable<(float X, float Y)> Grid()
    {
        for (var i = 0; i < 12; i++)
            for (var j = 0; j < 7; j++)
                yield return (-1.7f + i * 0.31f, -0.93f + j * 0.31f);
    }
}
