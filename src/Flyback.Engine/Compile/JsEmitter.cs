using System.Globalization;
using System.Text;

namespace Flyback.Core.Compile;

/// <summary>
/// Turns a sound's program into JavaScript that renders it a buffer at a time, for a
/// browser, which runs no IL. Text only, like <see cref="GlslEmitter"/>: where the
/// program's memory lives is handed to the script when it is made — see
/// <see cref="JsLayout"/>.
/// </summary>
/// <remarks>
/// The script is <see cref="CompiledPatch.Evaluate"/> transcribed, and its doubles
/// are the interpreter's except where a browser's <c>Math.sin</c>, <c>exp</c> and the
/// rest round their last bit differently. Arithmetic the interpreter does in
/// <see cref="float"/> is rounded with <c>Math.fround</c> where it happens.
/// <para>
/// Every constant is read from the layout's <c>constants</c> rather than written in, as
/// the shader reads its constants as uniforms: a knob turned leaves the text as it was, so
/// the engine keeps the script it has already optimized.
/// </para>
/// <para>
/// Ops are cut into functions of <see cref="ChunkSize"/>, as the IL is, since an
/// engine stops optimizing a function past a size. A register lives in a local
/// within its chunk and in a shared bank across them.
/// </para>
/// </remarks>
internal static class JsEmitter
{
    public const int ChunkSize = 128;

    /// <summary>
    /// A function expression that takes a layout and returns
    /// <c>render(time, frames, aspect, out)</c>, or null for a program this cannot
    /// run: one that reads a picture, which only a picture's program carries.
    /// </summary>
    /// <remarks>
    /// <c>render</c> writes two floats an evaluation, left then right, from element
    /// <c>out</c> of the heap's floats, and steps the clock the way
    /// <c>AudioRenderer.Render</c> does, so the same times reach the same ops.
    /// </remarks>
    public static string? Emit(CompiledPatch program)
    {
        if (program.PictureArray.Length > 0) return null;

        var ops = program.Ops;
        var text = new StringBuilder();

        text.Append(Prelude);

        var registers = Math.Max(program.RegisterCount, program.OutputWidth);
        text.Append(CultureInfo.InvariantCulture, $"const R = new Float64Array({registers});\n");

        var delays = ops.Count(o => o.Code is OpCode.Delay or OpCode.Allpass);
        for (var i = 0; i < delays; i++)
            text.Append(CultureInfo.InvariantCulture, $"const L{i} = m.lines[{i}], N{i} = m.lineLengths[{i}];\n");

        for (var i = 0; i < program.TraceCount; i++)
            text.Append(CultureInfo.InvariantCulture, $"const T{i} = m.traces[{i}];\n");

        for (var i = 0; i < program.TableArray.Length; i++)
            text.Append(CultureInfo.InvariantCulture, $"const S{i} = m.tables[{i}][0], SN{i} = m.tables[{i}][1], SR{i} = m.tables[{i}][2];\n");

        var outputs = Outputs(program);
        var chunks = Chunks(ops, outputs);

        var remembered = 0;

        for (var c = 0; c < chunks.Count; c++) Chunk(text, program, chunks[c], c, ref remembered);

        text.Append(CultureInfo.InvariantCulture, $"const M = new Float64Array({Math.Max(remembered, 1)}).fill(NaN);\n");

        var left = program.OutputBase;
        var right = program.OutputWidth > 1 ? program.OutputBase + 1 : program.OutputBase;

        text.Append("return function render(time, frames, aspect, out) {\n");
        text.Append("F32 = m.f32(); F64 = m.f64(); I32 = m.i32(); U8 = m.u8();\n");
        text.Append("let clock = time, o = out;\n");
        text.Append("for (let f = 0; f < frames; f++) {\n");
        text.Append("for (let k = 0; k < OS; k++) {\n");
        text.Append("const t = clock + k * INNER;\n");

        for (var c = 0; c < chunks.Count; c++)
            text.Append(CultureInfo.InvariantCulture, $"c{c}(t, aspect);\n");

        text.Append(CultureInfo.InvariantCulture, $"F32[o++] = R[{left}]; F32[o++] = R[{right}];\n");
        text.Append("}\nclock += OUTER;\n}\n};\n})");

        return text.ToString();
    }

