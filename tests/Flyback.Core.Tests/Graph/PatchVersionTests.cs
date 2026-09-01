using System.Text.Json;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The layout stamp on a patch file. It earns its place in two directions: a file
/// written before there was a stamp has to keep opening, and a file written after
/// this build's understanding runs out has to be refused with a sentence rather
/// than an exception.
/// </summary>
/// <remarks>
/// <see cref="PatchIoTests"/> covers the body of a file and
/// <see cref="PatchProvenanceTests"/> the plugins it names. This covers the one
/// number that says how to read either of them.
/// </remarks>
public class PatchVersionTests
{
    private static Patch Small()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        var coords = builder.Add("coord", 10, 20);
        var screen = builder.Add(NodeCatalog.OutputTypeId, 300, 20);

        builder.Wire(coords, 0, screen, NodeCatalog.OutputColorPort);

        return builder.Patch;
    }

    /// <summary>The file as text, with one property replaced or added at the top.</summary>
    private static string Stamped(string json, int? version)
    {
        var document = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
            ?? throw new InvalidOperationException("a patch should read back as an object");

        document.Remove(nameof(Patch.Version));

        var rebuilt = new Dictionary<string, JsonElement>();
        if (version is { } stamp)
            rebuilt[nameof(Patch.Version)] = JsonSerializer.SerializeToElement(stamp);

        foreach (var (key, value) in document) rebuilt[key] = value;

        return JsonSerializer.Serialize(rebuilt);
    }

    // --- writing ------------------------------------------------------------

    [Fact]
    public void Every_file_written_carries_the_layout_it_was_written_in()
    {
        var json = PatchIO.ToJson(Small(), NodeCatalog.BuiltIn);

        json.ShouldContain($"\"Version\": {PatchIO.FormatVersion}");
    }

    /// <summary>
    /// The stamp goes on the patch as well as into the text, the way the plugin
    /// list does, so nothing can be holding a patch that disagrees with the file
    /// it was just written to.
    /// </summary>
    [Fact]
    public void Writing_stamps_the_patch_as_well_as_the_file()
    {
        var patch = Small();
        patch.Version.ShouldBeNull("nothing has written it yet");

        PatchIO.ToJson(patch, NodeCatalog.BuiltIn);

        patch.Version.ShouldBe(PatchIO.FormatVersion);
    }

    [Fact]
    public void A_file_reads_back_at_the_layout_it_was_written_in()
    {
        var json = PatchIO.ToJson(Small(), NodeCatalog.BuiltIn);
        var loaded = PatchIO.Read(json, NodeCatalog.BuiltIn);

        loaded.Version.ShouldBe(PatchIO.FormatVersion);
        loaded.TooNew.ShouldBeFalse();
        loaded.IsComplete.ShouldBeTrue(loaded.Summary);
    }

    // --- files older than the stamp -----------------------------------------

    /// <summary>
    /// The compatibility that matters: every patch anybody has already saved was
    /// written without a stamp, and must open exactly as it did before there was
    /// one to write.
    /// </summary>
    [Fact]
    public void A_file_saved_before_there_was_a_stamp_still_opens()
    {
        var original = Small();
        var legacy = Stamped(PatchIO.ToJson(original, NodeCatalog.BuiltIn), null);

        legacy.ShouldNotContain("Version");

        var loaded = PatchIO.Read(legacy, NodeCatalog.BuiltIn);

        loaded.Version.ShouldBe(PatchIO.FirstVersion);
        loaded.TooNew.ShouldBeFalse();
        loaded.IsComplete.ShouldBeTrue(loaded.Summary);

        // And it is the same patch, not merely a patch.
        loaded.Patch.Nodes.Count.ShouldBe(original.Nodes.Count);
        loaded.Patch.Connections.Count.ShouldBe(original.Connections.Count);
    }

    /// <summary>
    /// A stamp that is not a number is a corrupt file, and is reported as one —
    /// the same way a corrupt anything else in the file is, rather than being
    /// forgiven because of which field it happens to be in.
    /// </summary>
    /// <remarks>
    /// What is pinned here is which complaint comes out. Reading the stamp must
    /// not mistake nonsense for a layout from the future, and must not throw an
    /// error of its own on the way past: the file is malformed, so the ordinary
    /// malformed-file path is the one that should get to speak.
    /// </remarks>
    [Fact]
    public void A_stamp_that_is_not_a_number_is_reported_as_a_corrupt_file()
    {
        var json = Stamped(PatchIO.ToJson(Small(), NodeCatalog.BuiltIn), null)
            .Replace("{", """{"Version": "banana",""", StringComparison.Ordinal);

        Should.Throw<JsonException>(() => PatchIO.Read(json, NodeCatalog.BuiltIn));
    }

    // --- files newer than this build ----------------------------------------

    [Fact]
    public void A_file_from_a_newer_build_is_refused()
    {
        var json = Stamped(PatchIO.ToJson(Small(), NodeCatalog.BuiltIn), PatchIO.FormatVersion + 1);
        var loaded = PatchIO.Read(json, NodeCatalog.BuiltIn);

        loaded.TooNew.ShouldBeTrue();
        loaded.IsComplete.ShouldBeFalse();

        loaded.Summary.ShouldContain("newer version of Flyback");
        loaded.Detail.ShouldNotBeEmpty("a refusal has to say what to do about it");
    }

    /// <summary>
    /// The reason the version is read out of the raw text: a newer layout may be
    /// any shape at all, and deserialising it first would throw where there is a
    /// perfectly good sentence to say instead.
    /// </summary>
    [Fact]
    public void A_file_from_a_newer_build_is_refused_without_being_understood()
    {
        var json = $$"""
            {
              "Version": {{PatchIO.FormatVersion + 1}},
              "Nodes": "this is not what nodes look like",
              "SomethingNobodyHasInventedYet": [1, 2, 3]
            }
            """;

        var loaded = Should.NotThrow(() => PatchIO.Read(json, NodeCatalog.BuiltIn));

        loaded.TooNew.ShouldBeTrue();
        loaded.IsComplete.ShouldBeFalse();
    }

    /// <summary>
    /// Nothing else is reported alongside it. A newer layout means the modules and
    /// plugins read out of the file are guesses, and listing guesses next to the
    /// real complaint would send somebody hunting for a plugin that is not the
    /// problem.
    /// </summary>
    [Fact]
    public void A_file_from_a_newer_build_reports_only_that()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        builder.Add(NodeCatalog.OutputTypeId, 0, 0);

        var json = Stamped(PatchIO.ToJson(builder.Patch, NodeCatalog.BuiltIn), PatchIO.FormatVersion + 1)
            .Replace("\"coord\"", "\"nobody.knows.this\"", StringComparison.Ordinal);

        var loaded = PatchIO.Read(json, NodeCatalog.BuiltIn);

        loaded.MissingProviders.ShouldBeEmpty();
        loaded.UnknownModules.ShouldBeEmpty();
        loaded.Summary.ShouldContain("newer version of Flyback");
    }

    /// <summary>
    /// Refused, but still handed back something usable — every caller of Read
    /// takes a patch off it, and one that refused with a null would move the
    /// refusal into their null checks instead of their error reporting.
    /// </summary>
    [Fact]
    public void A_refused_file_still_comes_back_with_a_patch_to_hold()
    {
        var json = Stamped(PatchIO.ToJson(Small(), NodeCatalog.BuiltIn), PatchIO.FormatVersion + 9);
        var loaded = PatchIO.Read(json, NodeCatalog.BuiltIn);

        loaded.Patch.ShouldNotBeNull();
        loaded.Patch.Nodes.ShouldContain(n => n.TypeId == NodeCatalog.OutputTypeId);
    }

    // --- the constants themselves -------------------------------------------

    /// <summary>
    /// A file that was legal once stays legal, so the oldest layout can never move
    /// and the current one can never drop below it.
    /// </summary>
    [Fact]
    public void The_oldest_layout_is_one_and_the_current_one_is_no_older()
    {
        PatchIO.FirstVersion.ShouldBe(1);
        PatchIO.FormatVersion.ShouldBeGreaterThanOrEqualTo(PatchIO.FirstVersion);
    }
}
