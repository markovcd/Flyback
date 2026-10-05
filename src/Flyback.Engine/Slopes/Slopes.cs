using Flyback.Core.Compile;
using Flyback.Engine.Compile;

namespace Flyback.Engine.Slopes;

/// <summary>
/// Forward-mode slopes through a compiled program: once the interpreter has run,
/// a second walk over the same ops carries how far each register moves per unit
/// of each knob, one lane per knob.
/// </summary>
/// <remarks>
/// The second walk reads the values the first left behind, which holds because
/// the emitter writes every register once. On the picture path what a pixel
/// remembers from the frame before is held still.
/// </remarks>
internal sealed class Slopes
{
    private readonly SlopeProgram source;
    private readonly CompiledPatch program;
    private readonly Op[] ops;
    private readonly int[] laneOf;
    private readonly double[] first;
    private readonly double[] second;
    private readonly double[] plus = new double[3];
    private readonly double[] minus = new double[3];
    private readonly double footprint;

    /// <param name="source"></param>
    /// <param name="knobs"></param>
    /// <param name="footprint">
    /// A pixel's size in the patch's units, to give a hard edge the slope its pixels
    /// see as it moves; zero for the exact slope, which is flat on either side of an edge.
    /// </param>
    public Slopes(SlopeProgram source, IReadOnlyList<int> knobs, double footprint = 0d)
    {
        this.source = source;
        this.footprint = footprint;
        program = source.Program;
        ops = program.Ops;
        Knobs = [.. knobs];
        KnobLanes = Knobs.Length;
        Lanes = KnobLanes + (footprint > 0d ? 2 : 0);

        laneOf = new int[program.LiveInputs.Count];
        Array.Fill(laneOf, -1);

        for (var live = 0; live < laneOf.Length; live++)
            if (source.KnobOf[live] is var knob and >= 0)
                laneOf[live] = Array.IndexOf(Knobs, knob);

        Values = program.AllocateRegisters();
        Tangents = new double[Values.Length * Lanes];
        first = new double[Lanes];
        second = new double[Lanes];

        WrittenOnce(ops, Values.Length);
    }

    /// <summary>The knob each lane follows, as an index into <see cref="SlopeProgram.Knobs"/>.</summary>
    public int[] Knobs { get; }

    /// <summary>Every lane, the knobs' and then, with a footprint, x's and y's.</summary>
    public int Lanes { get; }

    public int KnobLanes { get; }

    public double[] Values { get; }

    /// <summary>Every register's slopes, a register's lanes side by side.</summary>
    public double[] Tangents { get; }

    /// <summary>How many slopes came out not a number and were taken as flat.</summary>
    public long NonFinite { get; private set; }

    public ReadOnlySpan<double> Of(int register) => Tangents.AsSpan(register * Lanes, Lanes);

    /// <summary>The slopes of component <paramref name="component"/> of what the sink is handed.</summary>
    public ReadOnlySpan<double> Output(int component) => Of(program.OutputBase + component);

    public double Value(int component) => Values[program.OutputBase + component];

    /// <summary>One pixel and its slopes, against the frame before held still.</summary>
    public void Pixel(double x, double y, double t, double aspect, in FeedbackFrame feedback, Span<float> planes)
    {
        program.Evaluate(x, y, t, Values, feedback, null, aspect, source.Live, planes);
        Pass(feedback, null, null);
    }

    /// <summary>One evaluation of the sound and its slopes, carried through what it remembers.</summary>
    public void Sample(double t, double aspect, DelayState delays, SlopeMemory memory)
    {
        program.Evaluate(0d, 0d, t, Values, default, delays, aspect, source.Live);
        Pass(default, delays, memory);
    }

