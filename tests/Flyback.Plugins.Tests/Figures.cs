using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;

namespace Flyback.Plugins.Tests;

/// <summary>
/// What the Figures tests share: a module wired alone to the Output with
/// Coordinates' x on its trigger, run on the speakers' program sample by sample
/// or on the screen's program frame by frame at one pixel.
/// </summary>
internal static class Figures
{
    public const string Plate = "flyback.figures.plate";
    public const string Harmonograph = "flyback.figures.harmonograph";
    public const string Overtones = "flyback.figures.overtones";

    public const int Rate = GlobalConstants.SampleRate;

    /// <summary>Sixty frames a second, which is what the pen's segments are measured against.</summary>
    public const double Frame = 1d / 60d;

    public static ModuleCatalog Catalog => ShippedPlugins.Loaded.Modules;

    /// <summary>
    /// The module with one of Coordinates' outputs, x unless <paramref name="triggerFrom"/>
    /// says otherwise, wired to <paramref name="triggerPort"/>, and its output
    /// <paramref name="from"/> into the Output's <paramref name="into"/>.
    /// </summary>
    /// <remarks>
    /// A picture test that wants to strike without moving the pixel takes the trigger
    /// from Coordinates' aspect, which <see cref="Pixel.Frame"/> hands in by itself.
    /// </remarks>
    public static Patch Wired(
        string typeId,
        int from,
        int into,
        int? triggerPort = 0,
        int triggerFrom = NodeCatalog.CoordXPort,
        params (int Port, float Value)[] knobs)
    {
        var patch = new Patch();

        var module = NodeInstance.Create(Catalog.Require(typeId), 0, 0);
        foreach (var (port, value) in knobs) module.InputValues[port] = value;

        var sink = NodeInstance.Create(Catalog.Require(NodeCatalog.OutputTypeId), 0, 0);
        sink.InputValues[NodeCatalog.OutputVolumePort] = 1f;

        patch.Nodes.Add(module);
        patch.Nodes.Add(sink);

        if (triggerPort is { } trigger)
        {
            var coord = NodeInstance.Create(Catalog.Require("coord"), 0, 0);
            patch.Nodes.Add(coord);
            patch.Connect(coord.Id, triggerFrom, module.Id, trigger);
        }

        patch.Connect(module.Id, from, sink.Id, into);

        return patch;
    }

    /// <summary>
    /// The speakers' program run for <paramref name="trigger"/>'s length, the trigger
    /// arriving on x, and what came out of the Output's left.
    /// </summary>
    public static float[] Heard(Patch patch, float[] trigger)
    {
        var program = patch.CompileForAudio(Catalog).Program;
        var state = new DelayState(program, Rate);
        var registers = program.AllocateRegisters();
        var output = new float[trigger.Length];

        for (var i = 0; i < trigger.Length; i++)
        {
            program.Evaluate(trigger[i], 0f, i / (double)Rate, registers, default, state);
            output[i] = (float)registers[program.OutputBase];
        }

        return output;
    }

    /// <summary>
    /// The screen's program run frame by frame at one pixel, whose planes are kept
    /// between frames the way the renderer keeps them, the trigger arriving on x.
    /// </summary>
    public sealed class Pixel
    {
        private readonly CompiledPatch program;
        private readonly float[] planes;
        private double clock;

        public Pixel(Patch patch)
        {
            program = patch.CompileForVideo(Catalog).Program;
            planes = new float[program.PlaneCount];
        }

        public int Planes => program.PlaneCount;

        /// <summary>Draws the next frame at (<paramref name="x"/>, <paramref name="y"/>) and returns the Output's first channel.</summary>
        /// <param name="aspect">The screen's aspect, which a patch wired from it reads as its trigger.</param>
        public double Frame(double x, double y, double aspect = 16d / 9d)
        {
            var registers = program.AllocateRegisters();

            program.Evaluate(x, y, clock, registers, default, aspect: aspect, planes: planes);
            clock += Figures.Frame;

            return registers[program.OutputBase];
        }

        public void Skip(int frames) => clock += frames * Figures.Frame;
    }

    /// <summary>High for the first <paramref name="high"/> samples, then low.</summary>
    public static float[] Pulse(int high, int length) =>
        [.. Enumerable.Range(0, length).Select(i => i < high ? 1f : 0f)];

    public static double Rms(ReadOnlySpan<float> samples)
    {
        var sum = 0d;
        foreach (var s in samples) sum += s * (double)s;
        return Math.Sqrt(sum / samples.Length);
    }

    /// <summary>The shader's complaint, which it has no other way to make.</summary>
    public static void LowersToEveryDialect(CompiledPatch program)
    {
        program.UnitCount.ShouldBe(0, "a Figures module keeps no cell: what it remembers is in planes");
        program.Ops.Any(op => op.Code is OpCode.Table or OpCode.UnitRead).ShouldBeFalse();

        foreach (var dialect in Enum.GetValues<GlslDialect>())
            GlslEmitter.Emit(program, dialect).PatchFragment.ShouldNotBeNullOrEmpty();
    }
}
