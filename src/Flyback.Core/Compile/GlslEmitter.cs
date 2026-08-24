using System.Text;

namespace Flyback.Core.Compile;

/// <summary>
/// Which GLSL the context speaks. The two differ only in their opening lines —
/// everything below the header is one shared body — but a shader written for one
/// will not compile on the other, and which one you get is decided by the
/// windowing platform rather than by anything this project chooses.
/// </summary>
public enum GlslDialect
{
    /// <summary>GLSL ES 3.00, which is what ANGLE on Windows and GLES on Linux give you.</summary>
    GlslEs300,

    /// <summary>GLSL 1.50, which is what a desktop GL 3.2 core context gives you.</summary>
    Glsl150,
}

/// <summary>The four shaders a frame needs, plus what the caller must upload to them.</summary>
/// <param name="ConstantCount">
/// Length of the <c>uK</c> array. Every <see cref="OpCode.Const"/> is a uniform
/// rather than a literal — see <see cref="GlslEmitter"/> for why that matters
/// more than it looks like it should.
/// </param>
/// <param name="UsesFeedback">
/// Whether the fragment shader reads <c>uPrevious</c>. A patch without a Feedback
/// module needs no texture bound and no previous frame kept.
/// </param>
public sealed record ShaderSource(
    string PatchVertex,
    string PatchFragment,
    string BlitVertex,
    string BlitFragment,
    int ConstantCount,
    bool UsesFeedback,
    int OpCount);

