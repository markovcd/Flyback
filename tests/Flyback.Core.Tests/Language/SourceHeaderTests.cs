using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>What a source file says of itself, read without building it: how a shipped preset is offered.</summary>
public class SourceHeaderTests
{
    [Fact]
    public void The_description_and_tags_are_read_as_the_patch_holds_them()
    {
        var header = SourceHeader.Read("description \"A slow tide,\"\n  \"heard and seen.\"\ntags \"Ambient\" \"slow tide\"\nsine(freq: 220) |> out.left");

        header.Description.ShouldBe("A slow tide, heard and seen.");
        header.Tags.ShouldBe(["ambient", "slow-tide"]);
    }

    [Fact]
    public void A_file_that_says_nothing_has_an_empty_header()
    {
        var header = SourceHeader.Read("sine(freq: 220) |> out.left");

        header.Description.ShouldBeEmpty();
        header.Tags.ShouldBeNull();
    }

    [Fact]
    public void A_module_the_header_reader_does_not_know_is_no_matter()
    {
        SourceHeader.Read("description \"Two of them.\"\nnobody.knows(x: 1) |> out.left").Description.ShouldBe("Two of them.");
    }

    [Fact]
    public void Every_engine_preset_is_offered_with_what_its_patch_says()
    {
        foreach (var preset in Presets.Shipped)
        {
            var patch = preset.Build(NodeCatalog.BuiltIn);

            preset.Description.ShouldBe(patch.Description, preset.Name);
            preset.Tags.ShouldBe(patch.Tags, preset.Name);
        }
    }
}
