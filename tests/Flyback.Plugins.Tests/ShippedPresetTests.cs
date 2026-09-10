using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// Every patch that ships, built and compiled from the catalogue the app
/// actually runs with.
/// </summary>
/// <remarks>
/// The engine's own presets are covered in <c>PresetRulesTests</c>, against the
/// built-in catalogue. These are the ones a plugin registers, so building every
/// one here is what catches a preset naming a module id the catalogue does not
/// hold — a failure no snapshot, compile test or module test would otherwise
/// notice until somebody picked it in the app.
/// <para>
/// The concrete case: Slow weather guards on a provider id because it reaches
/// across a boundary for its Filter, and a provider that had been renamed left
/// the guard looking for a plugin nobody ships — so the preset threw the moment
/// it was chosen.
/// </para>
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
    /// And every preset names modules the catalogue actually holds, which is the
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
                $"'{name}' places a '{node.TypeId}', which is not in the catalogue");
    }

    /// <summary>
    /// And every preset in the picker arrives placed, plugins' own included: a
    /// preset declares no coordinates (ADR-0070), so one that returned the
    /// builder's patch rather than the placed one would hand the canvas a pile
    /// of modules at the origin.
    /// </summary>
    /// <remarks>
    /// Two things at the same spot is the whole of the check here. The full
    /// non-overlap property is a property of the layout and is tested as one in
    /// <c>PatchLayoutTests</c>; what this catches is a preset that never went
    /// through it.
    /// <para>
    /// A thing rather than a module, because a preset may group its modules and
    /// a group that is shut is one box drawn in place of several. What is behind
    /// a box is parked there and may be parked anywhere — the layout keeps no
    /// room for a picture nobody is looking at — so it is the box that is
    /// counted, at the corner it is drawn from. A preset that never went through
    /// the layout still fails: every box corner and every loose module is the
    /// origin.
    /// </para>
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
    /// And every preset lays out clear of itself once its groups have been taken
    /// off, which is a thing a person does to a patch they have been handed and
    /// want to see the whole of.
    /// </summary>
    /// <remarks>
    /// The plugins' presets are the big ones — Slow weather is a hundred and five
    /// modules in ten groups — and the size is the point. Ten boxes take three
    /// columns; the hundred modules behind them take seventeen, which is wider
    /// than half the canvas. A layout drawn from the origin rightwards had only
    /// that half to put them in, so the far end arrived folded onto the boundary
    /// by <see cref="NodeInstance.X"/> and stacked there. Nothing in
    /// <c>PatchLayoutTests</c> is large enough to reach it.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Every))]
    public void Every_preset_lays_out_clear_of_itself_with_its_groups_taken_off(string name)
    {
        var loaded = PluginHost.Load();
        var patch = loaded.Presets.Single(p => p.Name == name).Build(loaded.Modules);

        patch.Groups = null;

        PatchLayout.Arrange(patch, loaded.Modules)
            .ShouldBeTrue($"'{name}' should fit the canvas with its groups off");

        NothingOverlaps(patch, loaded.Modules, name);
    }

    /// <summary>
    /// Opening every group at once is the same again, and the one case that may
    /// honestly not fit: an open group is drawn as a ring round its modules, so
    /// several of them in a row take the room all of their modules take and a
    /// large patch can want more canvas than there is.
    /// </summary>
    /// <remarks>
    /// So the claim is the one that is always true rather than the one that is
    /// nearly true — a drawing that fits is a drawing with nothing on top of
    /// anything. What must not happen is fitting and overlapping anyway, and
    /// what a patch too big for the canvas gets is a sentence saying so, which
    /// is <c>NodeEditor.Tidy</c>'s to say.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Every))]
    public void A_preset_with_every_group_open_either_fits_the_canvas_or_says_it_does_not(string name)
    {
        var loaded = PluginHost.Load();
        var patch = loaded.Presets.Single(p => p.Name == name).Build(loaded.Modules);

        foreach (var group in patch.Groups ?? []) group.Collapsed = false;

        if (PatchLayout.Arrange(patch, loaded.Modules)) NothingOverlaps(patch, loaded.Modules, name);
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
