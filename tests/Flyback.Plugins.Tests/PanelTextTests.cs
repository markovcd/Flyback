using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// Every shipped patch written out as text and read back: the same programs,
/// played and not, the same panel with every socket following the same knob
/// over the same range, and the same boxes round the same modules.
/// </summary>
public class PanelTextTests
{
    private static ModuleCatalog Catalog => ShippedPlugins.Loaded.Modules;

    public static TheoryData<string> Shipped => [.. ShippedPlugins.Loaded.Presets.Select(preset => preset.Name)];

    [Fact]
    public void Some_shipped_preset_has_a_panel_and_some_has_boxes()
    {
        var built = ShippedPlugins.Loaded.Presets.Select(preset => preset.Build(Catalog)).ToList();

        built.ShouldContain(patch => patch.Controls != null && patch.Controls.Count > 0);
        built.ShouldContain(patch => patch.Groups != null && patch.Groups.Count > 0);
    }

    [Theory]
    [MemberData(nameof(Shipped))]
    public void A_shipped_preset_is_the_same_patch_through_the_text(string name) =>
        Survives(ShippedPlugins.Loaded.Presets.Single(preset => preset.Name == name).Build(Catalog));

    [Fact]
    public void The_preset_sites_played_default_is_the_same_patch_through_the_text()
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

        (again.Controls ?? []).Select(c => (c.Name, c.Value, c.Midi))
            .ShouldBe((patch.Controls ?? []).Select(c => (c.Name, c.Value, c.Midi)));

        Links(again).ShouldBe(Links(patch), source);
        Boxes(again).ShouldBe(Boxes(patch), source);

        foreach (var played in new[] { false, true })
        {
            Fingerprint(again.CompileForVideo(Catalog, played: played).Program)
                .ShouldBe(Fingerprint(patch.CompileForVideo(Catalog, played: played).Program), source);

            Fingerprint(again.CompileForAudio(Catalog, played: played).Program)
                .ShouldBe(Fingerprint(patch.CompileForAudio(Catalog, played: played).Program), source);
        }
    }

    /// <summary>
    /// Which knob each socket follows and how, by the knob's name rather than its
    /// id, sorted. An Expression's sockets are lettered in the order its sum
    /// reads them, so for one of those it is the knob that counts, not the letter.
    /// </summary>
    private static List<string> Links(Patch patch) =>
    [
        .. patch.Nodes
            .SelectMany(node => ControlMap.All(node).Select(linked => (node, linked.Port, linked.Link)))
            .Where(linked => Catalog.Get(linked.node.TypeId) is { } def && Catalog.Normalled(def.Inputs[linked.Port]) is null)
            .Select(linked =>
                $"{linked.node.TypeId}:{(linked.node.TypeId == NodeCatalog.ExpressionTypeId ? "?" : linked.Port)} "
                + $"<- {patch.Control(linked.Link.Control)?.Name} "
                + $"{linked.Link.Min}..{linked.Link.Max} {linked.Link.Knee}")
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>Each box by its name and the kinds of module in it, sorted.</summary>
    private static List<string> Boxes(Patch patch) =>
    [
        .. (patch.Groups ?? []).Select(group =>
                $"{group.Name}: " + string.Join(", ", group.Members
                    .Select(id => patch.Find(id)?.TypeId)
                    .OfType<string>()
                    .Order(StringComparer.Ordinal)))
            .Order(StringComparer.Ordinal),
    ];

    private static IEnumerable<(OpCode, int, int, int, int, float)> Fingerprint(CompiledPatch program) =>
        program.Ops.Select(o => (o.Code, o.Out, o.A, o.B, o.C, o.K));
}
