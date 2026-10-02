using Flyback.Core.Graph;
using Flyback.Engine.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>A text patch reads the same whichever plugins a program has loaded.</summary>
public class PluginShortNameTests
{
    private const string Text = "let tint = hsv(hue: 0.6, saturation: 0.8, value: 0.9)\nrings() |> out.color";

    private static readonly ModuleProvider Pictures = new("test.pictures", "Pictures");

    private static NodeDef Module(string typeId) => new(
        typeId, typeId, "Test",
        [new PortSpec("in")],
        [new PortSpec("out")],
        (em, i) => [em.Mul(i[0], 2f)]);

    [Fact]
    public void A_plugin_module_does_not_take_a_built_in_modules_short_name()
    {
        var plugged = NodeCatalog.BuiltIn.With(Pictures, [Module("test.pictures.hsv")]).Catalog;

        var bare = PatchLanguage.Build(Text, NodeCatalog.BuiltIn);
        var with = PatchLanguage.Build(Text, plugged);

        with.Report.ShouldBe(bare.Report);
        with.Issues.ShouldNotContain(issue => issue.IsError, with.Report);
    }

    [Fact]
    public void A_plugin_module_is_still_reached_in_full()
    {
        var plugged = NodeCatalog.BuiltIn.With(Pictures, [Module("test.pictures.hsv")]).Catalog;

        var load = PatchLanguage.Build("test.pictures.hsv() |> out.color", plugged);

        load.Issues.ShouldNotContain(issue => issue.IsError, load.Report);
    }
}
