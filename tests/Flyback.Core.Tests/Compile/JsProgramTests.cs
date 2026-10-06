using System.Globalization;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Flyback.Engine.Render;
using Shouldly;

namespace Flyback.Core.Tests.Compile;

/// <summary>
/// What <see cref="JsEmitter"/> writes, run under Node on a heap of its own: the
/// interpreter's sound by another route, which is what a browser plays.
/// </summary>
/// <remarks>
/// A script's <c>Math.sin</c>, <c>exp</c> and the rest may round their last bit
/// differently from .NET's, so a sample may be a hair off; everything else is the
/// interpreter's arithmetic in the interpreter's order and lands on the same float.
/// Skipped where there is no Node.
/// </remarks>
public class JsProgramTests
{
    /// <summary>A hair, against a signal whose steps a speaker hears are a thirty-thousandth of full scale.</summary>
    private const float Hair = 1e-5f;

    private const int SampleRate = 48_000;
    private const int Oversample = 4;

    public static TheoryData<OpCode> AllOpCodes => [.. Enum.GetValues<OpCode>()];

    public static TheoryData<string> AllPresets => [.. Presets.All.Select(p => p.Name)];

    /// <summary>
    /// Every preset's sound, each side keeping memory of its own, for three thousand
    /// frames, so a delay line or accumulator numbered differently shows up as every
    /// sample after it disagreeing.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllPresets))]
    public void The_sound_is_the_interpreters(string name)
    {
        Assert.SkipWhen(NodeJs.Path is null, "no Node on this machine");

        var program = Presets.All.Single(p => p.Name == name).Build(NodeCatalog.Current).CompileForAudio().Program;

        ShouldMatch(Interpret(program, 3_000), Script(program, 3_000), name);
    }

    /// <summary>A knob turned is a new value in the layout and the same script, so the engine keeps what it optimized.</summary>
    [Fact]
    public void A_knob_turned_leaves_the_script_as_it_was()
    {
        var (before, after) = SineTurned();

        JsEmitter.Emit(after).ShouldBe(JsEmitter.Emit(before));
        JsEmitter.Constants(after).ShouldNotBe(JsEmitter.Constants(before));
        JsEmitter.Constants(after).ShouldContain(331d);
    }

    /// <summary>A script handed a turned knob's constants as it plays sounds as the interpreter playing the turned program on.</summary>
    [Fact]
    public void A_script_retuned_as_it_plays_is_the_turned_program_played_on()
    {
        Assert.SkipWhen(NodeJs.Path is null, "no Node on this machine");

        var (before, after) = SineTurned();

        ShouldMatch(Interpret(before, 1_000, (after, 400)), Script(before, 1_000, (after, 400)), "retuned");
    }