    private void Pass(in FeedbackFrame feedback, DelayState? delays, SlopeMemory? memory)
    {
        var v = Values;
        var line = 0;
        var cell = 0;

        for (var index = 0; index < ops.Length; index++)
        {
            var op = ops[index];

            switch (op.Code)
            {
                case OpCode.Const:
                case OpCode.LoadT:
                case OpCode.LoadAspect:
                    T(op.Out).Clear();
                    break;

                case OpCode.LoadX:
                case OpCode.LoadY:
                {
                    var o = T(op.Out);
                    o.Clear();

                    if (footprint > 0d) o[KnobLanes + (op.Code is OpCode.LoadX ? 0 : 1)] = 1d;
                    break;
                }

                // Flat between jumps, so with a footprint a knob's slope is the jump as its pixels see it pass.
                case OpCode.Floor:
                    T(op.Out).Clear();
                    Secant(op.Out, Math.Floor, v[op.A], Of(op.A));
                    break;

                case OpCode.Ceil:
                    T(op.Out).Clear();
                    Secant(op.Out, Math.Ceiling, v[op.A], Of(op.A));
                    break;

                case OpCode.Sign:
                    T(op.Out).Clear();
                    Secant(op.Out, CompiledPatch.Signum, v[op.A], Of(op.A));
                    break;

                case OpCode.Step:
                {
                    T(op.Out).Clear();

                    if (footprint <= 0d) break;

                    var du = Difference(op.B, op.A);
                    Secant(op.Out, u => u < 0d ? 0d : 1d, v[op.B] - v[op.A], du);
                    break;
                }

                case OpCode.LoadLive:
                {
                    var o = T(op.Out);
                    o.Clear();

                    if ((uint)op.K < (uint)laneOf.Length && laneOf[(int)op.K] is var lane and >= 0) o[lane] = 1d;
                    break;
                }

                case OpCode.Copy: Combine(op.Out, 1d, op.A); break;
                case OpCode.Neg: Combine(op.Out, -1d, op.A); break;
                case OpCode.Abs:
                    Combine(op.Out, CompiledPatch.Signum(v[op.A]), op.A);
                    Secant(op.Out, Math.Abs, v[op.A], Of(op.A));
                    break;

                case OpCode.Sin: Combine(op.Out, Math.Cos(v[op.A]), op.A); break;
                case OpCode.Cos: Combine(op.Out, -Math.Sin(v[op.A]), op.A); break;

                case OpCode.Tan:
                {
                    var raw = Math.Tan(v[op.A]);
                    Combine(op.Out, double.IsFinite(raw) ? 1d + raw * raw : 0d, op.A);
                    break;
                }

                case OpCode.Sqrt: Combine(op.Out, v[op.A] <= 0d ? 0d : 0.5d / v[op.Out], op.A); break;
                case OpCode.Fract:
                    Combine(op.Out, 1d, op.A);
                    Secant(op.Out, CompiledPatch.Fract, v[op.A], Of(op.A));
                    break;

                case OpCode.Exp:
                {
                    var raw = Math.Exp(v[op.A]);
                    Combine(op.Out, double.IsFinite(raw) ? raw : 0d, op.A);
                    break;
                }

                case OpCode.Log: Combine(op.Out, v[op.A] <= 0d ? 0d : 1d / v[op.A], op.A); break;

                case OpCode.Add: Combine(op.Out, 1d, op.A, 1d, op.B); break;
                case OpCode.Sub: Combine(op.Out, 1d, op.A, -1d, op.B); break;
                case OpCode.Mul: Combine(op.Out, v[op.B], op.A, v[op.A], op.B); break;

                case OpCode.Div:
                {
                    double a = v[op.A], b = v[op.B];
                    var raw = a / b;

                    if (b == 0d || !double.IsFinite(raw)) T(op.Out).Clear();
                    else Combine(op.Out, 1d / b, op.A, -raw / b, op.B);
                    break;
                }

                case OpCode.Mod:
                {
                    double a = v[op.A], b = v[op.B];
                    var whole = Math.Floor(a / b);

                    if (b == 0d || !double.IsFinite(a - b * whole)) T(op.Out).Clear();
                    else
                    {
                        Combine(op.Out, 1d, op.A, -whole, op.B);
                        if (footprint > 0d) ModSecant(op, a, b);
                    }

                    break;
                }

                case OpCode.Pow:
                {
                    double a = v[op.A], b = v[op.B];
                    var raw = Math.Pow(a, b);

                    if (!double.IsFinite(raw)) T(op.Out).Clear();
                    else Combine(op.Out, CompiledPatch.Guard(b * Math.Pow(a, b - 1d)), op.A, a > 0d ? raw * Math.Log(a) : 0d, op.B);
                    break;
                }

                case OpCode.Min:
                    if (v[op.A] <= v[op.B]) Combine(op.Out, 1d, op.A);
                    else Combine(op.Out, 1d, op.B);
                    break;

                case OpCode.Max:
                    if (v[op.A] >= v[op.B]) Combine(op.Out, 1d, op.A);
                    else Combine(op.Out, 1d, op.B);
                    break;

                case OpCode.Atan2:
                {
                    double a = v[op.A], b = v[op.B];
                    var r2 = a * a + b * b;

                    if (r2 == 0d) T(op.Out).Clear();
                    else Combine(op.Out, b / r2, op.A, -a / r2, op.B);
                    break;
                }

                case OpCode.Hypot:
                {
                    var o = v[op.Out];

                    if (o == 0d) T(op.Out).Clear();
                    else Combine(op.Out, v[op.A] / o, op.A, v[op.B] / o, op.B);
                    break;
                }

                case OpCode.Clamp:
                {
                    double a = v[op.A], b = v[op.B], c = v[op.C];
                    var high = Math.Max(b, c);

                    if (a < b) Combine(op.Out, 1d, op.B);
                    else if (a > high) Combine(op.Out, 1d, c >= b ? op.C : op.B);
                    else Combine(op.Out, 1d, op.A);

                    if (footprint > 0d) ClampSecant(op, a, b, high, c >= b);
                    break;
                }

                case OpCode.Mix:
                {
                    double a = v[op.A], b = v[op.B], f = v[op.C];
                    Combine(op.Out, 1d - f, op.A, f, op.B, b - a, op.C);
                    break;
                }

                case OpCode.Smoothstep:
                {
                    double e0 = v[op.A], e1 = v[op.B], x = v[op.C];
                    var span = e1 - e0;
                    var u = span == 0d ? 0d : (x - e0) / span;

                    if (span == 0d || u <= 0d || u >= 1d) T(op.Out).Clear();
                    else
                    {
                        var rise = 6d * u * (1d - u);
                        Combine(op.Out, rise * (x - e1) / (span * span), op.A, -rise * (x - e0) / (span * span), op.B, rise / span, op.C);
                    }

                    if (footprint <= 0d) break;

                    if (span == 0d)
                    {
                        Secant(op.Out, w => w < 0d ? 0d : 1d, x - e0, Difference(op.C, op.A));
                        break;
                    }

                    // In u = (x - e0) / (e1 - e0), where the curve is the same shape however wide.
                    var du = first.AsSpan();
                    var dx = Of(op.C);
                    var d0 = Of(op.A);
                    var d1 = Of(op.B);

                    for (var j = 0; j < Lanes; j++) du[j] = (dx[j] - d0[j]) / span - (x - e0) * (d1[j] - d0[j]) / (span * span);

                    Secant(op.Out, Eased, u, du);
                    break;
                }

                case OpCode.Noise3:
                {
                    var (gx, gy, gz) = Noise.Slope3(v[op.A], v[op.B], v[op.C]);
                    Combine(op.Out, gx, op.A, gy, op.B, gz, op.C);
                    break;
                }

                case OpCode.HsvToRgb: HsvToRgb(op); break;
                case OpCode.SampleFeedback: SampleFeedback(op, feedback); break;
                case OpCode.SamplePicture: SamplePicture(op); break;

                case OpCode.Table:
                {
                    var clips = program.TableArray;
                    var clip = (int)op.K;
                    var slope = 0d;

                    if ((uint)clip < (uint)clips.Length && double.IsFinite(v[op.A]))
                    {
                        var samples = clips[clip].Samples;
                        var rate = (double)clips[clip].SampleRate;
                        var position = v[op.A] * rate;

                        if (position >= 0d && position < samples.Length)
                        {
                            var whole = (int)position;
                            var next = whole + 1 < samples.Length ? samples[whole + 1] : 0f;
                            slope = (next - samples[whole]) * rate;
                        }
                    }

                    Combine(op.Out, slope, op.A);
                    break;
                }

                case OpCode.Tap:
                    break;

                case OpCode.Delay:
                {
                    var slot = line++;

                    if (delays is null || memory is null) Combine(op.Out, 1d, op.A);
                    else Delay(op, slot, delays, memory);
                    break;
                }

                case OpCode.Allpass:
                {
                    var slot = line++;

                    if (delays is null || memory is null) Combine(op.Out, 1d, op.A);
                    else Allpass(op, slot, delays, memory);
                    break;
                }

                case OpCode.Phase:
                {
                    var slot = cell++;

                    if (memory is null) Combine(op.Out, v[op.B], op.A, v[op.A], op.B, 1d, op.C);
                    else Phase(op, slot, memory);
                    break;
                }

                case OpCode.UnitRead:
                    if (memory is null) T(op.Out).Clear();
                    else memory.Units.AsSpan((int)op.K * Lanes, Lanes).CopyTo(T(op.Out));
                    break;

                case OpCode.PlaneRead:
                    if (memory is null) T(op.Out).Clear();
                    else memory.Planes.AsSpan((int)op.K * Lanes, Lanes).CopyTo(T(op.Out));
                    break;

                case OpCode.UnitWrite:
                    if (memory is not null) Keep(memory.Units, (int)op.K, op.A, Held(v[op.A]));
                    break;

                case OpCode.PlaneWrite:
                    if (memory is not null) Keep(memory.Planes, (int)op.K, op.A, Held(v[op.A]));
                    break;

                case OpCode.ClockWrite:
                    if (memory is not null) Keep(memory.Units, (int)op.K, op.A, double.IsFinite(v[op.A]));
                    break;

                default:
                    throw new InvalidOperationException($"No slope rule for {op.Code}.");
            }
        }
    }

