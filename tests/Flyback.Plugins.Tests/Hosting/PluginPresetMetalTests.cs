using Flyback.Engine.Compile;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Hosting;

/// <summary>Every plugin's preset lowers to the Metal library the picture draws on Apple's machines.</summary>
/// <remarks>An opcode with no lowering throws here rather than drawing a black region on screen.</remarks>
public class PluginPresetMetalTests
{
    public static TheoryData<string> Names => [.. PluginPresetPrograms.Names.Select(row => row.Data)];

    [Theory]
    [MemberData(nameof(Names))]
    public void The_picture_lowers_to_Metal(string name) =>
        MetalEmitter.Emit(PluginPresetPrograms.Compiled(name, video: true)).Library.ShouldContain("fragment PatchTargets patchFragment(");
}
