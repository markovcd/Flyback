using System.Text.Json.Nodes;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// One file per layout <see cref="PatchIO"/> has ever written, kept in <c>layouts/</c>
/// as that build saved it. A raise of <see cref="PatchIO.FormatVersion"/> adds the new
/// layout's files beside the old, and the old ones keep opening.
/// </summary>
public class SavedLayoutTests
{
    private static string Folder => Path.Combine(AppContext.BaseDirectory, "Graph", "layouts");

    public static TheoryData<string> Layouts() =>
        [.. Directory.GetFiles(Folder).Select(file => Path.GetFileName(file)).Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Every_layout_ever_written_still_opens(string name)
    {
        var opened = Open(name);
        var load = opened.Load.ShouldNotBeNull();

        load.IsComplete.ShouldBeTrue(load.Summary);
        load.Version.ShouldBeLessThanOrEqualTo(PatchIO.FormatVersion);

        if (!name.EndsWith(PatchBundle.Extension, StringComparison.Ordinal)) return;

        opened.Files.Keys.ShouldBe(PatchBundle.Files(opened.Patch, NodeCatalog.BuiltIn), ignoreOrder: true);
    }

    [Fact]
    public void The_current_layout_has_a_file_of_each_kind()
    {
        var kept = Layouts().Select(row => row.Data).ToList();

        kept.ShouldContain($"v{PatchIO.FormatVersion}.{PatchIO.FileExtension}", "a raise of the layout adds its own files beside the old ones");
        kept.ShouldContain($"v{PatchIO.FormatVersion}{PatchBundle.Extension}");
    }

    /// <summary>Every field the current layout holds comes back out as it went in.</summary>
    [Fact]
    public void A_file_in_the_current_layout_writes_back_as_it_was()
    {
        var name = $"v{PatchIO.FormatVersion}.{PatchIO.FileExtension}";
        var opened = Open(name);

        JsonNode.DeepEquals(
                JsonNode.Parse(File.ReadAllText(Path.Combine(Folder, name))),
                JsonNode.Parse(PatchIO.ToJson(opened.Patch, NodeCatalog.BuiltIn)))
            .ShouldBeTrue();
    }

    private static LoadedBundle Open(string name) =>
        PatchFile.Read(name, File.ReadAllBytes(Path.Combine(Folder, name)), NodeCatalog.BuiltIn);
}
