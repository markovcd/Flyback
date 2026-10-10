using Flyback.Engine.Compile;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Hosting;

/// <summary>Every plugin's preset lowers to the shader the GPU draws, in every dialect.</summary>
/// <remarks>An opcode with no lowering throws here rather than drawing a black region on screen.</remarks>
public class PluginPresetGlslTests
{
    public static TheoryData<string, GlslDialect> NamesAndDialects =>
        [.. from name in PluginPresetPrograms.Names.Select(row => row.Data) from dialect in Enum.GetValues<GlslDialect>() select (name, dialect)];

    [Theory]
    [MemberData(nameof(NamesAndDialects))]
    public void The_picture_lowers_to_GLSL(string name, GlslDialect dialect) =>
        GlslEmitter.Emit(PluginPresetPrograms.Compiled(name, video: true), dialect).PatchFragment.ShouldNotBeNullOrEmpty();
}
