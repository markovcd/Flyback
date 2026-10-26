using System.Text;
using Flyback.Core.Compile;

namespace Flyback.Engine.Compile;

/// <summary>
/// Lowers a compiled patch to the Metal Shading Language, for the picture on Apple's
/// GPUs without OpenGL in between. The helpers and the per-op lines are
/// <see cref="ShaderText"/>'s, which <see cref="GlslEmitter"/> emits too, so the two
/// backends draw the same patch.
/// </summary>
/// <remarks>
/// GLSL's uniforms are globals and Metal has none a function can see, so the patch is
/// a struct: the uniforms, textures and arrays are its members, the helpers its
/// methods, and the fragment function fills one in and calls <c>run</c>. The prelude
/// names Metal's types after GLSL's and supplies the builtins Metal lacks.
/// <para>
/// The library must be compiled with fast math off. The helpers rely on IEEE
/// arithmetic: a NaN compared with itself, and the two-float clock that fast math
/// would reassociate to nought.
/// </para>
/// <para>
/// The vertex function draws the quad upside down, so a texture holds the frame
/// bottom row first, as OpenGL's do: the history, the planes and a readback read the
/// same rows on either backend.
/// </para>
/// </remarks>
public static class MetalEmitter
{
    public const string PatchVertex = "patchVertex";
    public const string PatchFragment = "patchFragment";
    public const string BlitVertex = "blitVertex";
    public const string BlitFragment = "blitFragment";

    /// <summary>The pass that smooths a finished frame's edges, drawn with <see cref="BlitVertex"/> (ADR-0215).</summary>
    public const string SmoothingFragment = "smoothingFragment";

    /// <summary>Where the smoothing pass's texel size is bound.</summary>
    public const int SmoothingBuffer = 0;

    /// <summary>Where <c>Frame</c> is bound: the clock, the aspect and the feedback's scales.</summary>
    public const int FrameBuffer = 0;

    /// <summary>Where the constants are bound, in <see cref="GlslEmitter.Constants"/>'s order.</summary>
    public const int ConstantsBuffer = 1;

    /// <summary>Where the live values are bound.</summary>
    public const int LiveBuffer = 2;

    /// <summary>Where each picture's aspect is bound, one float apiece in order.</summary>
    public const int PicturesBuffer = 3;

    /// <summary>Where each table's length and rate are bound, two floats apiece in order.</summary>
    public const int TablesBuffer = 4;

    /// <summary>Where the blit's two scales are bound.</summary>
    public const int BlitBuffer = 0;

    /// <summary>
    /// How many floats <c>Frame</c> holds, in this order: the clock's hi and lo, one,
    /// the aspect, the feedback's age, and its two scales.
    /// </summary>
    public const int FrameFloats = 7;

    public static MetalShaderSource Emit(CompiledPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);

        var constants = patch.Ops.Count(op => op.Code == OpCode.Const);
        var feedback = patch.Ops.Any(op => op.Code == OpCode.SampleFeedback);
        var pictures = ShaderText.Textures(patch);
        var targets = GlslEmitter.PlaneTargets(patch);
        var tables = ShaderText.Tables(patch);

        var text = new StringBuilder(Prelude);

        text.AppendLine();
        text.Append(Struct(patch, constants, feedback, pictures, targets, tables));
        text.AppendLine();
        text.Append(Entries(constants, patch.LiveCount, feedback, pictures, targets, tables));
        text.AppendLine();
        text.Append(Blit);

