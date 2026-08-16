using System.Runtime.CompilerServices;

namespace Flyback.Core.Compile;

/// <summary>
/// The previous frame, exposed to <see cref="OpCode.SampleFeedback"/>. Stored
/// as linear float RGB so repeated feedback passes don't quantise to 8 bits.
/// </summary>
public readonly struct FeedbackFrame(float[]? pixels, int width, int height)
{
    public readonly float[]? Pixels = pixels;
    public readonly int Width = width;
    public readonly int Height = height;

    public float Aspect => Height == 0 ? 1f : (float)Width / Height;
}

/// <summary>
/// A patch lowered to a straight-line program. Evaluating it for a pixel means
/// walking <see cref="Ops"/> once, so there is no graph traversal, no virtual
/// dispatch and no allocation in the inner loop.
/// </summary>
public sealed class CompiledPatch(Op[] ops, int registerCount, int outputBase, int outputWidth = 3)
{
    public Op[] Ops { get; } = ops;

    public int RegisterCount { get; } = registerCount;

    /// <summary>First of the <see cref="OutputWidth"/> registers holding the result.</summary>
    public int OutputBase { get; } = outputBase;

    /// <summary>3 for a video sink's RGB, 2 for an audio sink's stereo pair.</summary>
    public int OutputWidth { get; } = outputWidth;

    /// <summary>
    /// The longest delay each stateful op will ask for, in the order those ops
    /// run. A renderer sizes one ring buffer per entry; a program with none — the
    /// usual case — needs no state at all.
    /// </summary>
    public IReadOnlyList<float> DelayLengths { get; } =
        [.. ops.Where(o => o.Code is OpCode.Delay or OpCode.Allpass).Select(o => o.K)];

    /// <summary>
    /// How many phase accumulators the program runs. One cell each, so unlike a
    /// delay line there is nothing to size — but a program with any of these
    /// still needs state, and a renderer that gave it none would hand every
    /// oscillator back its multiply.
    /// </summary>
    public int PhaseCount { get; } = ops.Count(o => o.Code is OpCode.Phase);

    /// <summary>A program whose output is all zeroes — the fallback when a sink is missing.</summary>
    public static CompiledPatch Constant(int width) => new(
        [.. Enumerable.Range(0, width).Select(i => new Op(OpCode.Const, i))],
        width,
        0,
        width);

    /// <summary>Renders nothing, used when there is no Video Output node.</summary>
    public static CompiledPatch Black { get; } = Constant(3);

    /// <summary>Plays nothing, used when there is no Audio Output node.</summary>
    public static CompiledPatch Silent { get; } = Constant(2);

    public float[] AllocateRegisters() => new float[Math.Max(RegisterCount, OutputWidth)];

