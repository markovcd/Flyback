using System.Collections.Concurrent;
using CsCheck;
using Shouldly;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;

namespace Flyback.Core.Tests.Properties;

/// <summary>
/// Generates arbitrary well-formed patches and pushes them through the whole
/// pipeline. ADR-0008 notes that nothing links a module's declared ports to what
/// its emit function indexes — a mismatch only shows up when that module is
/// actually compiled. Enumerated tests would never cover all 52 combinations of
/// module, wiring and width; this does.
/// </summary>
public class PatchFuzzTests
{
    private const int Width = 32;
    private const int Height = 18;

    private static readonly NodeDef[] Modules =
        [.. NodeCatalog.All.Where(d => d.TypeId != NodeCatalog.OutputTypeId)];

    /// <summary>
    /// Modules are only ever wired from ones placed before them, so the graph is
    /// acyclic by construction — cycle handling is specified separately in the
    /// Gherkin suite rather than left to chance here.
    /// </summary>
    private static readonly Gen<Patch> Patches =
        Gen.Select(
                Gen.Int[0, Modules.Length - 1].Array[2, 12],
                Gen.Int[0, 4095].Array[96])
            .Select(t => BuildPatch(t.Item1, t.Item2));

    [Fact]
    public void Any_random_patch_compiles_and_renders_without_throwing()
    {
        Patches.Sample(patch =>
        {
            var result = PatchCompiler.Compile(patch);

            result.Program.RegisterCount.ShouldBeGreaterThanOrEqualTo(
                result.Program.OutputBase + result.Program.OutputWidth);

            var stride = Width * 4;
            var buffer = new byte[stride * Height];
            new SynthRenderer().Render(result.Program, 0.5f, Width, Height, buffer, stride);

            // Every pixel written, and opaque — the renderer's Saturate is the
            // last line of defence against a non-finite value reaching the screen.
            for (var i = 3; i < buffer.Length; i += 4)
                buffer[i].ShouldBe((byte)255);
        });
    }

    /// <summary>
    /// Feedback reads a buffer the renderer owns; a patch that samples it before
    /// any frame exists must cope with an empty history rather than fault.
    /// </summary>
    [Fact]
    public void Any_random_patch_survives_repeated_frames_with_feedback_history()
    {
        Patches.Sample(patch =>
        {
            var program = PatchCompiler.Compile(patch).Program;
            var renderer = new SynthRenderer();
            var stride = Width * 4;
            var buffer = new byte[stride * Height];

            for (var frame = 0; frame < 4; frame++)
                renderer.Render(program, frame / 30f, Width, Height, buffer, stride);

            renderer.Reset();
            renderer.Render(program, 0f, Width, Height, buffer, stride);
        });
    }

    /// <summary>
    /// The same patches through the audio sink. Whatever a module does when fed
    /// nonsense, it must not put a non-finite value or an out-of-range sample
    /// into a speaker.
    /// </summary>
    [Fact]
    public void Any_random_patch_renders_audio_within_the_representable_range()
    {
        Patches.Sample(patch =>
        {
            var program = PatchCompiler
                .Compile(patch, NodeCatalog.AudioOutputTypeId, NodeCatalog.AudioChannels)
                .Program;

            var buffer = new float[256 * 2];
            new AudioRenderer().Render(program, buffer, new AudioScan(true, 220f, 16f / 9f));

            foreach (var sample in buffer)
            {
                float.IsFinite(sample).ShouldBeTrue();
                sample.ShouldBeInRange(-1f, 1f);
            }
        });
    }

    /// <summary>
    /// Guards the fuzzer itself: a generator that only ever reached a handful of
    /// modules would pass everything above while testing almost nothing.
    /// </summary>
    [Fact]
    public void The_generator_reaches_every_module_in_the_catalogue()
    {
        var seen = new ConcurrentDictionary<string, byte>();

        Patches.Sample(
            patch =>
            {
                foreach (var node in patch.Nodes) seen.TryAdd(node.TypeId, 0);
            },
            iter: 2000);

        var missing = NodeCatalog.All.Select(d => d.TypeId).Except(seen.Keys).ToArray();
        missing.ShouldBeEmpty($"never generated: {string.Join(", ", missing)}");
    }

    private static Patch BuildPatch(int[] moduleIndices, int[] decisions)
    {
        var builder = new PatchBuilder();
        var placed = new List<(NodeInstance Node, NodeDef Def)>();
        var next = 0;

        int Decide() => decisions[next++ % decisions.Length];

        foreach (var index in moduleIndices)
        {
            var def = Modules[index];
            var node = builder.Add(def.TypeId, 0, 0);

            for (var port = 0; port < def.Inputs.Count; port++)
            {
                var upstream = placed.Where(p => p.Def.Outputs.Count > 0).ToArray();

                // Roughly a third of inputs are left on their knob value, so both
                // the wired and the default path (ADR-0009) get exercised.
                if (upstream.Length == 0 || Decide() % 3 == 0)
                {
                    node.InputValues[port] = ValueWithin(def.Inputs[port], Decide());
                    continue;
                }

                var (source, sourceDef) = upstream[Decide() % upstream.Length];
                builder.Wire(source, Decide() % sourceDef.Outputs.Count, node, port);
            }

            placed.Add((node, def));
        }

        var candidates = placed.Where(p => p.Def.Outputs.Count > 0).ToArray();

        // Both sinks, so one generated patch fuzzes the eye and the ear at once.
        var video = builder.Add(NodeCatalog.OutputTypeId, 0, 0);
        var speaker = builder.Add(NodeCatalog.AudioOutputTypeId, 0, 0);

        if (candidates.Length > 0)
        {
            var (source, sourceDef) = candidates[Decide() % candidates.Length];
            builder.Wire(source, Decide() % sourceDef.Outputs.Count, video, 0);

            var (audioSource, audioDef) = candidates[Decide() % candidates.Length];
            builder.Wire(audioSource, Decide() % audioDef.Outputs.Count, speaker, 0);
        }

        return builder.Patch;
    }

    /// <summary>Spreads the knob across the module's own declared range.</summary>
    private static float ValueWithin(PortSpec spec, int decision) =>
        spec.Min + (spec.Max - spec.Min) * (decision % 1024) / 1023f;
}
