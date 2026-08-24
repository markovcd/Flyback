using System.Text.Json;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// A module may be renamed, and a module nobody renamed is called whatever its
/// definition calls it.
/// </summary>
/// <remarks>
/// The name is a label and nothing else: nothing is ever found by it, so there
/// is no uniqueness to keep and no rule about what may be typed. What there is
/// instead is one meaning of "no name" — null, never an empty string and never a
/// copy of the definition's own name — because that is what makes a module
/// follow its definition when the catalogue renames it, and what keeps the name
/// out of the file of every patch that never used the feature.
/// </remarks>
public class NodeNameTests
{
    private static NodeDef Sine => NodeCatalog.BuiltIn.Require("osc.sine");

    private static NodeInstance Placed() => NodeInstance.Create(Sine, 40, 20);

    [Fact]
    public void A_module_nobody_renamed_is_called_what_its_definition_is()
    {
        var node = Placed();

        node.Name.ShouldBeNull();
        node.Title(Sine).ShouldBe("Sine");
    }

    [Fact]
    public void A_renamed_module_is_called_what_it_was_renamed_to()
    {
        var node = Placed();

        node.Rename(Sine, "Wobble");

        node.Name.ShouldBe("Wobble");
        node.Title(Sine).ShouldBe("Wobble");
    }

    /// <summary>
    /// The only way to ask for the default back, and the one the panel offers:
    /// empty the box. Whitespace counts as empty — a name of three spaces is a
    /// header with nothing in it, which is not something to be able to ask for.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t ")]
    public void An_empty_name_puts_the_definitions_back(string? blank)
    {
        var node = Placed();
        node.Rename(Sine, "Wobble");

        node.Rename(Sine, blank);

        node.Name.ShouldBeNull();
        node.Title(Sine).ShouldBe("Sine");
    }

    [Fact]
    public void A_name_is_kept_without_the_space_around_it()
    {
        var node = Placed();

        node.Rename(Sine, "  Bass  ");

        node.Name.ShouldBe("Bass");
    }

    /// <summary>
    /// Typing out what it is already called is not a rename. Storing it would
    /// leave a file asserting a name that would then stop following the module
    /// the day the catalogue renames it.
    /// </summary>
    [Fact]
    public void The_definitions_own_name_is_not_a_rename()
    {
        var node = Placed();

        node.Rename(Sine, "  Sine ");

        node.Name.ShouldBeNull();
    }

    [Fact]
    public void A_name_past_the_limit_is_cut_to_it()
    {
        var node = Placed();

        node.Rename(Sine, new string('x', NodeInstance.NameLimit + 40));

        node.Name.ShouldNotBeNull().Length.ShouldBe(NodeInstance.NameLimit);
    }

    [Fact]
    public void A_name_survives_being_written_and_read()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var sine = b.Add("osc.sine", 40, 20);
        var screen = b.Add(NodeCatalog.OutputTypeId, 400, 20);
        b.Wire(sine, 0, screen, NodeCatalog.OutputColorPort);

        sine.Rename(Sine, "Wobble");

        var read = PatchIo.Read(PatchIo.ToJson(b.Patch, NodeCatalog.BuiltIn), NodeCatalog.BuiltIn);

        read.IsComplete.ShouldBeTrue(read.Summary);
        read.Patch.Find(sine.Id).ShouldNotBeNull().Name.ShouldBe("Wobble");
    }

    /// <summary>
    /// Nothing about the file changes for a patch nobody renamed anything in,
    /// which is what lets this land without a format version to go with it.
    /// </summary>
    [Fact]
    public void An_unrenamed_module_writes_no_name_at_all()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        b.Add("osc.sine", 40, 20);
        b.Add(NodeCatalog.OutputTypeId, 400, 20);

        using var document = JsonDocument.Parse(PatchIo.ToJson(b.Patch, NodeCatalog.BuiltIn));

        foreach (var node in document.RootElement.GetProperty(nameof(Patch.Nodes)).EnumerateArray())
            node.TryGetProperty(nameof(NodeInstance.Name), out _).ShouldBeFalse();
    }

    [Fact]
    public void Copying_a_module_copies_what_it_is_called()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var sine = b.Add("osc.sine", 40, 20);
        b.Add(NodeCatalog.OutputTypeId, 400, 20);
        sine.Rename(Sine, "Wobble");

        var fragment = PatchClipboard.Copy(b.Patch, [sine.Id]);
        var pasted = PatchClipboard.Paste(new Patch(), fragment);

        pasted.ShouldHaveSingleItem().Name.ShouldBe("Wobble");
    }
}
