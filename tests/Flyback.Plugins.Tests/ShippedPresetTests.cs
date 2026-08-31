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
