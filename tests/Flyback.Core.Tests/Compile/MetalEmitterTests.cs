using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Compile;

/// <summary>
/// The Metal library a patch lowers to. Whether it compiles is a question for a Mac
/// (<c>MetalCompileTests</c>); what is asked here is that it runs the lines the GLSL
/// does and binds what the renderer binds.
/// </summary>
public class MetalEmitterTests
{
    public static TheoryData<OpCode> AllOpCodes => [.. Enum.GetValues<OpCode>()];

    public static TheoryData<string> AllPresets => [.. Presets.All.Select(p => p.Name)];

    /// <summary>The per-op lines are written once, so a preset draws the same on either backend.</summary>
    [Theory]
    [MemberData(nameof(AllPresets))]
    public void A_preset_runs_the_lines_its_GLSL_runs(string name)
    {
        var program = Presets.All.Single(p => p.Name == name).Build(NodeCatalog.BuiltIn).CompileForVideo(NodeCatalog.BuiltIn).Program;

        var glsl = GlslEmitter.Emit(program, GlslDialect.GlslEs300).PatchFragment;
        var main = glsl[(glsl.IndexOf("void main()\n{\n", StringComparison.Ordinal) + "void main()\n{\n".Length)..glsl.LastIndexOf('}')];

        MetalEmitter.Emit(program).Library.ShouldContain(main);
    }

    [Theory]
    [MemberData(nameof(AllOpCodes))]
    public void Every_opcode_lowers_to_a_line(OpCode code)
    {
        var library = MetalEmitter.Emit(OneOp(code)).Library;

        if (code is OpCode.UnitWrite or OpCode.ClockWrite or OpCode.Tap or OpCode.PlaneWrite)
            library.ShouldNotContain("r3 =");
        else
            library.ShouldContain("float r3 = ");
    }

    /// <summary>The previous frame at 0, then the pictures, the planes and the tables, as OpenGL's units are.</summary>
    [Fact]
    public void Textures_are_numbered_as_OpenGL_numbers_its_units()
    {
        var patch = new CompiledPatch(
            [
                new Op(OpCode.Const, 0, k: 0.25f),
                new Op(OpCode.SampleFeedback, 1, 0, 0),
                new Op(OpCode.SamplePicture, 4, 0, 0, k: 1f),
                new Op(OpCode.PlaneRead, 7, k: 4f),
                new Op(OpCode.PlaneWrite, -1, 7, k: 4f),
                new Op(OpCode.Table, 8, 0, k: 0f),
            ],
            9,
            1);

        var source = MetalEmitter.Emit(patch);

        source.PictureCount.ShouldBe(2);
        source.PlaneTargets.ShouldBe(2);
        source.TableCount.ShouldBe(1);
        source.UsesFeedback.ShouldBeTrue();

        source.Library.ShouldContain("texture2d<float> uPrevious [[texture(0)]]");
        source.Library.ShouldContain("texture2d<float> uPicture1 [[texture(2)]]");
        source.Library.ShouldContain("texture2d<float> uPlane1 [[texture(4)]]");
        source.Library.ShouldContain("texture2d<float> uTable0 [[texture(5)]]");
        source.Library.ShouldContain("float4 outPlane1 [[color(2)]];");
    }

    /// <summary>A zero-length buffer is no binding at all, so a patch with nothing to put there declares none.</summary>
    [Fact]
    public void A_patch_with_no_constants_or_live_values_binds_neither()
    {
        var library = MetalEmitter.Emit(new CompiledPatch([new Op(OpCode.LoadX, 0)], 1, 0)).Library;

        library.ShouldNotContain("uK");
        library.ShouldNotContain("uLive");
    }

    private static CompiledPatch OneOp(OpCode code) =>
        new(
            [
                new Op(OpCode.Const, 0, k: 0.25f),
                new Op(OpCode.Const, 1, k: 0.5f),
                new Op(OpCode.Const, 2, k: 0.75f),
                new Op(code, 3, 0, 1, 2, 1f),
            ],
            6,
            3);
}
