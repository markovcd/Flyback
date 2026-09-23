using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The Fade, read with its level on a Value. It is stateless, so no renderer is involved.
/// </summary>
public class FadeTests
{
    private const string FadeType = "flyback.voice.fade";

    private const int Out = 0;
    private const int Gate = 1;

    private const int InPort = 0;
    private const int LevelPort = 1;
    private const int FromPort = 2;
    private const int ToPort = 3;

    private static readonly ModuleCatalog Catalog = ShippedPlugins.Loaded.Modules;

    [Fact]
    public void The_voice_plugin_offers_it_under_timing()
    {
        var def = Catalog.Get(FadeType).ShouldNotBeNull();

        def.Name.ShouldBe("Fade");
        def.Category.ShouldBe(ModuleCategories.Timing);
        def.Sinks.ShouldBe(ModuleSinks.Both);
        def.Outputs.Select(p => p.Name).ShouldBe(["out", "gate"]);
        def.Inputs[InPort].Kind.ShouldBe(PortKind.Any);
        Catalog.ProviderOf(FadeType)!.Id.ShouldBe("flyback.voice");
        Catalog.All.Count(d => d.TypeId.Split('.')[^1] == "fade").ShouldBe(1);
    }

    [Fact]
    public void A_part_is_out_under_from_in_over_to_and_halfway_between()
    {
        Read(Out, 0.5f, 0.2f, 0.4f, 0.6f).ShouldBe(0f, 1e-6f);
        Read(Out, 0.5f, 0.5f, 0.4f, 0.6f).ShouldBe(0.25f, 1e-6f);
        Read(Out, 0.5f, 0.9f, 0.4f, 0.6f).ShouldBe(0.5f, 1e-6f);
    }

    [Fact]
    public void From_above_to_is_a_part_that_leaves_as_the_level_rises()
    {
        Read(Gate, 1f, 0.2f, 0.6f, 0.4f).ShouldBe(1f, 1e-6f);
        Read(Gate, 1f, 0.9f, 0.6f, 0.4f).ShouldBe(0f, 1e-6f);
    }

    [Fact]
    public void With_nothing_patched_in_out_is_the_gate()
    {
        foreach (var level in new[] { 0f, 0.45f, 0.5f, 0.58f, 1f })
        {
            var b = new PatchBuilder(Catalog);
            var song = b.Add("value", (0, level));
            var fade = b.Add(FadeType, (FromPort, 0.4f), (ToPort, 0.6f));
            var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

            b.Wire(song, 0, fade, LevelPort).Wire(fade, Out, sink, NodeCatalog.OutputLeftPort);

            Exact(b.Patch).ShouldBe(Exact(Wired(Gate, 1f, level, 0.4f, 0.6f)));
        }
    }

    [Fact]
    public void It_fades_a_picture_as_readily_as_a_voice()
    {
        var b = new PatchBuilder(Catalog);
        var orange = b.Add("color.rgb", (0, 1f), (1, 0.5f), (2, 0f));
        var fade = b.Add(FadeType, (LevelPort, 0.5f), (FromPort, 0.4f), (ToPort, 0.6f));
        var sink = b.Add(NodeCatalog.OutputTypeId);

        b.Wire(orange, 0, fade, InPort).Wire(fade, Out, sink, NodeCatalog.OutputColorPort);

        var program = b.Patch.CompileForVideo(Catalog).Program;
        var registers = program.AllocateRegisters();
        program.Evaluate(0f, 0f, 0d, registers, default);

        ((float)registers[program.OutputBase]).ShouldBe(0.5f, 1e-6f);
        ((float)registers[program.OutputBase + 1]).ShouldBe(0.25f, 1e-6f);
        ((float)registers[program.OutputBase + 2]).ShouldBe(0f, 1e-6f);
    }

    /// <summary>A Smoothstep into a Multiply, against this, equal rather than close.</summary>
    [Fact]
    public void It_is_the_two_modules_it_stands_for_to_the_last_bit()
    {
        foreach (var level in new[] { 0.41f, 0.4637f, 0.5f, 0.59f })
        {
            var b = new PatchBuilder(Catalog);
            var signal = b.Add("value", (0, 0.7311f));
            var song = b.Add("value", (0, level));
            var rises = b.Add("math.smoothstep", (0, 0.42f), (1, 0.48f));
            var product = b.Add("math.mul");
            var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

            b.Wire(song, 0, rises, 2).Wire(signal, 0, product, 0).Wire(rises, 0, product, 1)
             .Wire(product, 0, sink, NodeCatalog.OutputLeftPort);

            Exact(Wired(Out, 0.7311f, level, 0.42f, 0.48f)).ShouldBe(Exact(b.Patch));
        }
    }

    // --- harness -----------------------------------------------------------------

    private static float Read(int port, float signal, float level, float from, float to) =>
        (float)Exact(Wired(port, signal, level, from, to));

    private static Patch Wired(int port, float signal, float level, float from, float to)
    {
        var b = new PatchBuilder(Catalog);
        var source = b.Add("value", (0, signal));
        var song = b.Add("value", (0, level));
        var fade = b.Add(FadeType, (FromPort, from), (ToPort, to));
        var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        b.Wire(source, 0, fade, InPort).Wire(song, 0, fade, LevelPort)
         .Wire(fade, port, sink, NodeCatalog.OutputLeftPort);

        return b.Patch;
    }

    private static double Exact(Patch patch)
    {
        var program = patch.CompileForAudio(Catalog).Program;
        var registers = program.AllocateRegisters();

        program.Evaluate(0f, 0f, 0d, registers, default);
        return registers[program.OutputBase];
    }
}
