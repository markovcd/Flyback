using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The shipped patches that have a panel, written out as text and read back:
/// the same knobs, and every socket following the same knob over the same range.
/// </summary>
public class PanelTextTests
{
    private static ModuleCatalog Catalog => ShippedPlugins.Loaded.Modules;

    public static TheoryData<string> Played =>
    [
        .. ShippedPlugins.Loaded.Presets
            .Where(preset => preset.Build(Catalog).Controls is { Count: > 0 })
            .Select(preset => preset.Name),
    ];

    [Fact]
    public void Some_shipped_preset_has_a_panel() => Played.ShouldNotBeEmpty();

    [Theory]
    [MemberData(nameof(Played))]
    public void A_played_preset_keeps_its_panel_through_the_text(string name) =>
        Survives(ShippedPlugins.Loaded.Presets.Single(preset => preset.Name == name).Build(Catalog));

    [Fact]
    public void The_preset_sites_played_default_keeps_its_panel_through_the_text()
    {
        using var archive = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Defaults", "Tranquility.fbkb"));

        Survives(PatchBundle.Read(archive, Catalog).Patch);
    }

    private static void Survives(Patch patch)
    {
        var source = PatchPrinter.Print(patch, Catalog);
        var load = PatchLanguage.Build(source, Catalog);

        load.Issues.ShouldBeEmpty($"{load.Report}{Environment.NewLine}{source}");

        var again = load.Patch;

        again.Controls.ShouldNotBeNull().Select(c => (c.Name, c.Value, c.Midi))
            .ShouldBe(patch.Controls!.Select(c => (c.Name, c.Value, c.Midi)));

        Links(again).ShouldBe(Links(patch), source);

        foreach (var played in new[] { false, true })
        {
            Fingerprint(again.CompileForVideo(Catalog, played: played).Program)
                .ShouldBe(Fingerprint(patch.CompileForVideo(Catalog, played: played).Program), source);

            Fingerprint(again.CompileForAudio(Catalog, played: played).Program)
                .ShouldBe(Fingerprint(patch.CompileForAudio(Catalog, played: played).Program), source);
        }
    }

    /// <summary>Which knob each socket follows and how, by the knob's name rather than its id, sorted.</summary>
    private static List<string> Links(Patch patch) =>
    [
        .. patch.Nodes
            .SelectMany(node => ControlMap.All(node).Select(linked => (node, linked.Port, linked.Link)))
            .Where(linked => Catalog.Get(linked.node.TypeId) is { } def && Catalog.Normalled(def.Inputs[linked.Port]) is null)
            .Select(linked =>
                $"{linked.node.TypeId}:{linked.Port} <- {patch.Control(linked.Link.Control)?.Name} "
                + $"{linked.Link.Min}..{linked.Link.Max} {linked.Link.Knee}")
            .Order(StringComparer.Ordinal),
    ];

    private static IEnumerable<(OpCode, int, int, int, int, float)> Fingerprint(CompiledPatch program) =>
        program.Ops.Select(o => (o.Code, o.Out, o.A, o.B, o.C, o.K));
}