    /// <summary>One stretch of ops as a function, with its registers in locals.</summary>
    private static void Chunk(StringBuilder text, CompiledPatch program, Stretch stretch, int number, ref int remembered)
    {
        var ops = program.Ops;

        text.Append(CultureInfo.InvariantCulture, $"function c{number}(t, aspect) {{\n");

        foreach (var register in stretch.Locals)
        {
            if (stretch.Loads.Contains(register))
                text.Append(CultureInfo.InvariantCulture, $"let r{register} = R[{register}];\n");
            else
                text.Append(CultureInfo.InvariantCulture, $"let r{register} = 0;\n");
        }

        for (var i = stretch.From; i < stretch.To; i++)
        {
            var at = i - stretch.From;
            Op(text, program, ops[i], stretch.Lines[at], stretch.Cells[at], stretch.Constants[at], ref remembered);
        }

        foreach (var register in stretch.Stores)
            text.Append(CultureInfo.InvariantCulture, $"R[{register}] = r{register};\n");

        text.Append("}\n");
    }

    private static void Op(StringBuilder text, CompiledPatch program, Op op, int line, int cell, int constant, ref int remembered)
    {
        var o = $"r{op.Out}";
        var a = $"r{op.A}";
        var b = $"r{op.B}";
        var c = $"r{op.C}";
        var k = (int)op.K;

        var statement = op.Code switch
        {
            OpCode.Const => $"{o} = K[{constant}];",
            OpCode.LoadX or OpCode.LoadY => $"{o} = 0;",
            OpCode.LoadT => $"{o} = t;",
            OpCode.LoadAspect => $"{o} = aspect;",
            OpCode.LoadLive => k >= 0 ? $"{o} = {k} < LIVEN ? F32[LIVE + {k}] : 0;" : $"{o} = 0;",
            OpCode.Copy => $"{o} = {a};",

            OpCode.Neg => $"{o} = -{a};",
            OpCode.Abs => $"{o} = Math.abs({a});",
            OpCode.Sin => $"{o} = Math.sin({a});",
            OpCode.Cos => $"{o} = Math.cos({a});",
            OpCode.Tan => $"{o} = guard(Math.tan({a}));",
            OpCode.Sqrt => $"{o} = {a} <= 0 ? 0 : Math.sqrt({a});",
            OpCode.Floor => $"{o} = Math.floor({a});",
            OpCode.Ceil => $"{o} = Math.ceil({a});",
            OpCode.Fract => $"{o} = fract({a});",
            OpCode.Sign => $"{o} = signum({a});",
            OpCode.Exp => $"{o} = guard(Math.exp({a}));",
            OpCode.Log => $"{o} = {a} <= 0 ? 0 : Math.log({a});",

            OpCode.Add => $"{o} = {a} + {b};",
            OpCode.Sub => $"{o} = {a} - {b};",
            OpCode.Mul => $"{o} = {a} * {b};",
            OpCode.Div => $"{o} = divide({a}, {b});",
            OpCode.Mod => $"{o} = modulo({a}, {b});",
            OpCode.Pow => Remembered(ref remembered, op, $"guard(pow({a}, {b}))"),
            OpCode.Min => $"{o} = Math.min({a}, {b});",
            OpCode.Max => $"{o} = Math.max({a}, {b});",
            OpCode.Atan2 => $"{o} = Math.atan2({a}, {b});",
            OpCode.Step => $"{o} = {b} < {a} ? 0 : 1;",
            OpCode.Hypot => $"{o} = Math.sqrt({a} * {a} + {b} * {b});",
            OpCode.Clamp => $"{o} = clamp({a}, {b}, Math.max({b}, {c}));",
            OpCode.Mix => $"{o} = {a} + ({b} - {a}) * {c};",
            OpCode.Smoothstep => $"{o} = smoothstep({a}, {b}, {c});",
            OpCode.Noise3 => $"{o} = noise3({a}, {b}, {c});",

            OpCode.HsvToRgb =>
                $"hsv({a}, {b}, {c}); r{op.Out} = H[0]; r{op.Out + 1} = H[1]; r{op.Out + 2} = H[2];",

            // The speakers have no previous frame and, as a program here must, no pictures.
            OpCode.SampleFeedback or OpCode.SamplePicture =>
                $"r{op.Out} = 0; r{op.Out + 1} = 0; r{op.Out + 2} = 0;",

            OpCode.Tap => k >= 0 && k < program.TraceCount
                ? $"{{ const h = I32[TH + {k}]; F32[T{k} + h] = Number.isFinite({a}) ? {a} : 0; I32[TH + {k}] = (h + 1) % TL; }}"
                : "",

            OpCode.Table => k >= 0 && k < program.TableArray.Length
                ? $"{o} = table(S{k}, SN{k}, SR{k}, {a});"
                : $"{o} = 0;",

            OpCode.Delay =>
                $"{{ const heard = readLine(L{line}, N{line}, {line}, {c}, {Number(op.K)}); "
                + $"writeLine(L{line}, N{line}, {line}, {a} + feedback({b}) * heard); {o} = heard; }}",

            OpCode.Allpass =>
                $"{{ const heard = readLine(L{line}, N{line}, {line}, {c}, {Number(op.K)}); const gain = feedback({b}); "
                + $"const stored = {a} + gain * heard; writeLine(L{line}, N{line}, {line}, stored); {o} = heard - gain * stored; }}",

            OpCode.UnitRead => $"{o} = F64[UNITS + {k}];",
            OpCode.UnitWrite => $"F64[UNITS + {k}] = bounded({a});",
            OpCode.ClockWrite => $"F64[UNITS + {k}] = Number.isFinite({a}) ? {a} : 0;",
            OpCode.PlaneRead => $"{o} = F64[PLANES + {k}];",
            OpCode.PlaneWrite => $"F64[PLANES + {k}] = bounded({a});",
            OpCode.Phase => $"{o} = advance({cell}, {a}, {b}) + {c};",

            _ => throw new ArgumentOutOfRangeException(nameof(op), op.Code, "No JavaScript for this opcode."),
        };

        text.Append(statement).Append('\n');
    }

