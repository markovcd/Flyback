using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// Every patch that ships, built and compiled from the catalog the app actually
/// runs with.
/// </summary>
/// <remarks>
/// The engine's own presets are covered in <c>PresetRulesTests</c>; these are the
/// ones a plugin registers, so building every one here catches a preset naming a
/// module id the catalog does not hold — a failure nothing else would notice until
/// somebody picked it. Slow weather is the concrete case: it guards on a provider id
/// for its Filter, and a rename left the guard looking for a plugin nobody ships.
/// </remarks>
public class ShippedPresetTests
{
    public static TheoryData<string> Every =>
        [.. PluginHost.Load().Presets.Select(p => p.Name)];

    /// <summary>Every preset in the picker builds and compiles for both sinks.</summary>
    [Theory]
    [MemberData(nameof(Every))]
    public void Every_preset_builds_and_compiles(string name)
    {
        var loaded = PluginHost.Load();
        var preset = loaded.Presets.Single(p => p.Name == name);

        var patch = Should.NotThrow(() => preset.Build(loaded.Modules));

        patch.Nodes.ShouldContain(n => NodeCatalog.IsSink(n.TypeId), "every patch has an Output");

        foreach (var result in new[]
                 {
                     patch.CompileForVideo(loaded.Modules),
                     patch.CompileForAudio(loaded.Modules),
                 })
        {
            result.HasErrors.ShouldBeFalse(
                string.Join("; ", result.Issues.Select(i => i.Message)));
        }
    }

    /// <summary>
    /// And every preset names modules the catalog actually holds, which is the
    /// half of the above that a guard clause can hide: a preset that throws its
    /// own "that plugin is missing" is not the same as one that works.
    /// </summary>
    [Theory]
    [MemberData(nameof(Every))]
    public void Every_preset_names_modules_that_exist(string name)
    {
        var loaded = PluginHost.Load();
        var patch = loaded.Presets.Single(p => p.Name == name).Build(loaded.Modules);

        foreach (var node in patch.Nodes)
            loaded.Modules.Get(node.TypeId).ShouldNotBeNull(
                $"'{name}' places a '{node.TypeId}', which is not in the catalog");
    }

    /// <summary>
    /// And every preset in the picker arrives placed, plugins' own included: a preset
    /// declares no coordinates (ADR-0070), so one that returned the builder's patch
    /// would hand the canvas a pile of modules at the origin.
    /// </summary>
    /// <remarks>
    /// Two things at the same spot is the whole of the check: the full non-overlap
    /// property is the layout's and is tested in <c>PatchLayoutTests</c>. A thing
    /// rather than a module, because what is behind a shut box is parked at its
    /// corner and may be anywhere — so it is the box that is counted.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Every))]
    public void Every_preset_arrives_laid_out(string name)
    {
        var loaded = PluginHost.Load();
        var patch = loaded.Presets.Single(p => p.Name == name).Build(loaded.Modules);

        var drawn = patch.Nodes
            .Where(n => patch.CollapsedGroupOf(n.Id) is null)
            .Select(n => (n.X, n.Y))
            .Concat((patch.Groups ?? [])
                .Where(box => box.Collapsed)
                .Select(box => (
                    X: box.Members.Min(id => patch.Find(id)!.X),
                    Y: box.Members.Min(id => patch.Find(id)!.Y))))
            .ToList();

        drawn.ToHashSet().Count.ShouldBe(
            drawn.Count, $"'{name}' hands over modules stacked on one another");
    }

    /// <summary>
    /// And every preset lays out clear of itself once its groups have been taken off,
    /// which is what a person does to a patch they have been handed.
    /// </summary>
    /// <remarks>
    /// The plugins' presets are the big ones and the size is the point: Slow weather
    /// is ten boxes across three columns, and the ninety-four modules behind them
    /// take sixteen — nothing in <c>PatchLayoutTests</c> reaches the end of the
    /// canvas.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Every))]
    public void Every_preset_lays_out_clear_of_itself_with_its_groups_taken_off(string name)
    {
        var loaded = PluginHost.Load();
        var patch = loaded.Presets.Single(p => p.Name == name).Build(loaded.Modules);

        patch.Groups = null;

        PatchLayout.Arrange(patch, loaded.Modules)
            .Fitted.ShouldBeTrue($"'{name}' should fit the canvas with its groups off");

        NothingOverlaps(patch, loaded.Modules, name);
    }

