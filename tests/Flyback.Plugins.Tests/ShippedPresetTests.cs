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
/// built-in catalogue. These are the ones a plugin registers, and until this
/// existed nothing called their <c>Build</c> at all: each plugin tested the one
/// or two presets it happened to care about, so a preset could name a module id
/// that no longer existed and nothing would say so until somebody picked it in
/// the app.
/// <para>
/// Which is exactly what happened. Slow weather guards on a provider id, because
/// it reaches across a boundary for its Filter — and a provider that had been
/// renamed left the guard looking for a plugin nobody ships, so the preset threw
/// the moment it was chosen. A patch that cannot be built is the one failure a
/// preset can have that no snapshot, no compile test and no module test would
/// notice.
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
    /// The same rule the engine's own presets keep, and the plugins were the
    /// worse offenders: an audio plugin's preset used to draw something so the
    /// window was not black, and a video plugin's used to make a noise so the
    /// speakers were not silent. Both halves were decoration, and in each case
    /// the preset's own documentation said so.
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