/// <summary>
/// Lowers a compiled patch to a fragment shader. This is the second backend
/// ADR-0003 left room for: it consumes the same <see cref="Op"/> array the
/// interpreter walks, and emits one line of GLSL per op instead of executing it.
/// Everything upstream — the graph walk, dead-code elimination, coercion,
/// constant sharing — has already happened by the time a program arrives here.
/// </summary>
/// <remarks>
/// <para>
/// Registers become individual <c>float rN</c> declarations rather than an
/// indexed array. ADR-0007 already guarantees every register is written exactly
/// once and always before it is read, so the program is in SSA form and this is
/// simply legal; an array would additionally force dynamically-indexable
/// storage, which some drivers put in scratch memory rather than registers.
/// </para>
/// <para>
/// <see cref="CompiledPatch.Evaluate"/> is the specification. Where GLSL has a
/// builtin of the same name that does not agree with it — <c>fract</c>,
/// <c>mod</c>, <c>mix</c>, <c>pow</c>, <c>smoothstep</c>, <c>atan</c> — the
/// builtin is deliberately not used, and a helper transcribing the interpreter's
/// behaviour is emitted instead. Those disagreements are at edges rather than in
/// the middle, so the wrong choice would look right until a patch found one.
/// </para>
/// <para>
/// This class produces text and nothing else: no GL, no platform, no state. It
/// lives in Core so that what the GPU is asked to run is covered by Core's tests
/// (ADR-0019 is untouched — a string builder is not a dependency).
/// </para>
/// </remarks>
public static class GlslEmitter
{
    /// <summary>
    /// The values behind <c>uK</c>, in the order the shader indexes them.
    /// </summary>
    /// <remarks>
    /// This ordering is a contract between the emitted text and whoever uploads
    /// to it, so both ends of it are written here rather than in the renderer.
    /// </remarks>
    public static float[] Constants(CompiledPatch patch) =>
        [.. patch.Ops.Where(op => op.Code == OpCode.Const).Select(op => op.K)];

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
            OpCount: patch.Ops.Length);
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
    /// <c>vUv</c> lands on <c>(index + 0.5) / size</c>, which is exactly the
    /// half-pixel offset SynthRenderer applies by hand and ADR-0014 requires — so
    /// the coordinate convention comes out of the rasteriser for free rather than
    /// being a second place it could be got wrong.
    /// </summary>
    /// <remarks>
    /// Not the usual oversized triangle. That trick relies on everything past the
    /// edges being clipped away by the viewport, and the blit below deliberately
    /// draws smaller than its viewport — so the overshoot would be *visible*, as a
    /// smear of the top row across the letterbox and a wedge of untouched black
    /// where the hypotenuse cut the corner. A strip has no overshoot to leak.
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
    /// equivalent of PreviewSurface.Letterbox, which centres for free because the
    /// scale is applied about the origin in clip space.
    /// </summary>
    /// <remarks>
    /// No flip. The offscreen texture is stored bottom row first because that is
    /// what rendering into it does, and the target is a framebuffer with the same
    /// convention, so the two agree and <c>vUv</c> passes straight through.
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
    /// Every guard in <see cref="CompiledPatch"/>, transcribed once as a function
    /// rather than repeated at each of the several hundred sites that need it.
    /// ADR-0013 chose total arithmetic over propagating NaN, and a backend that
    /// quietly dropped that choice would diverge from the picture on the CPU in
    /// exactly the places a patch is most likely to wander into.
    /// </summary>
    private const string Helpers =
        """
        // The exact zero tests below are the point, not an oversight: they trap the
        // one divisor that makes a result undefined. See the same reasoning spelled
        // out over Divide in CompiledPatch.
        const float BIG = 3.402823e38;
        const float JUST_BELOW_ONE = 0.99999994;

        bool  fin(float v)          { return v == v && abs(v) < BIG; }
        float gd (float v)          { return fin(v) ? v : 0.0; }
        float fr (float v)          { float f = v - floor(v); return f < 1.0 ? f : JUST_BELOW_ONE; }
        float dv (float a, float b) { return b == 0.0 ? 0.0 : gd(a / b); }
        float md (float a, float b) { return b == 0.0 ? 0.0 : gd(a - b * floor(a / b)); }
        float sq (float a)          { return a <= 0.0 ? 0.0 : sqrt(a); }
        float lg (float a)          { return a <= 0.0 ? 0.0 : log(a); }
        float sat(float v)          { return fin(v) ? clamp(v, 0.0, 1.0) : 0.0; }

        // GLSL leaves atan undefined at the origin, where Math.Atan2 answers zero.
        float at2(float y, float x) { return (x == 0.0 && y == 0.0) ? 0.0 : atan(y, x); }

        // GLSL leaves pow undefined for a negative base, where Math.Pow(-2, 3) is -8.
        // The cases that are NaN or infinite on the CPU are the ones Guard turns to
        // zero, so they are answered directly here.
        float pw(float a, float b)
        {
            if (a > 0.0)       return gd(pow(a, b));
            if (a == 0.0)      return b == 0.0 ? 1.0 : 0.0;
            if (b != floor(b)) return 0.0;

            float m = gd(pow(-a, b));
            return mod(abs(b), 2.0) == 1.0 ? -m : m;
        }

        // GLSL's smoothstep divides by zero when the edges meet; the interpreter
        // answers a step there.
        float sm(float e0, float e1, float x)
        {
            if (e0 == e1) return x < e0 ? 0.0 : 1.0;

            float t = clamp((x - e0) / (e1 - e0), 0.0, 1.0);
            return t * t * (3.0 - 2.0 * t);
        }

        // Noise, transcribed from Noise.cs. Converting a negative int to uint keeps
        // the bit pattern in both languages, so the hash agrees exactly, which is
        // what stops a noisy patch looking like a different patch on the GPU.
        float hsh(int x, int y, int z)
        {
            uint h = uint(x) * 374761393u + uint(y) * 668265263u + uint(z) * 1274126177u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return float(h & 0xFFFFFFu) * (1.0 / 16777215.0);
        }

        float fade(float t) { return t * t * (3.0 - 2.0 * t); }
        float lrp (float a, float b, float t) { return a + (b - a) * t; }

        float nz(float x, float y, float z)
        {
            if (!(fin(x) && fin(y) && fin(z))) return 0.0;

            int xi = int(floor(x)), yi = int(floor(y)), zi = int(floor(z));
            float u = fade(x - float(xi)), v = fade(y - float(yi)), w = fade(z - float(zi));

            float z0 = lrp(lrp(hsh(xi, yi,     zi), hsh(xi + 1, yi,     zi), u),
                           lrp(hsh(xi, yi + 1, zi), hsh(xi + 1, yi + 1, zi), u), v);
            float z1 = lrp(lrp(hsh(xi, yi,     zi + 1), hsh(xi + 1, yi,     zi + 1), u),
                           lrp(hsh(xi, yi + 1, zi + 1), hsh(xi + 1, yi + 1, zi + 1), u), v);

            return lrp(z0, z1, w);
        }

        vec3 hsv(float h, float s, float v)
        {
            h = fr(h) * 6.0;
            s = clamp(s, 0.0, 1.0);

            int sector = int(h);
            float f = h - float(sector);
            float p = v * (1.0 - s);
            float q = v * (1.0 - s * f);
            float w = v * (1.0 - s * (1.0 - f));

            if (sector == 0) return vec3(v, w, p);
            if (sector == 1) return vec3(q, v, p);
            if (sector == 2) return vec3(p, v, w);
            if (sector == 3) return vec3(p, q, v);
            if (sector == 4) return vec3(w, p, v);
            return vec3(v, p, q);
        }

        """;

    /// <summary>
    /// The previous frame, read the way CompiledPatch.Sample reads it. The scales
    /// carry the whole of that mapping — patch coordinates to texel centres, and
    /// the y flip between a picture indexed downwards and a texture stored
    /// upwards — so the shader needs no width or height of its own. Bilinear
    /// filtering does the interpolation Sample does by hand, and clamping to the
    /// edge does what its clamp to width - 1.001 does.
    /// </summary>
    /// <remarks>
    /// The substitutions for a non-finite coordinate are the values that land on
    /// index zero, which is where Sample puts one.
    /// </remarks>
    private const string FeedbackHelper =
        """
        uniform sampler2D uPrevious;
        uniform float uFeedbackScaleX;
        uniform float uFeedbackScaleY;

        vec3 fb(float u, float v)
        {
            u = fin(u) ? u : -uAspect;
            v = fin(v) ? v :  1.0;

            return texture(uPrevious, vec2(u * uFeedbackScaleX + 0.5, v * uFeedbackScaleY + 0.5)).rgb;
        }

        """;

    private static string PatchFragment(CompiledPatch patch, GlslDialect dialect, int constants, bool feedback)
    {
        var text = new StringBuilder(Header(dialect, fragment: true));

        text.AppendLine("uniform float uTime;");
        text.AppendLine("uniform float uAspect;");

        // A zero-length array is not a legal declaration, and a patch of nothing
        // but loads genuinely has no constants in it.
        if (constants > 0) text.AppendLine($"uniform float uK[{constants}];");

        text.AppendLine();
        text.AppendLine("in vec2 vUv;");
        text.AppendLine("out vec4 fragColor;");
        text.AppendLine();
        text.Append(Helpers);

        if (feedback)
        {
            text.AppendLine();
            text.Append(FeedbackHelper);
        }

        text.AppendLine();
        text.AppendLine("void main()");
        text.AppendLine("{");

        // The same two lines SynthRenderer computes per pixel, against a vUv the
        // rasteriser has already centred.
        text.AppendLine("    float px = (vUv.x * 2.0 - 1.0) * uAspect;");
        text.AppendLine("    float py = vUv.y * 2.0 - 1.0;");
        text.AppendLine();

        var written = Body(patch, text);

        text.AppendLine();
        text.AppendLine(
            $"    fragColor = vec4({Output(patch, written, 0)}, " +
            $"{Output(patch, written, 1)}, {Output(patch, written, 2)}, 1.0);");
        text.AppendLine("}");

        return text.ToString();
    }

    /// <summary>
    /// Saturating here rather than at the end of the blit is deliberate: the
    /// offscreen target holds half floats and would happily store a value above
    /// one, and the clamp on the way into the frame history is what stops a
    /// feedback loop with any gain in it running away (ADR-0012).
    /// </summary>
    private static string Output(CompiledPatch patch, bool[] written, int channel)
    {
        var register = patch.OutputBase + channel;

        return channel < patch.OutputWidth && register < written.Length && written[register]
            ? $"sat(r{register})"
            : "0.0";
    }

    /// <summary>Emits one line per op, and reports which registers ended up declared.</summary>
    private static bool[] Body(CompiledPatch patch, StringBuilder text)
    {
        // A register read before anything wrote it reads as zero, which is what an
        // interpreter walking a freshly allocated register file would have found.
        var written = new bool[Math.Max(patch.RegisterCount, patch.OutputBase + patch.OutputWidth)];

        var constant = 0;

        foreach (var op in patch.Ops)
        {
            // The two ops with no result, and so the two ops with no line. There
            // is no state on this path for either to write to, and a declaration
            // of r-1 would not compile. What they fed is left as dead code for
            // the driver to drop, which is the same deal the other half of a sink
            // gets.
            if (op.Code is OpCode.UnitWrite or OpCode.ClockWrite) continue;

            string a = Read(op.A), b = Read(op.B), c = Read(op.C);

            var expression = op.Code switch
            {
                OpCode.Const => $"uK[{constant++}]",
                OpCode.LoadX => "px",
                OpCode.LoadY => "py",
                OpCode.LoadT => "uTime",
                OpCode.Copy => a,

                OpCode.Neg => $"-{a}",
                OpCode.Abs => $"abs({a})",
                OpCode.Sin => $"sin({a})",
                OpCode.Cos => $"cos({a})",
                OpCode.Tan => $"gd(tan({a}))",
                OpCode.Sqrt => $"sq({a})",
                OpCode.Floor => $"floor({a})",
                OpCode.Ceil => $"ceil({a})",
                OpCode.Fract => $"fr({a})",

                // Guarded on the way in, where the interpreter is not: Math.Sign
                // throws on a NaN rather than answering one, and a shader has
                // nowhere to throw to.
                OpCode.Sign => $"sign(gd({a}))",

                OpCode.Exp => $"gd(exp({a}))",
                OpCode.Log => $"lg({a})",

                OpCode.Add => $"{a} + {b}",
                OpCode.Sub => $"{a} - {b}",
                OpCode.Mul => $"{a} * {b}",
                OpCode.Div => $"dv({a}, {b})",
                OpCode.Mod => $"md({a}, {b})",
                OpCode.Pow => $"pw({a}, {b})",
                OpCode.Min => $"min({a}, {b})",
                OpCode.Max => $"max({a}, {b})",
                OpCode.Atan2 => $"at2({a}, {b})",

                // The one op that maps straight onto its builtin: GLSL's
                // step(edge, x) is x < edge ? 0 : 1, which is what the interpreter
                // does with A as the edge.
                OpCode.Step => $"step({a}, {b})",

                OpCode.Hypot => $"sqrt({a} * {a} + {b} * {b})",

                // The tolerance for an inverted range lives in the max, exactly as
                // it does in the interpreter.
                OpCode.Clamp => $"clamp({a}, {b}, max({b}, {c}))",

                // Not mix(), which drivers are free to contract into a fused
                // multiply-add and which then disagrees at the endpoints.
                OpCode.Mix => $"{a} + ({b} - {a}) * {c}",

                OpCode.Smoothstep => $"sm({a}, {b}, {c})",
                OpCode.Noise3 => $"nz({a}, {b}, {c})",

                // The stateful three, in the shape they take when there is no
                // state to carry. A picture is one evaluation per pixel in
                // whatever order the rasteriser likes, so this is the same branch
                // the interpreter takes on the video path — not a simplification.
                OpCode.Delay => a,
                OpCode.Allpass => a,
                OpCode.Phase => $"{a} * {b} + {c}",

                // A cycle with no cell behind it reads as nothing, which leaves the
                // loop open rather than closed — again what the interpreter does
                // when it is handed no state.
                OpCode.UnitRead => "0.0",

                OpCode.HsvToRgb => $"hsv({a}, {b}, {c})",
                OpCode.SampleFeedback => $"fb({a}, {b})",

                _ => throw new NotSupportedException(
                    $"{op.Code} has no GLSL lowering. Every opcode needs one, or the GPU " +
                    "backend renders a different patch from the one the interpreter does."),
            };

            if (op.Code is OpCode.HsvToRgb or OpCode.SampleFeedback)
            {
                text.AppendLine($"    vec3 t{op.Out} = {expression};");
                text.AppendLine(
                    $"    float r{op.Out} = t{op.Out}.x; " +
                    $"float r{op.Out + 1} = t{op.Out}.y; " +
                    $"float r{op.Out + 2} = t{op.Out}.z;");

                for (var i = 0; i < 3; i++)
                    written[op.Out + i] = true;
            }
            else
            {
                text.AppendLine($"    float r{op.Out} = {expression};");
                written[op.Out] = true;
            }
        }

        return written;

        // An operand of -1 means the op does not use that slot — Triple leaves C
        // unset for SampleFeedback — and reads as zero for the same reason.
        string Read(int register) =>
            register >= 0 && register < written.Length && written[register] ? $"r{register}" : "0.0";
    }
}
