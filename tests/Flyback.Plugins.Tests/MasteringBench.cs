using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;

namespace Flyback.Plugins.Tests;

/// <summary>
/// Plays a pair of signals through one Mastering module, sample by sample with
/// real state behind it: 'left' is Coordinates' x and 'right' its y, and two of
/// the module's outputs are what the Output's left and right hear.
/// </summary>
internal static class MasteringBench
{
    public const int Rate = GlobalConstants.SampleRate;

    public static readonly ModuleCatalog Catalog = ShippedPlugins.Loaded.Modules;

    /// <param name="into">Which of the module's inputs x and y are patched into; -1 leaves one out.</param>
    /// <param name="heard">Which of the module's outputs reach the Output's left and right.</param>
    public static (float[] Left, float[] Right) Play(
        string type,
        float[] x,
        float[]? y = null,
        (int X, int Y)? into = null,
        (int Left, int Right)? heard = null,
        params (int Port, float Value)[] knobs)
    {
        var patch = Wired(type, into ?? (0, y is null ? -1 : 1), heard ?? (0, 1), NodeCatalog.OutputLeftPort, knobs);

        var result = patch.CompileForAudio(Catalog);
        result.HasErrors.ShouldBeFalse(string.Join("; ", result.Issues.Select(i => i.Message)));

        var program = result.Program;
        var state = new DelayState(program.DelayLengths, Rate, program.PhaseCount, program.UnitCount);
        var registers = program.AllocateRegisters();
        var left = new float[x.Length];
        var right = new float[x.Length];

        for (var i = 0; i < x.Length; i++)
        {
            program.Evaluate(x[i], y?[i] ?? 0f, i / (double)Rate, registers, default, state);
            left[i] = (float)registers[program.OutputBase];
            right[i] = (float)registers[program.OutputBase + 1];
        }

        return (left, right);
    }

    /// <summary>What the module draws, pixel by pixel, where there is no memory at all.</summary>
    public static float[] Picture(string type, float[] x, params (int Port, float Value)[] knobs)
    {
        var patch = Wired(type, (0, -1), (0, 1), NodeCatalog.OutputColorPort, knobs);
        var program = patch.CompileForVideo(Catalog).Program;
        var registers = program.AllocateRegisters();

        return
        [
            .. x.Select(value =>
            {
                program.Evaluate(value, 0f, 0f, registers, default);
                return (float)registers[program.OutputBase];
            }),
        ];
    }

    private static Patch Wired(
        string type, (int X, int Y) into, (int Left, int Right) heard, int port, (int Port, float Value)[] knobs)
    {
        var b = new PatchBuilder(Catalog);

        var coord = b.Add(NodeCatalog.CoordTypeId);
        var module = b.Add(type, knobs);
        var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        b.Wire(coord, 0, module, into.X);
        if (into.Y >= 0) b.Wire(coord, 1, module, into.Y);

        b.Wire(module, heard.Left, sink, port);
        if (port == NodeCatalog.OutputLeftPort) b.Wire(module, heard.Right, sink, NodeCatalog.OutputRightPort);

        return b.Patch;
    }

    public static float[] Sine(double hertz, float amplitude, double seconds, double phase = 0d) =>
        [.. Enumerable.Range(0, (int)(seconds * Rate)).Select(i => amplitude * (float)Math.Sin(2d * Math.PI * hertz * i / Rate + phase))];

    public static float[] Hold(float value, double seconds) => [.. Enumerable.Repeat(value, (int)(seconds * Rate))];

    /// <summary>
    /// A sine's amplitude over its second half, once a filter has settled. From
    /// the RMS rather than the largest sample, since a tone near the top of the
    /// band is sampled well away from its peaks.
    /// </summary>
    public static float Settled(float[] signal)
    {
        var tail = signal.Skip(signal.Length / 2).ToArray();

        return (float)Math.Sqrt(2d * tail.Average(s => (double)s * s));
    }

    public static double Decibels(double ratio) => 20d * Math.Log10(ratio);
}
