using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// A patch printed to be written into text that is already there: a canvas copy
/// pasted into the text view.
/// </summary>
public class PrintedBesideTests
{
    private const string There = """
        let hum = t |> sine(freq: 220)

        group "Bass" {
          let low = sine(freq: 55)
          let lower = low * 0.5
        }

        hum |> out.left
        """;

    [Fact]
    public void A_name_the_text_already_binds_is_not_bound_again()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        b.Add("osc.sine", 0, 0, (1, 330f)).Name = "hum";

        var written = PatchPrinter.Beside(b.Patch, There, NodeCatalog.BuiltIn);
        var built = PatchLanguage.Build(There + "\n" + written, NodeCatalog.BuiltIn);

        built.Ok.ShouldBeTrue(built.Report);
        built.Patch.Nodes.Count(n => n.TypeId == "osc.sine").ShouldBe(3);
    }

    /// <summary>Blocks under one label are one group, so a pasted group takes a label of its own.</summary>
    [Fact]
    public void A_group_the_text_already_opens_is_not_opened_again()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var one = b.Add("osc.sine", 0, 0);
        var two = b.Add("math.mul", 200, 0);

        b.Wire(one, 0, two, 0);
        b.Patch.Groups = [new NodeGroup { Id = Guid.NewGuid(), Name = "Bass", Members = [one.Id, two.Id] }];

        var written = PatchPrinter.Beside(b.Patch, There, NodeCatalog.BuiltIn);
        var built = PatchLanguage.Build(There + "\n" + written, NodeCatalog.BuiltIn);

        built.Ok.ShouldBeTrue(built.Report);
        built.Patch.Groups.ShouldNotBeNull().Count.ShouldBe(2);
        built.Patch.Groups.ShouldAllBe(g => g.Members.Count == 2);
    }
}