    /// <summary>
    /// A two-operand op that answers from its last evaluation when both operands are the
    /// same, down to a zero's sign. A power's operands are usually a pitch and a constant,
    /// which hold for thousands of evaluations, and a script's <c>**</c> is dear.
    /// </summary>
    /// <remarks>
    /// Kept in a typed array rather than in variables, where every double stored would
    /// be a heap allocation.
    /// </remarks>
    private static string Remembered(ref int remembered, Op op, string expression)
    {
        var at = remembered;
        remembered += 3;

        var a = $"r{op.A}";
        var b = $"r{op.B}";

        return $"if ({a} === M[{at}] && {b} === M[{at + 1}] && ({a} !== 0 || Object.is({a}, M[{at}]))) r{op.Out} = M[{at + 2}]; "
            + $"else {{ M[{at}] = {a}; M[{at + 1}] = {b}; r{op.Out} = M[{at + 2}] = {expression}; }}";
    }

    /// <summary>The values the script's constants read, in the order of the ops that load them.</summary>
    public static IEnumerable<double> Constants(CompiledPatch program) =>
        program.Ops.Where(op => op.Code == OpCode.Const).Select(op => (double)op.K);

    /// <summary>A number the way the script reads it back as the same double.</summary>
    private static string Number(float value) => double.IsNaN(value)
        ? "NaN"
        : double.IsPositiveInfinity(value)
            ? "Infinity"
            : double.IsNegativeInfinity(value)
                ? "-Infinity"
                : $"({((double)value).ToString("R", CultureInfo.InvariantCulture)})";

    /// <summary>The registers something outside the ops reads: the speakers' left and right.</summary>
    private static HashSet<int> Outputs(CompiledPatch program) =>
        [program.OutputBase, program.OutputWidth > 1 ? program.OutputBase + 1 : program.OutputBase];

    private sealed record Stretch(
        int From,
        int To,
        SortedSet<int> Locals,
        HashSet<int> Loads,
        SortedSet<int> Stores,
        int[] Lines,
        int[] Cells,
        int[] Constants);

