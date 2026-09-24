using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// <c>requires flyback.picture</c>: the plugins a patch cannot be built
/// without, so one that is missing is said once, by name.
/// </summary>
public class RequiresTests
{
    private static readonly ModuleProvider Shapes = new("test.shapes", "Shapes");

    private static readonly ModuleCatalog WithShapes = NodeCatalog.BuiltIn.With(Shapes,
    [
        new NodeDef("test.shapes.blob", "Blob", "Test",
            [new PortSpec("in")],
            [new PortSpec("out", PortKind.Color)],
            (em, i) => [em.Mul(i[0], 1f)]),
    ]).Catalog;

    [Fact]
    public void A_missing_plugin_is_said_once_rather_than_once_a_module()
    {
        var load = PatchLanguage.Build(
            """
            requires test.shapes
            blob() |> out.color
            x |> blob() |> blob() |> out.left
            """,
            NodeCatalog.BuiltIn);

        var issue = load.Issues.ShouldHaveSingleItem(load.Report);

        issue.Code.ShouldBe(IssueCode.MissingPlugin);
        issue.Message.ShouldContain("'test.shapes'");
    }

    [Fact]
    public void A_plugin_this_build_has_is_said_nothing_about()
    {
        var load = PatchLanguage.Build("requires test.shapes\nx |> blob() |> out.color", WithShapes);

        load.Issues.ShouldBeEmpty(load.Report);
    }

    [Fact]
    public void A_misspelled_module_is_still_said_where_nothing_is_missing()
    {
        PatchLanguage.Build("requires test.shapes\nx |> blobb() |> out.color", WithShapes)
            .Issues.ShouldHaveSingleItem().Code.ShouldBe(IssueCode.UnknownModule);
    }

    [Fact]
    public void A_printing_says_which_plugins_its_modules_come_from()
    {
        var patch = PatchLanguage.Build("x |> blob() |> out.color", WithShapes).Patch;

        var printed = PatchPrinter.Print(patch, WithShapes);

        printed.ShouldStartWith("requires test.shapes");
        PatchLanguage.Build(printed, WithShapes).Issues.ShouldBeEmpty();
    }

    [Fact]
    public void A_patch_of_the_engines_own_modules_requires_nothing() =>
        PatchPrinter.Print(PatchLanguage.Build("x |> sine() |> out.left", NodeCatalog.BuiltIn).Patch, NodeCatalog.BuiltIn)
            .ShouldNotContain("requires");
}
