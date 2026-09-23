using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// Filter, Noise, Slew, Drive, Delay and Reverb answered to ids of the Voice and
/// Effects plugins before ADR-0128 moved them into the engine. Every door a type
/// id comes in through still takes the old one.
/// </summary>
public class LegacyTypeIdTests
{
    public static TheoryData<string, string> Aliases => new()
    {
        { "flyback.voice.filter", NodeCatalog.FilterTypeId },
        { "flyback.voice.random", NodeCatalog.NoiseTypeId },
        { "flyback.voice.slew", NodeCatalog.SlewTypeId },
        { "flyback.voice.drive", NodeCatalog.DriveTypeId },
        { "flyback.effects.delay", NodeCatalog.DelayTypeId },
        { "flyback.effects.reverb", NodeCatalog.ReverbTypeId },
    };

    [Theory]
    [MemberData(nameof(Aliases))]
    public void The_catalog_resolves_the_old_id_to_the_new_modules_definition(string oldId, string newId)
    {
        var def = NodeCatalog.BuiltIn.Get(oldId).ShouldNotBeNull();

        def.ShouldBeSameAs(NodeCatalog.BuiltIn.Require(newId));
    }

    [Theory]
    [MemberData(nameof(Aliases))]
    public void Reading_a_file_rewrites_the_old_id_to_the_new_one(string oldId, string newId)
    {
        var json = $$"""
            {
              "Nodes": [ { "Id": "8f9d1d3e-0000-4000-8000-000000000010", "TypeId": "{{oldId}}" } ],
              "Connections": []
            }
            """;

        var loaded = PatchIO.Read(json, NodeCatalog.BuiltIn);

        loaded.UnknownModules.ShouldBeEmpty();
        loaded.Patch.Nodes.Select(n => n.TypeId).ShouldContain(newId);
        loaded.Patch.Nodes.Select(n => n.TypeId).ShouldNotContain(oldId);
    }

    [Theory]
    [MemberData(nameof(Aliases))]
    public void Saving_after_reading_writes_the_new_id_rather_than_the_old(string oldId, string newId)
    {
        var json = $$"""
            {
              "Nodes": [ { "Id": "8f9d1d3e-0000-4000-8000-000000000011", "TypeId": "{{oldId}}" } ],
              "Connections": []
            }
            """;

        var loaded = PatchIO.Read(json, NodeCatalog.BuiltIn);
        var written = PatchIO.ToJson(loaded.Patch, NodeCatalog.BuiltIn);

        written.ShouldContain($"\"{newId}\"");
        written.ShouldNotContain(oldId);
    }

    /// <summary>
    /// A file stamped before the move names the plugin its modules came from at
    /// the time — and a Filter or a Delay no longer needs one. Recomputed on
    /// read, so a build with neither Voice nor Effects installed still opens it.
    /// </summary>
    [Fact]
    public void A_file_that_only_used_a_moved_module_is_no_longer_reported_as_needing_its_old_plugin()
    {
        var json = """
            {
              "Requires": [ { "Id": "flyback.voice", "Name": "Voice" } ],
              "Nodes": [ { "Id": "8f9d1d3e-0000-4000-8000-000000000012", "TypeId": "flyback.voice.filter" } ],
              "Connections": []
            }
            """;

        var loaded = PatchIO.Read(json, NodeCatalog.BuiltIn);

        loaded.IsComplete.ShouldBeTrue(loaded.Summary);
        loaded.MissingProviders.ShouldBeEmpty();
    }

    /// <summary>A patch using both a moved module and one still in a plugin keeps needing that plugin.</summary>
    [Fact]
    public void A_file_that_also_uses_a_module_still_in_the_plugin_keeps_needing_it()
    {
        var provider = new ModuleProvider("test.stayed", "Stayed");
        var stillPluginOnly = new NodeDef(
            "test.stayed.thing", "Thing", "Test", [], [new PortSpec("out")], (em, _) => [em.Constant(0f)]);
        var catalog = NodeCatalog.BuiltIn.With(provider, [stillPluginOnly]).Catalog;

        var json = $$"""
            {
              "Requires": [ { "Id": "flyback.voice", "Name": "Voice" }, { "Id": "test.stayed", "Name": "Stayed" } ],
              "Nodes": [
                { "Id": "8f9d1d3e-0000-4000-8000-000000000013", "TypeId": "flyback.voice.filter" },
                { "Id": "8f9d1d3e-0000-4000-8000-000000000014", "TypeId": "test.stayed.thing" }
              ],
              "Connections": []
            }
            """;

        var loaded = PatchIO.Read(json, catalog);

        loaded.IsComplete.ShouldBeTrue(loaded.Summary);

        var written = PatchIO.ToJson(loaded.Patch, catalog);
        written.ShouldContain("test.stayed");
        written.ShouldNotContain("flyback.voice");
    }

    [Theory]
    [MemberData(nameof(Aliases))]
    public void A_bundle_carries_a_patch_saved_under_the_old_id_and_reads_it_back_under_the_new(
        string oldId, string newId)
    {
        var json = $$"""
            {
              "Nodes": [ { "Id": "8f9d1d3e-0000-4000-8000-000000000015", "TypeId": "{{oldId}}" } ],
              "Connections": []
            }
            """;

        var patch = PatchIO.Read(json, NodeCatalog.BuiltIn).Patch;

        using var archive = new MemoryStream();
        PatchBundle.Write(archive, patch, _ => null, NodeCatalog.BuiltIn);
        archive.Position = 0;

        var reopened = PatchBundle.Read(archive, NodeCatalog.BuiltIn);

        reopened.Patch.Nodes.Select(n => n.TypeId).ShouldContain(newId);
    }

    /// <summary>
    /// The text language resolves an exact type id straight off the catalog
    /// (ADR-0065), and the alias sits under that lookup, so a full old id typed
    /// out by hand still builds.
    /// </summary>
    [Theory]
    [MemberData(nameof(Aliases))]
    public void The_text_language_still_builds_a_type_id_written_out_in_full(string oldId, string newId)
    {
        var load = PatchLanguage.Build($"x |> {oldId}() |> out.left", NodeCatalog.BuiltIn);

        load.Issues.ShouldBeEmpty(load.Report);
        load.Patch.Nodes.Select(n => n.TypeId).ShouldContain(newId);
    }
}