    /// <summary>
    /// With a footprint, puts in the knob lanes of <paramref name="into"/> the slope of
    /// <paramref name="curve"/> averaged over the pixel at <paramref name="u"/>: the
    /// secant across the pixel's width, in u's units, times how far u moves.
    /// </summary>
    private void Secant(int into, Func<double, double> curve, double u, ReadOnlySpan<double> du)
    {
        if (footprint <= 0d) return;

        var width = Width(du);

        if (!(width > 0d)) return;

        var slope = (curve(u + width / 2d) - curve(u - width / 2d)) / width;
        var o = T(into);

        for (var j = 0; j < KnobLanes; j++) o[j] = slope * du[j];
    }

    /// <summary>A pixel's width in the units of a value that moves across the frame by <paramref name="du"/>.</summary>
    private double Width(ReadOnlySpan<double> du) =>
        Math.Sqrt(du[KnobLanes] * du[KnobLanes] + du[KnobLanes + 1] * du[KnobLanes + 1]) * footprint;

    /// <summary>The slopes of one register less another's, in a scratch row.</summary>
    private Span<double> Difference(int plus, int minus)
    {
        var d = first.AsSpan();
        var p = Of(plus);
        var m = Of(minus);

        for (var j = 0; j < Lanes; j++) d[j] = p[j] - m[j];
        return d;
    }