    /// <summary>
    /// Cuts the ops into stretches and works out, for each, which registers come in
    /// from the bank and which go back to it.
    /// </summary>
    /// <remarks>
    /// A register comes in when the stretch reads it before writing it, which is
    /// either an earlier stretch's or the evaluation before's. It goes back when
    /// the stretch writes it and anything else may read it: another stretch, this
    /// one before its write on the next evaluation, or the speakers.
    /// </remarks>
    private static List<Stretch> Chunks(Op[] ops, HashSet<int> outputs)
    {
        var readBy = new Dictionary<int, HashSet<int>>();
        var bounds = new List<(int From, int To)>();

        for (var from = 0; from < ops.Length; from += ChunkSize)
            bounds.Add((from, Math.Min(from + ChunkSize, ops.Length)));

        if (bounds.Count == 0) bounds.Add((0, 0));

        for (var s = 0; s < bounds.Count; s++)
            for (var i = bounds[s].From; i < bounds[s].To; i++)
                foreach (var register in Reads(ops[i]))
                {
                    if (!readBy.TryGetValue(register, out var stretches)) readBy[register] = stretches = [];
                    stretches.Add(s);
                }

        var stretchesOut = new List<Stretch>();
        var line = 0;
        var cell = 0;
        var constant = 0;

        for (var s = 0; s < bounds.Count; s++)
        {
            var (from, to) = bounds[s];
            var locals = new SortedSet<int>();
            var loads = new HashSet<int>();
            var written = new HashSet<int>();
            var readEarly = new HashSet<int>();
            var lines = new int[to - from];
            var cells = new int[to - from];
            var constants = new int[to - from];

            for (var i = from; i < to; i++)
            {
                var op = ops[i];

                foreach (var register in Reads(op))
                {
                    locals.Add(register);

                    if (written.Contains(register)) continue;

                    loads.Add(register);
                    readEarly.Add(register);
                }

                foreach (var register in Writes(op))
                {
                    locals.Add(register);
                    written.Add(register);
                }

                lines[i - from] = op.Code is OpCode.Delay or OpCode.Allpass ? line++ : -1;
                cells[i - from] = op.Code is OpCode.Phase ? cell++ : -1;
                constants[i - from] = op.Code is OpCode.Const ? constant++ : -1;
            }

            var stores = new SortedSet<int>(written.Where(register =>
                outputs.Contains(register)
                || readEarly.Contains(register)
                || (readBy.TryGetValue(register, out var by) && by.Any(other => other != s))));

            stretchesOut.Add(new Stretch(from, to, locals, loads, stores, lines, cells, constants));
        }

        return stretchesOut;
    }

    private static IEnumerable<int> Reads(Op op)
    {
        var inputs = OpShape.Inputs(op.Code);

        if (inputs > 0) yield return op.A;
        if (inputs > 1) yield return op.B;
        if (inputs > 2) yield return op.C;
    }

    private static IEnumerable<int> Writes(Op op)
    {
        for (var i = 0; i < OpShape.Outputs(op.Code); i++) yield return op.Out + i;
    }