    /// <summary>
    /// Opening every group at once fits too, by shutting boxes again where it must:
    /// an open group is a ring round a sub-drawing of its own, so several in a row can
    /// want more canvas than there is (ADR-0092).
    /// </summary>
    [Theory]
    [MemberData(nameof(Every))]
    public void A_preset_with_every_group_open_is_laid_out_to_fit(string name)
    {
        var loaded = PluginHost.Load();
        var patch = loaded.Presets.Single(p => p.Name == name).Build(loaded.Modules);

        foreach (var group in patch.Groups ?? []) group.Collapsed = false;

        PatchLayout.Arrange(patch, loaded.Modules)
            .Fitted.ShouldBeTrue($"'{name}' should fit the canvas with boxes shut as needed");

        NothingOverlaps(patch, loaded.Modules, name);
    }

    /// <summary>
    /// And Mycelium is the preset that needs it: two hundred and seventy-one modules
    /// in twenty-three boxes is some twenty-four thousand units wide with every box
    /// open, against a canvas of fifteen.
    /// </summary>
    [Fact]
    public void Mycelium_with_every_box_open_is_too_wide_for_the_canvas_and_has_boxes_shut()
    {
        var loaded = PluginHost.Load();
        var patch = loaded.Presets.Single(p => p.Name == "Mycelium").Build(loaded.Modules);

        foreach (var group in patch.Groups ?? []) group.Collapsed = false;

        var laid = PatchLayout.Arrange(patch, loaded.Modules);

        laid.Fitted.ShouldBeTrue();
        laid.Shut.ShouldNotBeEmpty();

        // Shut to make it fit and no further: the drawing it settled on still has
        // boxes open, so what was closed was closed for the canvas and not for luck.
        laid.Shut.Count.ShouldBeLessThan(patch.Groups!.Count);

        foreach (var group in laid.Shut) group.Collapsed.ShouldBeTrue();
    }

    /// <summary>Nothing drawn sits on anything else drawn.</summary>
    private static void NothingOverlaps(Patch patch, ModuleCatalog modules, string name)
    {
        var size = PatchLayout.Metrics.Default;

        var drawn = patch.Nodes
            .Where(n => patch.CollapsedGroupOf(n.Id) is null)
            .Select(n => (n.TypeId, n.X, n.Y, Height: size.Height(modules.Require(n.TypeId))))
            .ToArray();

        for (var a = 0; a < drawn.Length; a++)
        for (var b = a + 1; b < drawn.Length; b++)
        {
            var (one, two) = (drawn[a], drawn[b]);

            var apart = one.X + size.Width <= two.X || two.X + size.Width <= one.X
                || one.Y + one.Height <= two.Y || two.Y + two.Height <= one.Y;

            apart.ShouldBeTrue($"'{name}' draws {one.TypeId} on top of {two.TypeId}");
        }
    }

    /// <summary>
    /// The one-idea-one-sink rule, applied to the presets a plugin registers.
    /// </summary>
    /// <remarks>
    /// The same rule the engine's own presets keep. A preset that reaches a
    /// sink only for decoration — drawing something so the window is not black,
    /// or making a noise so the speakers are not silent — should drop that half
    /// rather than keep it for its own sake.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Every))]
    public void A_preset_about_one_idea_reaches_one_sink(string name)
    {
        var loaded = PluginHost.Load();
        var preset = loaded.Presets.Single(p => p.Name == name);

        if (preset.Kind is not (PresetKind.Idea or PresetKind.Interplay)) return;

        var patch = preset.Build(loaded.Modules);
        var sink = patch.Nodes.Single(n => NodeCatalog.IsSink(n.TypeId));

        var draws = Driven(patch, sink, NodeCatalog.OutputColorPort);
        var sounds = Driven(patch, sink, NodeCatalog.OutputLeftPort, NodeCatalog.OutputRightPort);

        if (preset.Kind is PresetKind.Interplay)
        {
            draws.ShouldBeTrue($"'{name}' is about both sinks and draws nothing");
            sounds.ShouldBeTrue($"'{name}' is about both sinks and makes no sound");
            return;
        }

        (draws && sounds).ShouldBeFalse(
            $"'{name}' teaches one idea and carries both sinks — either the other half is "
            + $"decoration and should go, or it should say {nameof(PresetKind)}."
            + $"{nameof(PresetKind.Interplay)}");

        (draws || sounds).ShouldBeTrue($"'{name}' reaches neither sink and does nothing at all");
    }

    private static bool Driven(Patch patch, NodeInstance sink, params int[] ports) =>
        patch.Connections.Any(wire =>
            wire.TargetNode == sink.Id && ports.Contains(wire.TargetPort));
}
