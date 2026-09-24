using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The "Echo chamber" preset, which pairs the engine's own Delay and Reverb.
/// Both modules' own behavior is covered in <c>Flyback.Core.Tests</c>; this checks
/// the shipped preset builds and compiles clean.
/// </summary>
public class SpaceTests
{
    [Fact]
    public void The_preset_builds_and_compiles_for_both_sinks()
    {
        var loaded = ShippedPlugins.Loaded;
        var patch = loaded.Presets.Single(p => p.Name == "Echo chamber").Build(loaded.Modules);

        patch.Nodes.Select(n => n.TypeId).ShouldContain(NodeCatalog.DelayTypeId);
        patch.Nodes.Select(n => n.TypeId).ShouldContain(NodeCatalog.ReverbTypeId);

        patch.CompileForVideo(loaded.Modules).Issues.ShouldBeEmpty();

        var audio = patch.CompileForAudio(loaded.Modules);
        audio.Issues.ShouldBeEmpty();

        // One delay line for the echo, and the reverb's seventeen — a pre-delay,
        // eight combs, and four allpasses for each of its two outputs.
        audio.Program.DelayLengths.Count.ShouldBe(18);
    }
}
