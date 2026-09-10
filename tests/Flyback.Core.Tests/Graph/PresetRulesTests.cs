using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The rules the shipped patches keep, as tests rather than as a convention.
/// </summary>
/// <remarks>
/// A preset is written to teach something, and the failure mode is always the same
/// shape: a second half at the other sink that is not what it is teaching. Checked
/// against the engine's own catalogue, which is also the promise a shipped preset
/// makes — it must never need a plugin installed.
/// </remarks>
public class PresetRulesTests
{
    public static TheoryData<string> Every => [.. Presets.All.Select(p => p.Name)];

    public static TheoryData<string> Ideas =>
        [.. Presets.All.Where(p => p.Kind is PresetKind.Idea).Select(p => p.Name)];

    private static PatchPreset Preset(string name) => Presets.All.Single(p => p.Name == name);

    /// <summary>
    /// A patch teaching one idea reaches one sink: about sound it draws nothing,
    /// about the picture it makes no sound.
    /// </summary>
    /// <remarks>
    /// Read off the compiled programs rather than the wires, because that is where
    /// the question is settled — a module nothing downstream of the walk reaches
    /// emits no ops. The exemptions are named on the preset:
    /// <see cref="PresetKind.Interplay"/> must reach both, and
    /// <see cref="PresetKind.Showcase"/> is not teaching one thing at all.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Ideas))]
    public void A_patch_about_one_idea_reaches_one_sink(string name)
    {
        var patch = Preset(name).Build(NodeCatalog.BuiltIn);

        var draws = Draws(patch);
        var sounds = Sounds(patch);

        (draws && sounds).ShouldBeFalse(
            $"'{name}' teaches one idea and carries both sinks — either the other half is "
            + "decoration and should go, or the patch is about the two meeting and should say "
            + $"so with {nameof(PresetKind)}.{nameof(PresetKind.Interplay)}");

        (draws || sounds).ShouldBeTrue($"'{name}' reaches neither sink and does nothing at all");
    }

    /// <summary>
    /// And a patch that says it is about the two sinks meeting really reaches
    /// both — the same rule from the other side, and the one that stops
    /// <see cref="PresetKind.Interplay"/> becoming a way of opting out.
    /// </summary>
    [Fact]
    public void A_patch_about_the_two_sinks_reaches_both()
    {
        foreach (var preset in Presets.All.Where(p => p.Kind is PresetKind.Interplay))
        {
            var patch = preset.Build(NodeCatalog.BuiltIn);

            Draws(patch)
                .ShouldBeTrue($"'{preset.Name}' claims to be about both sinks and draws nothing");

            Sounds(patch)
                .ShouldBeTrue($"'{preset.Name}' claims to be about both sinks and makes no sound");
        }
    }

    /// <summary>Every shipped patch compiles clean for both sinks.</summary>
    /// <remarks>
    /// Warnings are allowed and are load-bearing for two of them: Clip and
    /// Picture in ship with no file chosen, and the warning saying so is the
    /// patch's first instruction. Errors are not.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Every))]
    public void Every_preset_compiles(string name)
    {
        var patch = Preset(name).Build(NodeCatalog.BuiltIn);

        foreach (var result in new[]
                 {
                     patch.CompileForVideo(NodeCatalog.BuiltIn),
                     patch.CompileForAudio(NodeCatalog.BuiltIn),
                 })
        {
            result.HasErrors.ShouldBeFalse(
                string.Join("; ", result.Issues.Select(i => i.Message)));
        }
    }

    /// <summary>Every shipped patch says what it is for.</summary>
    /// <remarks>
    /// The description is what the picker shows under the name, so a preset
    /// without one is a row that says less than it could. Empty is allowed for
    /// nothing except the blank canvas, which has nothing to say.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Every))]
    public void Every_preset_describes_itself(string name)
    {
        var preset = Preset(name);

        if (preset.Kind is PresetKind.Blank) return;

        preset.Description.ShouldNotBeNullOrWhiteSpace();
        preset.Description.Length.ShouldBeLessThan(
            120, "a subtitle is a sentence, not a paragraph");
    }
    /// <summary>
    /// Whether anything in the patch drives a given sink.
    /// </summary>
    /// <remarks>
    /// Asked of the wires rather than the compiled program, because a sink always
    /// emits something: the speakers' program multiplies by the gain and clamps
    /// whether or not anything is patched in. What settles it is whether the
    /// Output's own socket has a wire in it, which is where the compiler's walk
    /// starts.
    /// </remarks>
    private static bool Driven(Patch patch, params int[] ports) =>
        patch.Connections.Any(wire =>
            wire.TargetNode == Sink(patch).Id && ports.Contains(wire.TargetPort));

    private static NodeInstance Sink(Patch patch) =>
        patch.Nodes.Single(n => NodeCatalog.IsSink(n.TypeId));

    private static bool Draws(Patch patch) => Driven(patch, NodeCatalog.OutputColorPort);

    private static bool Sounds(Patch patch) =>
        Driven(patch, NodeCatalog.OutputLeftPort, NodeCatalog.OutputRightPort);
}
