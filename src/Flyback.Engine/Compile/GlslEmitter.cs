using System.Text;
using Flyback.Core.Compile;

namespace Flyback.Engine.Compile;

/// <summary>
/// Lowers a compiled patch to a fragment shader — the second backend ADR-0003
/// left room for. It consumes the same <see cref="Op"/> array the interpreter
/// walks and emits one line of GLSL per op, so the graph walk, dead-code
/// elimination, coercion and constant sharing have all already happened.
/// </summary>
/// <remarks>
/// Registers become individual <c>float rN</c> declarations rather than an
/// indexed array: ADR-0007 already guarantees SSA, and an array would force
/// dynamically-indexable storage, which some drivers put in scratch memory.
/// <para>
/// <see cref="CompiledPatch.Evaluate"/> is the specification. Where GLSL has a
/// builtin of the same name that does not agree with it — <c>fract</c>,
/// <c>mod</c>, <c>mix</c>, <c>pow</c>, <c>smoothstep</c>, <c>atan</c> — a helper
/// transcribing the interpreter is emitted instead, because those disagreements
/// are at edges and the wrong choice would look right until a patch found one.
/// </para>
/// <para>
/// Text and nothing else: no GL, no platform, no state. It lives in Core so what
/// the GPU runs is covered by Core's tests.
/// </para>
/// </remarks>
public static class GlslEmitter
{
    /// <summary>
    /// The values behind <c>uK</c>, in the order the shader indexes them. This
    /// ordering is a contract between the emitted text and whoever uploads to it,
    /// so both ends of it are written here.
    /// </summary>
    public static float[] Constants(CompiledPatch patch) =>
        [.. patch.Ops.Where(op => op.Code == OpCode.Const).Select(op => op.K)];

    /// <summary>
    /// How many render targets the planes of <paramref name="patch"/> need: four
    /// to a target, since each is one channel of an RGBA surface.
    /// </summary>
    public static int PlaneTargets(CompiledPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);