    /// <summary>Runs the program for one pixel. <paramref name="registers"/> is reused across pixels.</summary>
    /// <param name="delays">
    /// Memory for the stateful ops, or null when there is none. Null is not a
    /// failure: it is what the video path passes, because rows render in
    /// parallel and a shared delay line has no meaning per pixel. Without it a
    /// delay hands its input straight through, so a patch built for the speakers
    /// still shows a picture.
    /// </param>
    public void Evaluate(
        float x,
        float y,
        float t,
        Span<float> registers,
        in FeedbackFrame feedback,
        DelayState? delays = null)
    {
        var ops = Ops;

        // Which line or cell an op uses is its position among the ops of its
        // kind, so each kind is counted on its own.
        var line = 0;
        var cell = 0;

        for (var index = 0; index < ops.Length; index++)
        {
            ref readonly var op = ref ops[index];

            switch (op.Code)
            {
                case OpCode.Const: registers[op.Out] = op.K; break;
                case OpCode.LoadX: registers[op.Out] = x; break;
                case OpCode.LoadY: registers[op.Out] = y; break;
                case OpCode.LoadT: registers[op.Out] = t; break;
                case OpCode.Copy: registers[op.Out] = registers[op.A]; break;

                case OpCode.Neg: registers[op.Out] = -registers[op.A]; break;
                case OpCode.Abs: registers[op.Out] = MathF.Abs(registers[op.A]); break;
                case OpCode.Sin: registers[op.Out] = MathF.Sin(registers[op.A]); break;
                case OpCode.Cos: registers[op.Out] = MathF.Cos(registers[op.A]); break;
                case OpCode.Tan: registers[op.Out] = Guard(MathF.Tan(registers[op.A])); break;
                case OpCode.Sqrt: registers[op.Out] = registers[op.A] <= 0f ? 0f : MathF.Sqrt(registers[op.A]); break;
                case OpCode.Floor: registers[op.Out] = MathF.Floor(registers[op.A]); break;
                case OpCode.Ceil: registers[op.Out] = MathF.Ceiling(registers[op.A]); break;
                case OpCode.Fract: registers[op.Out] = Fract(registers[op.A]); break;
                case OpCode.Sign: registers[op.Out] = MathF.Sign(registers[op.A]); break;
                case OpCode.Exp: registers[op.Out] = Guard(MathF.Exp(registers[op.A])); break;
                case OpCode.Log: registers[op.Out] = registers[op.A] <= 0f ? 0f : MathF.Log(registers[op.A]); break;

                case OpCode.Add: registers[op.Out] = registers[op.A] + registers[op.B]; break;
                case OpCode.Sub: registers[op.Out] = registers[op.A] - registers[op.B]; break;
                case OpCode.Mul: registers[op.Out] = registers[op.A] * registers[op.B]; break;
                case OpCode.Div: registers[op.Out] = Divide(registers[op.A], registers[op.B]); break;
                case OpCode.Mod: registers[op.Out] = Modulo(registers[op.A], registers[op.B]); break;
                case OpCode.Pow: registers[op.Out] = Guard(MathF.Pow(registers[op.A], registers[op.B])); break;
                case OpCode.Min: registers[op.Out] = MathF.Min(registers[op.A], registers[op.B]); break;
                case OpCode.Max: registers[op.Out] = MathF.Max(registers[op.A], registers[op.B]); break;
                case OpCode.Atan2: registers[op.Out] = MathF.Atan2(registers[op.A], registers[op.B]); break;
                case OpCode.Step: registers[op.Out] = registers[op.B] < registers[op.A] ? 0f : 1f; break;
                case OpCode.Hypot:
                {
                    float a = registers[op.A], b = registers[op.B];
                    registers[op.Out] = MathF.Sqrt(a * a + b * b);
                    break;
                }

                case OpCode.Clamp:
                    registers[op.Out] = Math.Clamp(registers[op.A], registers[op.B], MathF.Max(registers[op.B], registers[op.C]));
                    break;

                case OpCode.Mix:
                {
                    float a = registers[op.A], b = registers[op.B], f = registers[op.C];
                    registers[op.Out] = a + (b - a) * f;
                    break;
                }

                case OpCode.Smoothstep:
                    registers[op.Out] = Smoothstep(registers[op.A], registers[op.B], registers[op.C]);
                    break;

                case OpCode.Noise3:
                    registers[op.Out] = Noise.Value3(registers[op.A], registers[op.B], registers[op.C]);
                    break;

                case OpCode.HsvToRgb:
                    HsvToRgb(registers[op.A], registers[op.B], registers[op.C], registers[op.Out..(op.Out + 3)]);
                    break;

                case OpCode.SampleFeedback:
                    Sample(feedback, registers[op.A], registers[op.B], registers[op.Out..(op.Out + 3)]);
                    break;

                case OpCode.Delay:
                {
                    var slot = line++;
                    if (delays is null) { registers[op.Out] = registers[op.A]; break; }

                    // Read before write, so the shortest possible delay is one
                    // evaluation. A zero-sample loop would be algebraic, and
                    // there would be nothing for it to mean.
                    var heard = delays.Read(slot, registers[op.C], op.K);
                    delays.Write(slot, registers[op.A] + Feedback(registers[op.B]) * heard);
                    registers[op.Out] = heard;
                    break;
                }

                case OpCode.Allpass:
                {
                    var slot = line++;
                    if (delays is null) { registers[op.Out] = registers[op.A]; break; }

                    var heard = delays.Read(slot, registers[op.C], op.K);
                    var gain = Feedback(registers[op.B]);
                    var stored = registers[op.A] + gain * heard;

                    delays.Write(slot, stored);
                    registers[op.Out] = heard - gain * stored;
                    break;
                }

                case OpCode.Phase:
                {
                    var slot = cell++;
                    float input = registers[op.A], frequency = registers[op.B];

                    // Without state there is no previous evaluation to step from
                    // — a picture's pixels are one evaluation each, in whatever
                    // order the rows happen to run — so this is the multiply the
                    // accumulator replaces, and over a still frame the two agree.
                    registers[op.Out] = delays is null
                        ? input * frequency + registers[op.C]
                        : delays.Advance(slot, input, frequency) + registers[op.C];
                    break;
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Guard(float v) => float.IsFinite(v) ? v : 0f;

    /// <summary>
    /// Feedback held below one. At exactly one a delay line never decays and at
    /// more than one it doubles every pass, and unlike every other op in here
    /// that damage persists after the knob is turned back down.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Feedback(float v) => float.IsFinite(v) ? Math.Clamp(v, -0.99f, 0.99f) : 0f;

    /// <summary>
    /// The largest float below 1. For a tiny negative input, <c>v - floor(v)</c>
    /// is mathematically just under 1 but cancels to exactly 1.0 in single
    /// precision. Fract is documented as half-open, and Saw and Tile both read
    /// it that way, so the result is pinned just below the boundary instead of
    /// being allowed to reach it.
    /// </summary>
    private const float JustBelowOne = 0.99999994f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Fract(float v)
    {
        var fraction = v - MathF.Floor(v);
        return fraction < 1f ? fraction : JustBelowOne;
    }

    // Exact equality is the point in the three guards below: they trap the one
    // divisor that makes the result undefined. A tolerance would not be safer,
    // it would be wrong — Divide(1, 1e-20f) is a legitimate 1e20, and an epsilon
    // would silently flatten it to zero. Guard already handles the overflow.
    // ReSharper disable CompareOfFloatsByEqualityOperator

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Divide(float a, float b) => b == 0f ? 0f : Guard(a / b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Modulo(float a, float b) => b == 0f ? 0f : Guard(a - b * MathF.Floor(a / b));

    private static float Smoothstep(float edge0, float edge1, float x)
    {
        if (edge0 == edge1) return x < edge0 ? 0f : 1f;

        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    // ReSharper restore CompareOfFloatsByEqualityOperator

    private static void HsvToRgb(float h, float s, float v, Span<float> rgb)
    {
        h = Fract(h) * 6f;
        s = Math.Clamp(s, 0f, 1f);

        var sector = (int)h;
        var f = h - sector;
        var p = v * (1f - s);
        var q = v * (1f - s * f);
        var t = v * (1f - s * (1f - f));

        (rgb[0], rgb[1], rgb[2]) = sector switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
    }

    /// <summary>Bilinear read of the previous frame in patch coordinates, clamped at the edges.</summary>
    private static void Sample(in FeedbackFrame frame, float u, float v, Span<float> rgb)
    {
        var pixels = frame.Pixels;
        if (pixels is null || frame.Width < 2 || frame.Height < 2)
        {
            rgb[0] = rgb[1] = rgb[2] = 0f;
            return;
        }

        var fx = (u / frame.Aspect * 0.5f + 0.5f) * (frame.Width - 1);
        var fy = (0.5f - v * 0.5f) * (frame.Height - 1);

        fx = Math.Clamp(float.IsFinite(fx) ? fx : 0f, 0f, frame.Width - 1.001f);
        fy = Math.Clamp(float.IsFinite(fy) ? fy : 0f, 0f, frame.Height - 1.001f);

        int x0 = (int)fx, y0 = (int)fy;
        float tx = fx - x0, ty = fy - y0;

        var row0 = y0 * frame.Width;
        var row1 = row0 + frame.Width;
        var i00 = (row0 + x0) * 3;
        var i10 = i00 + 3;
        var i01 = (row1 + x0) * 3;
        var i11 = i01 + 3;

        for (var c = 0; c < 3; c++)
        {
            var top = pixels[i00 + c] + (pixels[i10 + c] - pixels[i00 + c]) * tx;
            var bottom = pixels[i01 + c] + (pixels[i11 + c] - pixels[i01 + c]) * tx;
            rgb[c] = top + (bottom - top) * ty;
        }
    }
}