    private static double Eased(double u)
    {
        var t = Math.Clamp(u, 0d, 1d);
        return t * t * (3d - 2d * t);
    }

    /// <summary><c>a - b * floor(a / b)</c> with the floor averaged over the pixel.</summary>
    private void ModSecant(in Op op, double a, double b)
    {
        var q = a / b;
        var da = Of(op.A);
        var db = Of(op.B);
        var dq = first.AsSpan();

        for (var j = 0; j < Lanes; j++) dq[j] = (da[j] - q * db[j]) / b;

        var width = Width(dq);

        if (!(width > 0d)) return;

        var jumps = (Math.Floor(q + width / 2d) - Math.Floor(q - width / 2d)) / width;
        var whole = Math.Floor(q);
        var o = T(op.Out);

        for (var j = 0; j < KnobLanes; j++) o[j] = da[j] - whole * db[j] - b * jumps * dq[j];
    }

    /// <summary>A clamp averaged over the pixel: the share of it inside the bounds follows a, the rest the bound it is past.</summary>
    private void ClampSecant(in Op op, double a, double low, double high, bool ownHigh)
    {
        var da = Of(op.A);
        var width = Width(da);

        if (!(width > 0d)) return;

        var below = Math.Clamp((low - (a - width / 2d)) / width, 0d, 1d);
        var above = Math.Clamp((a + width / 2d - high) / width, 0d, 1d);
        var inside = (Math.Clamp(a + width / 2d, low, high) - Math.Clamp(a - width / 2d, low, high)) / width;
        var dl = Of(op.B);
        var dh = Of(ownHigh ? op.C : op.B);
        var o = T(op.Out);

        for (var j = 0; j < KnobLanes; j++) o[j] = inside * da[j] + below * dl[j] + above * dh[j];
    }