        return (patch.PlaneCount + 3) / 4;
    }

    public static ShaderSource Emit(CompiledPatch patch, GlslDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(patch);

        var constants = patch.Ops.Count(op => op.Code == OpCode.Const);
        var feedback = patch.Ops.Any(op => op.Code == OpCode.SampleFeedback);

        return new ShaderSource(
            PatchVertex: Header(dialect, fragment: false) + PatchVertexBody,
            PatchFragment: PatchFragment(patch, dialect, constants, feedback),
            BlitVertex: Header(dialect, fragment: false) + BlitVertexBody,
            BlitFragment: Header(dialect, fragment: true) + BlitFragmentBody,
            ConstantCount: constants,
            UsesFeedback: feedback,
            OpCount: patch.Ops.Length,
            LiveCount: patch.LiveCount,
            PictureCount: ShaderText.Textures(patch),
            PlaneTargets: PlaneTargets(patch),
            TableCount: ShaderText.Tables(patch));
    }

    // --- headers -----------------------------------------------------------------

    /// <summary>
    /// The only text that differs between dialects. The precision statements are
    /// ES-only and none of them is optional: the default precision for an integer
    /// in an ES fragment shader is 16 bits, which the noise hash needs 32 of, and
    /// the default for a sampler is <c>lowp</c>, which would undo the whole point
    /// of keeping the feedback history in half floats.
    /// </summary>
    private static string Header(GlslDialect dialect, bool fragment) => dialect switch
    {
        GlslDialect.GlslEs300 when fragment =>
            """
            #version 300 es
            precision highp float;
            precision highp int;
            precision highp sampler2D;


            """,
        GlslDialect.GlslEs300 => "#version 300 es\n\n",
        GlslDialect.Glsl150 => "#version 150\n\n",
        _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "Unknown GLSL dialect."),
    };

    // --- the fixed shaders -------------------------------------------------------

    /// <summary>
    /// The unit square as a four-vertex strip, with no vertex buffer and no
    /// attributes — the corners come from <c>gl_VertexID</c>. The interpolated
    /// <c>vUv</c> lands on <c>(index + 0.5) / size</c>, which is the half-pixel
    /// offset ADR-0014 requires, so the convention comes out of the rasteriser
    /// rather than being a second place to get it wrong.
    /// </summary>
    /// <remarks>
    /// Not the usual oversized triangle: that relies on the viewport clipping the
    /// overshoot, and the blit below draws smaller than its viewport, so the
    /// overshoot would be visible.
    /// </remarks>
    private const string Corners =
        """
            vec2 p = vec2(float(gl_VertexID & 1), float((gl_VertexID >> 1) & 1));
        """;

    private const string PatchVertexBody =
        $$"""
        out vec2 vUv;

        void main()
        {
        {{Corners}}
            vUv = p;
            gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
        }

        """;

    /// <summary>
    /// Draws the finished frame into whatever the compositor handed us, scaled to
    /// the largest rectangle of the picture's aspect that fits — the GPU-side
    /// PreviewSurface.Letterbox, centered for free because the scale is applied
    /// about the origin in clip space.
    /// </summary>
    /// <remarks>
    /// No flip: the offscreen texture is stored bottom row first because that is
    /// what rendering into it does, and the target has the same convention.
    /// </remarks>
    private const string BlitVertexBody =
        $$"""
        uniform float uScaleX;
        uniform float uScaleY;

        out vec2 vUv;

        void main()
        {
        {{Corners}}
            vUv = p;
            gl_Position = vec4((p * 2.0 - 1.0) * vec2(uScaleX, uScaleY), 0.0, 1.0);
        }

        """;

    private const string BlitFragmentBody =
        """
        uniform sampler2D uTexture;

        in vec2 vUv;
        out vec4 fragColor;

        void main()
        {
            fragColor = vec4(texture(uTexture, vUv).rgb, 1.0);
        }

        """;

    // --- the patch fragment shader -----------------------------------------------

    /// <summary>
    /// The constants <see cref="ShaderText.Helpers"/> leans on, which GLSL declares
    /// at file scope.
    /// </summary>
    private const string ShaderConstants =
        """
        // The exact zero tests below are the point, not an oversight: they trap the
        // one divisor that makes a result undefined. See the same reasoning spelled
        // out over Divide in CompiledPatch.
        const float BIG = 3.402823e38;
        const float JUST_BELOW_ONE = 0.99999994;


        """;

    /// <summary>The previous frame's sampler and the scales <see cref="ShaderText.Feedback"/> reads it by.</summary>
    private const string FeedbackUniforms =
        """
        uniform sampler2D uPrevious;
        uniform float uFeedbackScaleX;
        uniform float uFeedbackScaleY;


        """;

    /// <summary>A picture's sampler and the aspect <see cref="ShaderText.Picture"/> reads it at.</summary>
    private static string PictureUniforms(int index) =>
        $$"""
        uniform sampler2D uPicture{{index}};
        uniform float uPictureAspect{{index}};


        """;

    /// <summary>A table's sampler, its length and its rate, for <see cref="ShaderText.Table"/>.</summary>
    private static string TableUniforms(int index) =>
        $$"""
        uniform sampler2D uTable{{index}};
        uniform float uTableLength{{index}};
        uniform float uTableRate{{index}};


        """;

    /// <summary>How many floats a row of a table's texture holds; a longer table wraps onto more rows.</summary>
    public const int TableRow = 4096;

    private static string PatchFragment(CompiledPatch patch, GlslDialect dialect, int constants, bool feedback)
    {
        var text = new StringBuilder(Header(dialect, fragment: true));

        // The clock as hi + lo: a float alone steps by two seconds a year in.
        text.AppendLine("uniform float uTime;");
        text.AppendLine("uniform float uTimeLo;");
        text.AppendLine("uniform float uOne;");
        text.AppendLine("uniform float uAspect;");

        // Only where it is read, as with the live inputs below.
        if (patch.Ops.Any(op => op.Code == OpCode.LoadFeedbackAge)) text.AppendLine("uniform float uFeedbackAge;");

        // A zero-length array is not a legal declaration, and a patch of nothing
        // but loads genuinely has no constants in it.
        if (constants > 0) text.AppendLine($"uniform float uK[{constants}];");

        // The same rule, for the same reason. A patch nobody is playing declares
        // no array and reads none.
        if (patch.LiveCount > 0) text.AppendLine($"uniform float uLive[{patch.LiveCount}];");

        var targets = PlaneTargets(patch);

        // One sampler per target, holding what these pixels left there when the
        // frame before was drawn. Declared rather than arrayed, as the pictures
        // are and for the same reason.
        for (var target = 0; target < targets; target++)
            text.AppendLine($"uniform sampler2D uPlane{target};");

        text.AppendLine();
        text.AppendLine("in vec2 vUv;");

        // A location on every output or none: a shader that writes more than the
        // picture has to say which attachment each goes to, and ES has no
        // glBindFragDataLocation to say it from the outside — see
        // GpuFrameRenderer, which binds the same numbers where the dialect will
        // not carry them.
        if (targets == 0)
        {
            text.AppendLine("out vec4 fragColor;");
        }
        else
        {
            var located = dialect is GlslDialect.GlslEs300;

            text.AppendLine(located ? "layout(location = 0) out vec4 fragColor;" : "out vec4 fragColor;");

            for (var target = 0; target < targets; target++)
                text.AppendLine(located
                    ? $"layout(location = {target + 1}) out vec4 outPlane{target};"
                    : $"out vec4 outPlane{target};");
        }

        text.AppendLine();
        text.Append(ShaderConstants);
        text.Append(ShaderText.Helpers);

        if (targets > 0)
        {
            text.AppendLine();
            text.Append(ShaderText.Plane);
        }

        if (feedback)
        {
            text.AppendLine();
            text.Append(FeedbackUniforms);
            text.Append(ShaderText.Feedback);
        }

        // One sampler and one shape per picture, declared rather than arrayed:
        // an array of samplers may only be indexed by a constant in ES, and the
        // index here is one anyway — every op names its picture at compile time.
        for (var picture = 0; picture < ShaderText.Textures(patch); picture++)
        {
            text.AppendLine();
            text.Append(PictureUniforms(picture));
            text.Append(ShaderText.Picture(picture));
        }

        for (var table = 0; table < ShaderText.Tables(patch); table++)
        {
            text.AppendLine();
            text.Append(TableUniforms(table));
            text.Append(ShaderText.Table(table));
        }

        text.AppendLine();
        text.AppendLine("void main()");
        text.AppendLine("{");

        ShaderText.Main(patch, text);

        text.AppendLine("}");

        return text.ToString();
    }
}
