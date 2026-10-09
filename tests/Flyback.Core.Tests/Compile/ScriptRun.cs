using System.Globalization;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Render;
using Shouldly;

namespace Flyback.Core.Tests.Compile;

/// <summary>
/// A program played by the interpreter and by what <see cref="JsEmitter"/> writes, run
/// under Node on a heap of its own, for the engine's presets here and the plugins' in
/// Flyback.Plugins.Tests.
/// </summary>
/// <remarks>
/// A script's <c>Math.sin</c>, <c>exp</c> and the rest may round their last bit
/// differently from .NET's, so a sample may be a hair off; everything else is the
/// interpreter's arithmetic in the interpreter's order and lands on the same float.
/// </remarks>
internal static class ScriptRun
{
    /// <summary>A hair, against a signal whose steps a speaker hears are a thirty-thousandth of full scale.</summary>
    public const float Hair = 1e-5f;

    public const int SampleRate = 48_000;
    public const int Oversample = 4;

    /// <summary>
    /// What the interpreter makes of <paramref name="frames"/> frames, at the times the script
    /// makes them, playing <paramref name="turned"/>'s program on from its frame where given.
    /// </summary>
    public static float[] Interpret(CompiledPatch program, int frames, (CompiledPatch Program, int At)? turned = null, float[]? input = null)
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
    public static float[] Script(CompiledPatch program, int frames, (CompiledPatch Program, int At)? turned = null, float[]? input = null)
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

    public static void ShouldMatch(float[] expected, float[] actual, string where)
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