        return new MetalShaderSource(
            Library: text.ToString(),
            ConstantCount: constants,
            UsesFeedback: feedback,
            OpCount: patch.Ops.Length,
            LiveCount: patch.LiveCount,
            PictureCount: pictures,
            PlaneTargets: targets,
            TableCount: tables);
    }

    /// <summary>
    /// GLSL's words in Metal. <c>texture</c> filters linearly and clamps, which is
    /// what every texture it reads is set to on OpenGL; <c>mod</c> and the
    /// two-argument <c>atan</c> are GLSL's definitions.
    /// </summary>
    private const string Prelude =
        """
        #include <metal_stdlib>
        using namespace metal;

        typedef float2 vec2;
        typedef float3 vec3;
        typedef float4 vec4;
        typedef int2 ivec2;

        // The exact zero tests in the helpers are the point, not an oversight: they
        // trap the one divisor that makes a result undefined. See the same reasoning
        // spelled out over Divide in CompiledPatch.
        constant float BIG = 3.402823e38;
        constant float JUST_BELOW_ONE = 0.99999994;

        constexpr sampler linearClamp(filter::linear, address::clamp_to_edge);

        float mod(float a, float b) { return a - b * floor(a / b); }
        float atan(float y, float x) { return atan2(y, x); }
        vec4 texture(texture2d<float> t, vec2 uv) { return t.sample(linearClamp, uv); }
        vec4 texelFetch(texture2d<float> t, ivec2 at, int lod) { return t.read(uint2(at), uint(lod)); }

        struct Frame
        {
            float uTime;
            float uTimeLo;
            float uOne;
            float uAspect;
            float uFeedbackAge;
            float uFeedbackScaleX;
            float uFeedbackScaleY;
        };

        """;

    private static string Struct(CompiledPatch patch, int constants, bool feedback, int pictures, int targets, int tables)
    {
        var text = new StringBuilder();

        text.AppendLine("struct Patch");
        text.AppendLine("{");
        text.AppendLine("    float uTime;");
        text.AppendLine("    float uTimeLo;");
        text.AppendLine("    float uOne;");
        text.AppendLine("    float uAspect;");
        text.AppendLine("    float uFeedbackAge;");
        text.AppendLine("    float uFeedbackScaleX;");
        text.AppendLine("    float uFeedbackScaleY;");

        if (constants > 0) text.AppendLine("    constant float* uK;");
        if (patch.LiveCount > 0) text.AppendLine("    constant float* uLive;");
        if (feedback) text.AppendLine("    texture2d<float> uPrevious;");

        for (var i = 0; i < pictures; i++)
        {
            text.AppendLine($"    texture2d<float> uPicture{i};");
            text.AppendLine($"    float uPictureAspect{i};");
        }

        for (var i = 0; i < targets; i++)
            text.AppendLine($"    texture2d<float> uPlane{i};");

        for (var i = 0; i < tables; i++)
        {
            text.AppendLine($"    texture2d<float> uTable{i};");
            text.AppendLine($"    float uTableLength{i};");
            text.AppendLine($"    float uTableRate{i};");
        }

        text.AppendLine();
        text.Append(ShaderText.Helpers);

        if (targets > 0)
        {
            text.AppendLine();
            text.Append(ShaderText.Plane);
        }

        if (feedback)
        {
            text.AppendLine();
            text.Append(ShaderText.Feedback);
        }

        for (var i = 0; i < pictures; i++)
        {
            text.AppendLine();
            text.Append(ShaderText.Picture(i));
        }

        for (var i = 0; i < tables; i++)
        {
            text.AppendLine();
            text.Append(ShaderText.Table(i));
        }

        text.AppendLine();
        text.Append("void run(vec2 vUv, vec4 gl_FragCoord, thread vec4& fragColor");

        for (var i = 0; i < targets; i++)
            text.Append($", thread vec4& outPlane{i}");

        text.AppendLine(")");
        text.AppendLine("{");
        ShaderText.Main(patch, text);
        text.AppendLine("}");
        text.AppendLine("};");

        return text.ToString();
    }

    /// <summary>
    /// The patch's two functions. Textures are numbered as on OpenGL: the previous
    /// frame at 0, then the pictures, the planes and the tables.
    /// </summary>
    private static string Entries(int constants, int live, bool feedback, int pictures, int targets, int tables)
    {
        var text = new StringBuilder(
            """
            struct PatchVaryings
            {
                float4 position [[position]];
                float2 vUv;
            };

            vertex PatchVaryings patchVertex(uint id [[vertex_id]])
            {
                float2 p = float2(float(id & 1), float((id >> 1) & 1));

                PatchVaryings out;
                out.vUv = p;
                out.position = float4(p.x * 2.0 - 1.0, 1.0 - p.y * 2.0, 0.0, 1.0);
                return out;
            }

            struct PatchTargets
            {
                float4 fragColor [[color(0)]];

            """);

        for (var i = 0; i < targets; i++)
            text.AppendLine($"    float4 outPlane{i} [[color({i + 1})]];");

        text.AppendLine("};");
        text.AppendLine();
        text.AppendLine("fragment PatchTargets patchFragment(");
        text.Append($"    PatchVaryings in [[stage_in]],\n    constant Frame& frame [[buffer({FrameBuffer})]]");

        if (constants > 0) text.Append($",\n    constant float* uK [[buffer({ConstantsBuffer})]]");
        if (live > 0) text.Append($",\n    constant float* uLive [[buffer({LiveBuffer})]]");
        if (pictures > 0) text.Append($",\n    constant float* aspects [[buffer({PicturesBuffer})]]");
        if (tables > 0) text.Append($",\n    constant float* shapes [[buffer({TablesBuffer})]]");
        if (feedback) text.Append(",\n    texture2d<float> uPrevious [[texture(0)]]");

        for (var i = 0; i < pictures; i++)
            text.Append($",\n    texture2d<float> uPicture{i} [[texture({1 + i})]]");

        for (var i = 0; i < targets; i++)
            text.Append($",\n    texture2d<float> uPlane{i} [[texture({1 + pictures + i})]]");

        for (var i = 0; i < tables; i++)
            text.Append($",\n    texture2d<float> uTable{i} [[texture({1 + pictures + targets + i})]]");

        text.AppendLine(")");
        text.AppendLine("{");
        text.AppendLine("    Patch p;");
        text.AppendLine("    p.uTime = frame.uTime;");
        text.AppendLine("    p.uTimeLo = frame.uTimeLo;");
        text.AppendLine("    p.uOne = frame.uOne;");
        text.AppendLine("    p.uAspect = frame.uAspect;");
        text.AppendLine("    p.uFeedbackAge = frame.uFeedbackAge;");
        text.AppendLine("    p.uFeedbackScaleX = frame.uFeedbackScaleX;");
        text.AppendLine("    p.uFeedbackScaleY = frame.uFeedbackScaleY;");

        if (constants > 0) text.AppendLine("    p.uK = uK;");
        if (live > 0) text.AppendLine("    p.uLive = uLive;");
        if (feedback) text.AppendLine("    p.uPrevious = uPrevious;");

        for (var i = 0; i < pictures; i++)
        {
            text.AppendLine($"    p.uPicture{i} = uPicture{i};");
            text.AppendLine($"    p.uPictureAspect{i} = aspects[{i}];");
        }

        for (var i = 0; i < targets; i++)
            text.AppendLine($"    p.uPlane{i} = uPlane{i};");

        for (var i = 0; i < tables; i++)
        {
            text.AppendLine($"    p.uTable{i} = uTable{i};");
            text.AppendLine($"    p.uTableLength{i} = shapes[{2 * i}];");
            text.AppendLine($"    p.uTableRate{i} = shapes[{2 * i + 1}];");
        }

        text.AppendLine();
        text.AppendLine("    PatchTargets o;");
        text.Append("    p.run(in.vUv, in.position, o.fragColor");

        for (var i = 0; i < targets; i++)
            text.Append($", o.outPlane{i}");

        text.AppendLine(");");
        text.AppendLine("    return o;");
        text.AppendLine("}");

        return text.ToString();
    }

    /// <summary>
    /// The finished frame drawn into the view, scaled to the largest rectangle of the
    /// picture's aspect that fits, as GLSL's blit draws it. Not flipped: the frame is
    /// stored bottom row first and a drawable top row first, which is the turn the
    /// patch's vertex function took.
    /// </summary>
    private const string Blit =
        """
        struct BlitVaryings
        {
            float4 position [[position]];
            float2 vUv;
        };

        vertex BlitVaryings blitVertex(uint id [[vertex_id]], constant float2& scale [[buffer(0)]])
        {
            float2 p = float2(float(id & 1), float((id >> 1) & 1));

            BlitVaryings out;
            out.vUv = p;
            out.position = float4((p * 2.0 - 1.0) * scale, 0.0, 1.0);
            return out;
        }

        fragment float4 blitFragment(BlitVaryings in [[stage_in]], texture2d<float> frame [[texture(0)]])
        {
            return float4(texture(frame, in.vUv).rgb, 1.0);
        }

        static float3 smoothingAt(texture2d<float> frame, float2 uv)
        {
            constexpr sampler bilinear(filter::linear, address::clamp_to_edge);
            return frame.sample(bilinear, uv).rgb;
        }

        // GLSL's smoothing pass, step for step: rows run upward here too.
        fragment float4 smoothingFragment(
            BlitVaryings in [[stage_in]],
            texture2d<float> frame [[texture(0)]],
            constant float2& texel [[buffer(0)]])
        {
            const float3 luma = float3(0.299, 0.587, 0.114);
            float2 uv = in.vUv;

            float3 middle = smoothingAt(frame, uv);
            float nw = dot(smoothingAt(frame, uv + float2(-1.0, 1.0) * texel), luma);
            float ne = dot(smoothingAt(frame, uv + float2(1.0, 1.0) * texel), luma);
            float sw = dot(smoothingAt(frame, uv + float2(-1.0, -1.0) * texel), luma);
            float se = dot(smoothingAt(frame, uv + float2(1.0, -1.0) * texel), luma);
            float m = dot(middle, luma);

            float least = min(m, min(min(nw, ne), min(sw, se)));
            float most = max(m, max(max(nw, ne), max(sw, se)));

            // Most of a picture is no edge at all, and is left as it was drawn.
            if (most - least < max(1.0 / 32.0, most * (1.0 / 8.0))) return float4(middle, 1.0);

            float2 along = float2(-((nw + ne) - (sw + se)), (ne + se) - (nw + sw));
            float reduce = max((nw + ne + sw + se) * 0.25 * (1.0 / 8.0), 1.0 / 128.0);
            along = clamp(along / (min(abs(along.x), abs(along.y)) + reduce), float2(-8.0), float2(8.0));

            float3 inner = 0.5 * (smoothingAt(frame, uv + along * (1.0 / 3.0 - 0.5) * texel)
                + smoothingAt(frame, uv + along * (2.0 / 3.0 - 0.5) * texel));
            float3 outer = inner * 0.5 + 0.25 * (smoothingAt(frame, uv - along * 0.5 * texel)
                + smoothingAt(frame, uv + along * 0.5 * texel));
            float lumaOuter = dot(outer, luma);

            return float4(lumaOuter < least || lumaOuter > most ? inner : outer, 1.0);
        }

        """;
}
