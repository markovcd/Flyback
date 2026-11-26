using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Language;
using Flyback.Engine.Packs;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>A module pack added to the catalog, and a patch placing its modules.</summary>
[Binding]
public sealed class PackSteps
{
    private ModuleCatalog modules = NodeCatalog.BuiltIn;
    private Patch? patch;

    [Given("the pack:")]
    public void GivenThePack(string source)
    {
        var (catalog, issues) = PackModules.Into(NodeCatalog.BuiltIn, source);

        issues.ShouldBeEmpty();
        modules = catalog;
    }

    [When("the text is read with the pack:")]
    public void WhenRead(string source)
    {
        var load = PatchLanguage.Build(source, modules);

        load.Issues.ShouldBeEmpty(load.Report);
        patch = load.Patch;
    }

    [Then("the patch holds the one module {string} besides the Output")]
    public void ThenOneModule(string typeId) =>
        Patch.Nodes.Select(n => n.TypeId).Where(id => id != "output").ShouldBe([typeId]);

    [Then("with the pack the speakers play {float}")]
    public void ThenThePackPlays(float expected)
    {
        var program = Patch.CompileForAudio(modules).Program;
        var state = new DelayState(program, 48_000);
        var registers = program.AllocateRegisters();

        program.Evaluate(0d, 0d, 0d, registers, default, state);

        registers[program.OutputBase].ShouldBe(expected, 1e-6);
    }

    private Patch Patch => patch.ShouldNotBeNull("no text has been read");
}
