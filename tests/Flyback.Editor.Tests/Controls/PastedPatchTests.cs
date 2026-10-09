using Flyback.Core.Graph;
using Flyback.Editor.Controls;
using Flyback.Engine.Graph;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Controls;

/// <summary>What a paste into the text makes of a patch file, and what it leaves as it is.</summary>
public class PastedPatchTests
{
    [Fact]
    public void Text_that_is_not_a_patch_file_pastes_as_it_is()
    {
        PastedPatch.Written("osc.sine() |> out.left", "", out var refused).ShouldBeNull();
        refused.ShouldBeNull();
    }

    [Fact]
    public void Text_that_starts_like_a_patch_file_but_does_not_read_pastes_as_it_is()
    {
        PastedPatch.Written("{ not a patch", "", out var refused).ShouldBeNull();
        refused.ShouldBeNull();
    }

    [Fact]
    public void A_patch_file_this_run_cannot_read_whole_is_refused_and_pastes_nothing()
    {
        var patch = new Patch();
        patch.Nodes.Add(new NodeInstance { Id = Guid.NewGuid(), TypeId = "nobody.has.this", InputValues = [] });

        PastedPatch.Written(PatchIO.ToJson(patch), "", out var refused).ShouldBe(string.Empty);
        refused.ShouldNotBeNull().ShouldStartWith("Not pasted.");
    }

    [Fact]
    public void A_patch_file_with_nothing_in_it_pastes_nothing()
    {
        PastedPatch.Written(PatchIO.ToJson(new Patch()), "", out var refused).ShouldBe(string.Empty);
        refused.ShouldBeNull();
    }

    [Fact]
    public void A_patch_file_is_written_as_the_text_its_modules_would_be()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var tone = builder.Add("osc.sine", 0, 0);
        var speaker = builder.Add(NodeCatalog.OutputTypeId, 0, 0);
        var patch = builder.Wire(tone, 0, speaker, NodeCatalog.OutputLeftPort).Patch;

        var written = PastedPatch.Written(PatchIO.ToJson(patch), "", out var refused);

        written.ShouldNotBeNull().ShouldContain("sine");
        refused.ShouldBeNull();
    }
}
