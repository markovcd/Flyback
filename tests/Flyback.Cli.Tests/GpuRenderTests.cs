using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Flyback.Engine.Render;
using Flyback.Gpu;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary>
/// The GPU's picture against the processor's, drawn headless the way
/// <c>flyback-cli render</c> draws it.
/// </summary>
/// <remarks>
/// CI draws these on Mesa's llvmpipe through EGL with no surface. A machine with no
/// OpenGL at all skips them and says why. The bounds are ADR-0035's: the two may
/// differ in their last bits, and a frame upside down, a channel swapped or a
/// history a frame behind differs by far more than that.
/// </remarks>
public class GpuRenderTests
{
    private const int Width = 160;
    private const int Height = 90;

    /// <summary>Plain, two kinds of feedback, a picture, a Meter's reading, the widest spread and the largest shader.</summary>
    public static TheoryData<string> Drawn() =>
        ["Plasma", "Feedback tunnel", "Trails", "Picture in", "Heard", "Kaleidoscope", "Whole band"];

    [Theory]
    [MemberData(nameof(Drawn))]
    public void The_GPU_draws_a_preset_as_the_processor_does(string name) =>
        Agrees(Presets.All.Single(p => p.Name == name).Build(NodeCatalog.BuiltIn));

    /// <summary>A value carried round per pixel, which on the GPU is a float target ping-ponged beside the history.</summary>
    [Fact]
    public void The_GPU_carries_a_plane_as_the_processor_does()
    {
        var patch = Presets.All.Single(p => p.Name == "Empty").Build(NodeCatalog.BuiltIn);
        var add = NodeInstance.Create(NodeCatalog.Require("math.add"), 0, 0);
        add.InputValues[1] = 0.25f;

        patch.Nodes.Add(add);
        patch.Connect(add.Id, 0, add.Id, 0);
        patch.Connect(add.Id, 0, patch.Output.Id, NodeCatalog.OutputColorPort);

        Agrees(patch);
    }

    /// <summary>A Scope's chart, its buffer filled as the sound would fill it, which the shader reads as a texture.</summary>
    [Fact]
    public void The_GPU_draws_a_Scope_as_the_processor_does()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var sine = b.Add("osc.sine", 0, 0);
        var scope = b.Add(NodeCatalog.ScopeTypeId, 100, 0);
        var output = b.Add(NodeCatalog.OutputTypeId, 200, 0);

        b.Wire(sine, 0, scope, 0);
        b.Wire(sine, 0, output, NodeCatalog.OutputLeftPort);
        b.Wire(scope, 0, output, NodeCatalog.OutputColorPort);

        var program = b.Patch.CompileForVideo(NodeCatalog.BuiltIn).Program;
        var buffer = program.Taps.ShouldHaveSingleItem().Trace.Samples;

        for (var i = 0; i < buffer.Length; i++) buffer[i] = MathF.Sin(i * 0.037f) * 0.8f;

        Agrees(program);
    }

    /// <summary>A Beam's phosphor, drawn from a circle the sound played, which the shader reads as two squares of texels.</summary>
    [Fact]
    public void The_GPU_draws_a_Beam_as_the_processor_does()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var across = b.Add("osc.sine", 0, 0, (1, 100f), (3, 0.5f));
        var up = b.Add("osc.sine", 0, 100, (1, 100f), (2, 0.25f), (3, 0.5f));
        var beam = b.Add(NodeCatalog.BeamTypeId, 100, 0);
        var output = b.Add(NodeCatalog.OutputTypeId, 200, 0);

        b.Wire(across, 0, beam, 0);
        b.Wire(up, 0, beam, 1);
        b.Wire(beam, 0, output, NodeCatalog.OutputColorPort);

        var program = b.Patch.CompileForVideo(NodeCatalog.BuiltIn).Program;
        var heard = b.Patch.CompileForAudio(NodeCatalog.BuiltIn).Program;

        var speaker = new AudioRenderer();
        speaker.Render(heard, new float[speaker.SampleRate / 4 * 2]);
        Traces.Refresh(program, heard, speaker.Memory);

        program.Taps.ShouldHaveSingleItem().Trace.Samples.Max().ShouldBeGreaterThan(0f);

        Agrees(program);
    }

    /// <summary>A clip played into the picture, longer than a row of its texture, so the read wraps onto the next.</summary>
    [Fact]
    public void The_GPU_draws_a_Sample_as_the_processor_does()
    {
        var folder = Directory.CreateTempSubdirectory("flyback-gpu-sample").FullName;

        try
        {
            var samples = new float[GlslEmitter.TableRow * 3 + 17];
            for (var i = 0; i < samples.Length; i++) samples[i] = MathF.Sin(i * 0.013f);

            var path = Path.Combine(folder, "clip.wav");
            using (var file = File.Create(path)) WavWriter.Write(file, samples, 8000, 1);

            var b = new PatchBuilder(NodeCatalog.BuiltIn);
            var player = b.Add(NodeCatalog.SampleTypeId, 0, 0);
            SampleExtra.Set(player, path);
            var output = b.Add(NodeCatalog.OutputTypeId, 200, 0);
            b.Wire(player, 0, output, NodeCatalog.OutputColorPort);

            var program = b.Patch.CompileForVideo(NodeCatalog.BuiltIn, new SampleLibrary { Beside = folder }).Program;
            program.Tables.ShouldHaveSingleItem();

            Agrees(program);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static void Agrees(Patch patch) => Agrees(patch.CompileForVideo(NodeCatalog.BuiltIn).Program);

    /// <summary>Four frames a thirtieth apart on each, so a loop has three frames behind it to disagree about.</summary>
    private static void Agrees(CompiledPatch program)
    {
        using var gpu = HeadlessRenderer.Open(out var why);
        Assert.SkipWhen(gpu is null, $"No GPU here. {why}");

        IlCompiler.CompileOnce(program, IlParts.Staged);

        gpu.Prepare(program).ShouldBeNull();

        IFrameRenderer processor = new SynthRenderer();
        var drawn = new byte[Width * Height * 4];
        var expected = new byte[Width * Height * 4];

        for (var frame = 0; frame < 4; frame++)
        {
            var time = 1d + frame / 30d;
            gpu.Render(program, time, Width, Height, drawn, Width * 4);
            processor.Render(program, time, Width, Height, expected, Width * 4);
        }

        long sum = 0;
        var worst = 0;

        for (var i = 0; i < drawn.Length; i++)
        {
            var difference = Math.Abs(drawn[i] - expected[i]);
            sum += difference;
            worst = Math.Max(worst, difference);
        }

        var mean = sum / (double)drawn.Length;

        worst.ShouldBeLessThanOrEqualTo(8, $"on {gpu.Description}");
        mean.ShouldBeLessThanOrEqualTo(0.5d, $"on {gpu.Description}");
    }
}
