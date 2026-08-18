using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Compile;

/// <summary>
/// The GPU backend renders a different picture from the interpreter the moment
/// one opcode is lowered wrongly, and a shader has no way to say so — it just
/// draws something. These tests are what stands in for that missing complaint.
/// </summary>
public class GlslEmitterTests
{
    public static TheoryData<OpCode> AllOpCodes => [.. Enum.GetValues<OpCode>()];

    public static TheoryData<GlslDialect> AllDialects => [.. Enum.GetValues<GlslDialect>()];

    public static TheoryData<string, GlslDialect> PresetsAndDialects =>
        [.. from preset in Presets.All from dialect in Enum.GetValues<GlslDialect>() select (preset.Name, dialect)];

    /// <summary>
    /// Ops that write no register, and so lower to no line. Named one by one
    /// rather than detected, so that adding one is a decision somebody made here
    /// rather than a silent hole in the theory below.
    /// </summary>
    private static bool WritesNothing(OpCode code) => code is OpCode.UnitWrite;

    /// <summary>
    /// The test that matters: it fails the day an opcode is added and the shader
    /// is not told about it, which is otherwise a black region on screen that
    /// nobody traces back to this file.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllOpCodes))]
    public void Every_opcode_lowers_to_a_line(OpCode code)
    {
        var source = GlslEmitter.Emit(OneOp(code), GlslDialect.GlslEs300);

        if (WritesNothing(code))
        {
            // Not merely allowed to have no line — required to. Its result is the
            // next evaluation's, there is no state on this path to keep one in,
            // and the register it would assign to does not exist: a program that
            // means it writes r-1, which is not a name GLSL has.
            source.PatchFragment.ShouldNotContain("r3");
            return;
        }

        // Registers 0..2 hold the operands, so the op under test writes r3 —
        // either directly or, for the ops that fill three registers, off a vec3.
        source.PatchFragment.ShouldContain("float r3 = ");
    }

    [Fact]
    public void An_unknown_opcode_is_refused_rather_than_skipped()
    {
        var patch = OneOp((OpCode)200);

        Should.Throw<NotSupportedException>(() => GlslEmitter.Emit(patch, GlslDialect.GlslEs300));
    }

    [Theory]
    [MemberData(nameof(AllDialects))]
    public void A_patch_without_constants_declares_no_constant_array(GlslDialect dialect)
    {
        // A zero-length uniform array will not compile, and a patch of nothing but
        // loads genuinely has none.
        var patch = new CompiledPatch([new Op(OpCode.LoadX, 0)], 1, 0, 1);

        GlslEmitter.Emit(patch, dialect).PatchFragment.ShouldNotContain("uK[0]");
    }

    [Fact]
    public void Constants_are_uploaded_in_the_order_the_shader_indexes_them()
    {
        var patch = new CompiledPatch(
            [new Op(OpCode.Const, 0, k: 7f), new Op(OpCode.LoadX, 1), new Op(OpCode.Const, 2, k: 9f)],
            3,
            0,
            1);

        GlslEmitter.Constants(patch).ShouldBe([7f, 9f]);
        GlslEmitter.Emit(patch, GlslDialect.GlslEs300).PatchFragment.ShouldContain("float r2 = uK[1];");
    }

    /// <summary>
    /// The lowering table read back as text. This is here so that a change to it
    /// has to be looked at and approved rather than merely compiling, and GLSL is
    /// cheap to review by eye in a way a rendered frame is not.
    /// </summary>
    [Theory]
    [MemberData(nameof(PresetsAndDialects))]
    public async Task Preset_lowers_as_approved(string presetName, GlslDialect dialect)
    {
        var patch = Presets.All.Single(p => p.Name == presetName).Build(NodeCatalog.BuiltIn);
        var program = patch.CompileForVideo(NodeCatalog.BuiltIn).Program;

        await Verify(GlslEmitter.Emit(program, dialect).PatchFragment, "glsl")
            .UseDirectory("snapshots")
            .UseParameters(presetName, dialect);
    }

    /// <summary>The three shaders that do not depend on the patch.</summary>
    [Theory]
    [MemberData(nameof(AllDialects))]
    public async Task Fixed_shaders_are_as_approved(GlslDialect dialect)
    {
        var source = GlslEmitter.Emit(CompiledPatch.Black, dialect);

        await Verify(
                string.Join(
                    "\n// ----------------------------------------------------------------\n",
                    source.PatchVertex,
                    source.BlitVertex,
                    source.BlitFragment),
                "glsl")
            .UseDirectory("snapshots")
            .UseParameters(dialect);
    }

    /// <summary>
    /// A program of three constants and one op over them, so every opcode has
    /// operands to read and somewhere to write regardless of how many it uses.
    /// </summary>
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