    /// <summary>Whether a cell keeps a value as it is, rather than clamped or zeroed.</summary>
    private static bool Held(double value) => double.IsNormal(value) && Math.Abs(value) < 16d;

    private void Keep(double[] cells, int slot, int register, bool held)
    {
        var into = cells.AsSpan(slot * Lanes, Lanes);

        if (held) Of(register).CopyTo(into);
        else into.Clear();
    }

    private void Delay(in Op op, int slot, DelayState delays, SlopeMemory memory)
    {
        var v = Values;
        var heard = T(op.Out);

        memory.Read(slot, v[op.C], op.K, delays.SampleRate, heard);
        Axpy(heard, delays.ReadSlopeBeforeWrite(slot, v[op.C], op.K), Of(op.C));

        var gain = CompiledPatch.Feedback(v[op.B]);
        var written = v[op.A] + gain * v[op.Out];
        var w = first.AsSpan();

        w.Clear();

        if (Narrowed(written))
        {
            Axpy(w, 1d, Of(op.A));
            Axpy(w, gain, heard);
            Axpy(w, Turnable(v[op.B]) ? v[op.Out] : 0d, Of(op.B));
        }

        memory.Write(slot, w);
    }

    private void Allpass(in Op op, int slot, DelayState delays, SlopeMemory memory)
    {
        var v = Values;
        var heardValue = delays.ReadBeforeWrite(slot, v[op.C], op.K);
        var heard = first.AsSpan();

        memory.Read(slot, v[op.C], op.K, delays.SampleRate, heard);
        Axpy(heard, delays.ReadSlopeBeforeWrite(slot, v[op.C], op.K), Of(op.C));

        var gain = CompiledPatch.Feedback(v[op.B]);
        var turnable = Turnable(v[op.B]) ? 1d : 0d;
        var stored = v[op.A] + gain * heardValue;
        var keep = second.AsSpan();

        keep.Clear();
        Axpy(keep, 1d, Of(op.A));
        Axpy(keep, gain, heard);
        Axpy(keep, turnable * heardValue, Of(op.B));

        var o = T(op.Out);
        o.Clear();
        Axpy(o, 1d, heard);
        Axpy(o, -gain, keep);
        Axpy(o, -turnable * stored, Of(op.B));

        if (!Narrowed(stored)) keep.Clear();

        memory.Write(slot, keep);
    }