    /// <summary>A Line In's two inputs are written into the script once a frame, from what the renderer hears, as the interpreter's renderer writes them.</summary>
    [Fact]
    public void A_line_in_is_heard_a_frame_at_a_time()
    {
        Assert.SkipWhen(NodeJs.Path is null, "no Node on this machine");

        var builder = new PatchBuilder();
        var line = builder.Add(NodeCatalog.LineInTypeId, 0, 0, (0, 0.8f));
        var sink = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputVolumePort, 1f));
        builder.Wire(line, 0, sink, NodeCatalog.OutputLeftPort).Wire(line, 1, sink, NodeCatalog.OutputRightPort);

        var program = builder.Patch.CompileForAudio(NodeCatalog.BuiltIn).Program;
        var heard = new float[2_000 * 2];

        for (var i = 0; i < 2_000; i++)
        {
            heard[i * 2] = 0.5f * MathF.Sin(i * 0.05f);
            heard[i * 2 + 1] = 0.25f * MathF.Cos(i * 0.031f);
        }

        var interpreted = Interpret(program, 1_000, input: heard);

        interpreted.ShouldContain(sample => Math.Abs(sample) > 0.1f, "the interpreter heard nothing, so agreeing with it proves nothing");
        ShouldMatch(interpreted, Script(program, 1_000, input: heard), "line in");
    }

    /// <summary>A sine heard on the left, before and after its frequency is turned from 220 to 331.</summary>
    private static (CompiledPatch Before, CompiledPatch After) SineTurned()
    {
        var b = new PatchBuilder(NodeCatalog.Current);
        var sine = b.Add("osc.sine", (1, 220f));
        var output = b.Add(NodeCatalog.OutputTypeId);
        b.Wire(sine, 0, output, NodeCatalog.OutputLeftPort);

        var before = b.Patch.CompileForAudio().Program;
        sine.InputValues[1] = 331f;

        return (before, b.Patch.CompileForAudio().Program);
    }

    /// <summary>Fails the day an opcode is added and the emitter is not told about it.</summary>
    [Theory]
    [MemberData(nameof(AllOpCodes))]
    public void Every_opcode_gives_the_interpreters_answer(OpCode code)
    {
        Assert.SkipWhen(NodeJs.Path is null, "no Node on this machine");

        var program = OneOp(code);

        ShouldMatch(Interpret(program, 8), Script(program, 8), code.ToString());
    }

    /// <summary>
    /// A program longer than one function is several, and each numbers its delay lines
    /// and accumulators from where the one before it stopped, and hands its registers on.
    /// </summary>
    [Fact]
    public void A_program_split_across_functions_keeps_its_memory_in_order()
    {
        Assert.SkipWhen(NodeJs.Path is null, "no Node on this machine");

        var ops = new List<Op> { new(OpCode.LoadT, 0), new(OpCode.Const, 1, k: 0.5f) };
        var last = 0;

        for (var i = 0; i < 400; i++)
        {
            var next = ops.Count + 1;
            ops.Add(new Op(OpCode.Const, next, k: i % 2 == 0 ? 1f + i % 7 : (1f + i % 5) * 1e-5f));
            ops.Add(i % 2 == 0
                ? new Op(OpCode.Phase, next + 1, 0, next, last)
                : new Op(OpCode.Delay, next + 1, last, 1, next, 1e-4f * (1 + i % 3)));
            last = next + 1;
        }

        var program = new CompiledPatch(
            [.. ops, new Op(OpCode.Copy, ops.Count + 1, last), new Op(OpCode.Sin, ops.Count + 2, last)],
            ops.Count + 3,
            ops.Count + 1,
            2);

        program.Ops.Length.ShouldBeGreaterThan(3 * JsEmitter.ChunkSize);

        ShouldMatch(Interpret(program, 500), Script(program, 500), "split");
    }

    [Fact]
    public void A_program_that_reads_a_picture_is_left_to_the_interpreter()
    {
        var picture = new LoadedImage(new float[2 * 2 * 3], 2, 2);
        var program = new CompiledPatch(
            [new Op(OpCode.Const, 0), new Op(OpCode.SamplePicture, 1, 0, 0, k: 0)],
            4,
            1,
            2,
            pictures: [picture]);

        JsEmitter.Emit(program).ShouldBeNull();
    }

    /// <summary>
    /// What the interpreter makes of <paramref name="frames"/> frames, at the times the script
    /// makes them, playing <paramref name="turned"/>'s program on from its frame where given.
    /// </summary>
    private static float[] Interpret(CompiledPatch program, int frames, (CompiledPatch Program, int At)? turned = null, float[]? input = null)
    {
        var memory = new AudioRenderer(oversample: Oversample).DelayMemoryFor(program);
        var registers = program.AllocateRegisters();
        var live = new LiveValues(program.LiveInputs);
        var lineLeft = live.IndexOf(LineInSignal.Left);
        var lineRight = live.IndexOf(LineInSignal.Right);
        var left = program.OutputBase;
        var right = program.OutputWidth > 1 ? program.OutputBase + 1 : program.OutputBase;
        var inner = 1.0 / (SampleRate * Oversample);
        var outer = 1.0 / SampleRate;
        var heard = new float[frames * Oversample * 2];
        var clock = 0d;
        var at = 0;

        for (var frame = 0; frame < frames; frame++)
        {
            var playing = turned is { } t && frame >= t.At ? t.Program : program;

            if (input is not null)
            {
                if (lineLeft >= 0) live.Storage[lineLeft] = input[frame * 2];
                if (lineRight >= 0) live.Storage[lineRight] = input[frame * 2 + 1];
            }

            for (var k = 0; k < Oversample; k++)
            {
                playing.Evaluate(0d, 0d, clock + k * inner, registers, default, memory, 1d, live);
                heard[at++] = (float)registers[left];
                heard[at++] = (float)registers[right];
            }

            clock += outer;
        }

        return heard;
    }

    /// <summary>
    /// What the emitted script makes of the same, under Node, on fresh memory of its own,
    /// retuned to <paramref name="turned"/>'s constants at its frame where given.
    /// </summary>
    private static float[] Script(CompiledPatch program, int frames, (CompiledPatch Program, int At)? turned = null, float[]? input = null)
    {
        var source = JsEmitter.Emit(program);
        source.ShouldNotBeNull();

        var folder = Directory.CreateTempSubdirectory("flyback-js-");

        try
        {
            var heap = new ScriptHeap();
            var memory = new AudioRenderer(oversample: Oversample).DelayMemoryFor(program);
            var live = new LiveValues(program.LiveInputs);
            var layout = JsLayout.Of(program, memory, live, SampleRate, Oversample, heap.Place);
            var count = frames * Oversample * 2;
            var output = heap.Floats(count);
            var heard = input is null ? 0 : heap.Place(input) / sizeof(float);

            var path = (string name) => Path.Combine(folder.FullName, name);

            heap.Save(path("heap.bin"));
            File.WriteAllText(path("layout.json"), layout);
            File.WriteAllText(path("program.js"), source);
            File.WriteAllText(path("run.mjs"), Runner);

            // The turned program's constants as JSON numbers, and the frame they are handed over at; none, and it is never retuned.
            var constants = turned is { } t ? $"[{string.Join(',', JsEmitter.Constants(t.Program).Select(k => k.ToString("R", CultureInfo.InvariantCulture)))}]" : "[]";
            File.WriteAllText(path("turned.json"), constants);

            NodeJs.Run(path("run.mjs"), path("heap.bin"), path("layout.json"), path("program.js"), path("out.bin"),
                $"{frames}", $"{output}", $"{count}", path("turned.json"), $"{turned?.At ?? frames}", $"{heard}");

            return ScriptHeap.ReadFloats(path("out.bin"), count);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private static void ShouldMatch(float[] expected, float[] actual, string where)
    {
        actual.Length.ShouldBe(expected.Length);

        var worst = 0f;
        var at = -1;

        for (var i = 0; i < expected.Length; i++)
        {
            var off = float.IsNaN(expected[i]) && float.IsNaN(actual[i]) ? 0f : Math.Abs(expected[i] - actual[i]);

            if (!(off <= worst)) (worst, at) = (off, i);
        }

        worst.ShouldBeLessThanOrEqualTo(Hair, $"{where}: evaluation {at / 2}, {(at % 2 == 0 ? "left" : "right")}: {(at >= 0 ? actual[at] : 0f):R} against {(at >= 0 ? expected[at] : 0f):R}");
    }

    /// <summary>Three operands and the op under test writing r3, with r3 and r4 heard as left and right.</summary>
    private static CompiledPatch OneOp(OpCode code) =>
        new(
            [
                new Op(OpCode.Const, 0, k: 0.25f),
                new Op(OpCode.Const, 1, k: 0.5f),
                new Op(OpCode.Const, 2, k: 0.75f),
                new Op(code, 3, 0, 1, 2, 1f),
            ],
            6,
            3,
            2);

    /// <summary>
    /// Loads the heap, makes the program, renders from time nought, retunes it at its frame
    /// and renders the rest, and writes the evaluations out.
    /// </summary>
    private const string Runner =
        """
        import { readFileSync, writeFileSync } from 'node:fs';

        const [heapFile, layoutFile, programFile, outFile, frames, out, count, turnedFile, at, input] = process.argv.slice(2);
        const saved = readFileSync(heapFile);
        const buffer = new ArrayBuffer(Number(saved.readBigInt64LE(0)));
        const bytes = new Uint8Array(buffer);

        for (let at = 8; at < saved.length;) {
          const offset = Number(saved.readBigInt64LE(at));
          const length = saved.readInt32LE(at + 8);
          bytes.set(saved.subarray(at + 12, at + 12 + length), offset);
          at += 12 + length;
        }

        const m = JSON.parse(readFileSync(layoutFile, 'utf8'));
        m.f32 = () => new Float32Array(buffer);
        m.f64 = () => new Float64Array(buffer);
        m.i32 = () => new Int32Array(buffer);
        m.u8 = () => bytes;

        const render = new Function(`return ${readFileSync(programFile, 'utf8')}`)()(m);
        const split = Math.min(Number(at), Number(frames));
        render(0, split, 1, Number(out), Number(input));

        if (split < Number(frames)) {
          render.retune(JSON.parse(readFileSync(turnedFile, 'utf8')));
          render(split / m.sampleRate, Number(frames) - split, 1, Number(out) + split * m.oversample * 2, Number(input) + split * 2);
        }

        writeFileSync(outFile, bytes.subarray(Number(out) * 4, (Number(out) + Number(count)) * 4));
        """;
}