    /// <summary>
    /// The interpreter's helpers, <see cref="DelayState"/>'s and <see cref="Noise"/>'s,
    /// written again, with the layout's constants. Each keeps the original's order of
    /// operations, which is what keeps its doubles the same.
    /// </summary>
    private const string Prelude =
        """
        (function (m) {
        "use strict";
        let F32, F64, I32, U8;
        const OS = m.oversample, INNER = 1 / (m.sampleRate * m.oversample), OUTER = 1 / m.sampleRate;
        const RATE = m.sampleRate * m.oversample;
        const LIVE = m.live, LIVEN = m.liveCount;
        const POS = m.positions, PHASES = m.phases, PREVIOUS = m.previous, RUNNING = m.running;
        const UNITS = m.units, PLANES = m.planes, TH = m.traceHeads, TL = m.traceLength;
        const K = Float64Array.from(m.constants, Number);
        const JUST_BELOW_ONE = 0.99999999999999989;
        const H = new Float64Array(3);

        const guard = v => Number.isFinite(v) ? v : 0;
        const signum = v => v > 0 ? 1 : v < 0 ? -1 : 0;
        const clamp = (v, lo, hi) => v < lo ? lo : v > hi ? hi : v;
        const feedback = v => Number.isFinite(v) ? clamp(v, -0.99, 0.99) : 0;
        const index = (i, n) => { i %= n; return i < 0 ? i + n : i; };

        function fract(v) {
          const f = v - Math.floor(v);
          return f < 1 ? f : JUST_BELOW_ONE;
        }

        const divide = (a, b) => b === 0 ? 0 : guard(a / b);
        const modulo = (a, b) => b === 0 ? 0 : guard(a - b * Math.floor(a / b));

        // Math.Pow's answers where a script's differ: one to any power, and minus one to an infinite one.
        const pow = (a, b) => a === 1 ? 1 : a === -1 && (b === Infinity || b === -Infinity) ? 1 : a ** b;

        function smoothstep(e0, e1, x) {
          if (e0 === e1) return x < e0 ? 0 : 1;
          const t = clamp((x - e0) / (e1 - e0), 0, 1);
          return t * t * (3 - 2 * t);
        }

        function hsv(h, s, v) {
          h = fract(h) * 6;
          s = clamp(s, 0, 1);
          const sector = Math.trunc(h), f = h - sector;
          const p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
          switch (sector) {
            case 0: H[0] = v; H[1] = t; H[2] = p; break;
            case 1: H[0] = q; H[1] = v; H[2] = p; break;
            case 2: H[0] = p; H[1] = v; H[2] = t; break;
            case 3: H[0] = p; H[1] = q; H[2] = v; break;
            case 4: H[0] = t; H[1] = p; H[2] = v; break;
            default: H[0] = v; H[1] = p; H[2] = q; break;
          }
        }

        function hash(x, y, z) {
          let h = ((Math.imul(x, 374761393) + Math.imul(y, 668265263) + Math.imul(z, 1274126177)) | 0) >>> 0;
          h = Math.imul(h ^ (h >>> 13), 1274126177) >>> 0;
          h = (h ^ (h >>> 16)) >>> 0;
          return (h & 0xFFFFFF) * (1 / 0xFFFFFF);
        }

        const fade = t => t * t * (3 - 2 * t);
        const lerp = (a, b, t) => a + (b - a) * t;

        function noise3(x, y, z) {
          if (!Number.isFinite(x) || !Number.isFinite(y) || !Number.isFinite(z)) return 0;
          const xf = Math.floor(x), yf = Math.floor(y), zf = Math.floor(z);
          const xi = xf | 0, yi = yf | 0, zi = zf | 0;
          const u = fade(x - xf), v = fade(y - yf), w = fade(z - zf);
          const z0 = lerp(
            lerp(hash(xi, yi, zi), hash(xi + 1 | 0, yi, zi), u),
            lerp(hash(xi, yi + 1 | 0, zi), hash(xi + 1 | 0, yi + 1 | 0, zi), u),
            v);
          const z1 = lerp(
            lerp(hash(xi, yi, zi + 1 | 0), hash(xi + 1 | 0, yi, zi + 1 | 0), u),
            lerp(hash(xi, yi + 1 | 0, zi + 1 | 0), hash(xi + 1 | 0, yi + 1 | 0, zi + 1 | 0), u),
            v);
          return lerp(z0, z1, w);
        }

        function table(base, length, rate, seconds) {
          if (!Number.isFinite(seconds) || length === 0) return 0;
          const position = seconds * rate;
          if (position < 0 || position >= length) return 0;
          const whole = Math.trunc(position), fraction = position - whole;
          const first = F32[base + whole], second = whole + 1 < length ? F32[base + whole + 1] : 0;
          return first + Math.fround(second - first) * fraction;
        }

        function readLine(base, length, slot, seconds, maximum) {
          if (!Number.isFinite(seconds)) seconds = 0;
          let samples = clamp(seconds, 0, maximum) * RATE;
          samples = Math.min(samples, length - 2);
          const whole = Math.trunc(samples), fraction = samples - whole;
          const first = index(I32[POS + slot] - whole, length), second = index(first - 1, length);
          const a = F32[base + first];
          return a + Math.fround(F32[base + second] - a) * fraction;
        }

        function writeLine(base, length, slot, value) {
          const next = index(I32[POS + slot] + 1, length);
          const narrow = Math.fround(value);
          F32[base + next] = Number.isFinite(narrow) && Math.abs(narrow) >= 1.1754943508222875e-38 ? clamp(narrow, -16, 16) : 0;
          I32[POS + slot] = next;
        }

        const bounded = v => Number.isFinite(v) && Math.abs(v) >= 2.2250738585072014e-308 ? clamp(v, -16, 16) : 0;

        function advance(cell, input, frequency) {
          if (!Number.isFinite(input)) input = F64[PREVIOUS + cell];
          if (!Number.isFinite(frequency)) frequency = 0;
          let step = U8[RUNNING + cell] !== 0 ? (input - F64[PREVIOUS + cell]) * frequency : 0;
          F64[PREVIOUS + cell] = input;
          U8[RUNNING + cell] = 1;
          if (!Number.isFinite(step)) step = 0;
          let next = F64[PHASES + cell] + step;
          next -= Math.floor(next);
          return F64[PHASES + cell] = Number.isFinite(next) ? next : 0;
        }

        """;
}