    private void Phase(in Op op, int slot, SlopeMemory memory)
    {
        var v = Values;
        double a = v[op.A], b = v[op.B];
        var inputOk = double.IsFinite(a);
        var rateOk = double.IsFinite(b);
        var input = inputOk ? a : memory.PreviousInputs[slot];
        var rate = rateOk ? b : 0d;
        var running = memory.Running[slot];
        var stepOk = running && double.IsFinite((input - memory.PreviousInputs[slot]) * rate);
        var moved = input - memory.PreviousInputs[slot];

        var phase = memory.Phases.AsSpan(slot * Lanes, Lanes);
        var previous = memory.PreviousSlopes.AsSpan(slot * Lanes, Lanes);
        var da = Of(op.A);
        var db = Of(op.B);
        var dc = Of(op.C);
        var o = T(op.Out);

        for (var j = 0; j < Lanes; j++)
        {
            var now = inputOk ? da[j] : previous[j];

            if (stepOk) phase[j] += (now - previous[j]) * rate + moved * (rateOk ? db[j] : 0d);

            previous[j] = now;
            o[j] = phase[j] + dc[j];
        }

        memory.PreviousInputs[slot] = input;
        memory.Running[slot] = true;
    }

    private static bool Narrowed(double value)
    {
        var narrow = (float)value;
        return float.IsNormal(narrow) && MathF.Abs(narrow) < 16f;
    }

    private static bool Turnable(double gain) => double.IsFinite(gain) && Math.Abs(gain) < 0.99d;

    private void HsvToRgb(in Op op)
    {
        var v = Values;
        var h = CompiledPatch.Fract(v[op.A]) * 6d;
        var rawS = v[op.B];
        var s = Math.Clamp(rawS, 0d, 1d);
        var ds = rawS > 0d && rawS < 1d ? 1d : 0d;
        var value = v[op.C];
        var sector = (int)h;
        var f = h - sector;

        (double H, double S, double V) top = (0d, 0d, 1d);
        (double H, double S, double V) p = (0d, -value * ds, 1d - s);
        (double H, double S, double V) q = (-value * s * 6d, -value * f * ds, 1d - s * f);
        (double H, double S, double V) t = (value * s * 6d, -value * (1d - f) * ds, 1d - s * (1d - f));

        var (r, g, bl) = sector switch
        {
            0 => (top, t, p),
            1 => (q, top, p),
            2 => (p, top, t),
            3 => (p, q, top),
            4 => (t, p, top),
            _ => (top, p, q),
        };

        Combine(op.Out, r.H, op.A, r.S, op.B, r.V, op.C);
        Combine(op.Out + 1, g.H, op.A, g.S, op.B, g.V, op.C);
        Combine(op.Out + 2, bl.H, op.A, bl.S, op.B, bl.V, op.C);
    }

