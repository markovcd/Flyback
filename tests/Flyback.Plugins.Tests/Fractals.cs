using System.Text.Json.Nodes;
using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Shouldly;

namespace Flyback.Plugins.Tests;

/// <summary>
/// What the Fractals tests share: a module wired alone to the Output, read at a
/// point of the screen or played sample by sample.
/// </summary>
internal static class Fractals
{
    public const string Mandelbrot = "flyback.fractals.mandelbrot";
    public const string Julia = "flyback.fractals.julia";
    public const string Orbit = "flyback.fractals.orbit";

    /// <summary>The sockets Mandelbrot and Julia share.</summary>
    public const int Re = 2;
    public const int Im = 3;
    public const int Zoom = 4;
    public const int Shift = 5;

    /// <summary>The readings Mandelbrot and Julia share.</summary>
    public const int Color = 0;
    public const int Escape = 1;
    public const int Inside = 2;

    public const int Rate = GlobalConstants.SampleRate;

    public static ModuleCatalog Catalog => ShippedPlugins.Loaded.Modules;

    /// <summary>A module of <paramref name="typeId"/> with its knobs set and, where given, its iteration count.</summary>
    public static NodeInstance Module(string typeId, int iterations = 0, params (int Port, float Value)[] knobs)
    {
        var node = NodeInstance.Create(Catalog.Require(typeId), 0, 0);

        foreach (var (port, value) in knobs) node.InputValues[port] = value;

        if (iterations > 0) node.SetState("escape", new JsonObject { ["iterations"] = iterations.ToString() });

        return node;
    }

    /// <summary>The screen's program with <paramref name="node"/>'s output <paramref name="port"/> on the Output's color.</summary>
    public static CompiledPatch Seen(NodeInstance node, int port) =>
        Wired(node, port, NodeCatalog.OutputColorPort).CompileForVideo(Catalog).Program;

    /// <summary>The speakers' program with <paramref name="node"/>'s output <paramref name="port"/> on the Output's left.</summary>
    public static CompiledPatch Heard(NodeInstance node, int port) =>
        Wired(node, port, NodeCatalog.OutputLeftPort).CompileForAudio(Catalog).Program;

    /// <summary>One scalar output as a function of where on the screen it is read.</summary>
    public static Func<float, float, double> Field(string typeId, int port, int iterations, params (int Port, float Value)[] knobs)
    {
        var program = Seen(Module(typeId, iterations, knobs), port);
        var registers = program.AllocateRegisters();

        return (x, y) =>
        {
            program.Evaluate(x, y, 0d, registers, default);
            return registers[program.OutputBase];
        };
    }

    /// <summary>The color output read at one point of the screen.</summary>
    public static (double R, double G, double B) ColorAt(CompiledPatch program, float x, float y)
    {
        var registers = program.AllocateRegisters();

        program.Evaluate(x, y, 0d, registers, default);

        return (registers[program.OutputBase], registers[program.OutputBase + 1], registers[program.OutputBase + 2]);
    }

    /// <summary><paramref name="seconds"/> of the speakers' program, sample by sample.</summary>
    public static float[] Play(CompiledPatch program, double seconds)
    {
        var state = new DelayState(program, Rate);
        var registers = program.AllocateRegisters();
        var output = new float[(int)(seconds * Rate)];

        for (var i = 0; i < output.Length; i++)
        {
            program.Evaluate(0d, 0d, i / (double)Rate, registers, default, state);
            output[i] = (float)registers[program.OutputBase];
        }

        return output;
    }

    /// <summary>Points across the picture, off the axes so none is a special case.</summary>
    public static IEnumerable<(float X, float Y)> Grid()
    {
        for (var i = 0; i < 9; i++)
        for (var j = 0; j < 9; j++)
            yield return (-1.7f + i * 0.41f, -0.95f + j * 0.23f);
    }

    /// <summary>The shader's complaint, which it has no other way to make.</summary>
    public static void LowersToEveryDialect(CompiledPatch program)
    {
        program.Tables.Count.ShouldBe(0);

        foreach (var dialect in Enum.GetValues<GlslDialect>())
            GlslEmitter.Emit(program, dialect).PatchFragment.ShouldNotBeNullOrEmpty();
    }

    private static Patch Wired(NodeInstance node, int port, int into)
    {
        var patch = new Patch();
        patch.Nodes.Add(node);

        var sink = NodeInstance.Create(Catalog.Require(NodeCatalog.OutputTypeId), 0, 0);
        sink.InputValues[NodeCatalog.OutputVolumePort] = 1f;
        patch.Nodes.Add(sink);

        patch.Connect(node.Id, port, sink.Id, into);

        return patch;
    }
}