    private void SampleFeedback(in Op op, in FeedbackFrame frame)
    {
        var pixels = frame.Pixels;

        if (pixels is null || frame.Width < 2 || frame.Height < 2)
        {
            for (var c = 0; c < 3; c++) T(op.Out + c).Clear();
            return;
        }

        var v = Values;
        var fx = (v[op.A] / frame.Aspect * 0.5d + 0.5d) * (frame.Width - 1);
        var fy = (0.5d - v[op.B] * 0.5d) * (frame.Height - 1);
        var dfx = double.IsFinite(fx) && fx > 0d && fx < frame.Width - 1.001d ? 0.5d / frame.Aspect * (frame.Width - 1) : 0d;
        var dfy = double.IsFinite(fy) && fy > 0d && fy < frame.Height - 1.001d ? -0.5d * (frame.Height - 1) : 0d;

        fx = Math.Clamp(double.IsFinite(fx) ? fx : 0d, 0d, frame.Width - 1.001d);
        fy = Math.Clamp(double.IsFinite(fy) ? fy : 0d, 0d, frame.Height - 1.001d);

        int x0 = (int)fx, y0 = (int)fy;
        double tx = fx - x0, ty = fy - y0;

        var row0 = y0 * frame.Width;
        var i00 = (row0 + x0) * 3;
        var i10 = i00 + 3;
        var i01 = (row0 + frame.Width + x0) * 3;
        var i11 = i01 + 3;

        for (var c = 0; c < 3; c++)
        {
            var top = pixels[i00 + c] + (pixels[i10 + c] - pixels[i00 + c]) * tx;
            var bottom = pixels[i01 + c] + (pixels[i11 + c] - pixels[i01 + c]) * tx;
            var across = (1d - ty) * (pixels[i10 + c] - pixels[i00 + c]) + ty * (pixels[i11 + c] - pixels[i01 + c]);

            Combine(op.Out + c, across * dfx, op.A, (bottom - top) * dfy, op.B);
        }
    }

    /// <summary>A picture's slope by a central difference, since it is a bilinear read whose pieces are straight.</summary>
    private void SamplePicture(in Op op)
    {
        var pictures = program.PictureArray;
        var picture = (int)op.K;

        if ((uint)picture >= (uint)pictures.Length)
        {
            for (var c = 0; c < 3; c++) T(op.Out + c).Clear();
            return;
        }

        // With a footprint the difference spans the pixel, so detail finer than it is averaged rather than missed.
        var hx = footprint > 0d ? Math.Max(1e-5, Width(Of(op.A)) / 2d) : 1e-5;
        var hy = footprint > 0d ? Math.Max(1e-5, Width(Of(op.B)) / 2d) : 1e-5;
        double x = Values[op.A], y = Values[op.B];
        Span<double> across = stackalloc double[3];

        pictures[picture].At(x + hx, y, plus);
        pictures[picture].At(x - hx, y, minus);
        for (var c = 0; c < 3; c++) across[c] = (plus[c] - minus[c]) / (2d * hx);

        pictures[picture].At(x, y + hy, plus);
        pictures[picture].At(x, y - hy, minus);
        for (var c = 0; c < 3; c++) Combine(op.Out + c, across[c], op.A, (plus[c] - minus[c]) / (2d * hy), op.B);
    }

    private Span<double> T(int register) => Tangents.AsSpan(register * Lanes, Lanes);

    private void Combine(int into, double pa, int a, double pb = 0d, int b = -1, double pc = 0d, int c = -1)
    {
        var o = T(into);
        o.Clear();

        if (pa != 0d && a >= 0) Axpy(o, Finite(pa), Of(a));
        if (pb != 0d && b >= 0) Axpy(o, Finite(pb), Of(b));
        if (pc != 0d && c >= 0) Axpy(o, Finite(pc), Of(c));
    }

    private double Finite(double partial)
    {
        if (double.IsFinite(partial)) return partial;

        NonFinite++;
        return 0d;
    }

    private static void Axpy(Span<double> into, double k, ReadOnlySpan<double> x)
    {
        if (k == 0d) return;

        for (var j = 0; j < into.Length; j++) into[j] += k * x[j];
    }

    /// <summary>Refuses a program that writes a register twice, whose values the second walk could not trust.</summary>
    private static void WrittenOnce(Op[] ops, int registers)
    {
        var written = new bool[registers];

        foreach (var op in ops)
        {
            for (var w = 0; w < OpShape.Outputs(op.Code); w++)
            {
                if (written[op.Out + w]) throw new InvalidOperationException($"r{op.Out + w} is written twice ({op}).");

                written[op.Out + w] = true;
            }
        }
    }
}
