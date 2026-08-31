using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;

namespace Flyback.Core.Tests.Rendering;

/// <summary>
/// Renders each preset and compares it against an approved image. This is the
/// only test that covers the full path — catalogue, compiler, interpreter,
/// coordinate conventions and feedback history — as a single observable
/// result.
/// </summary>
public class PresetSnapshotTests
{
    private const int Width = 320;
    private const int Height = 180;

    /// <summary>
    /// Enough frames for the feedback preset to build up history; the others are
    /// pure functions of time and would look the same after one.
    /// </summary>
    private const int WarmUpFrames = 40;

    public static TheoryData<string> PresetNames => [.. Presets.All.Select(p => p.Name)];

    [Theory]
    [MemberData(nameof(PresetNames))]
    public async Task Preset_renders_as_approved(string presetName)
    {
        // Built against the engine's own catalogue on purpose: a preset that
        // ships with the synth must never need a plugin to be installed.
        var patch = Presets.All.Single(p => p.Name == presetName).Build(NodeCatalog.BuiltIn);
        var program = patch.CompileForVideo(NodeCatalog.BuiltIn).Program;

        var renderer = new SynthRenderer();
        var stride = Width * 4;
        var buffer = new byte[stride * Height];

        for (var frame = 0; frame < WarmUpFrames; frame++)
            renderer.Render(program, frame / 30f, Width, Height, buffer, stride);

        var png = new MemoryStream();
        PngWriter.WriteBgra(png, buffer, Width, Height, stride);
        png.Position = 0;

        await Verify(png, "png")
            .UseDirectory("snapshots")
            .UseParameters(presetName);
    }

    /// <summary>
    /// The one preset that is played rather than run, drawn with a key held down.
    /// </summary>
    /// <remarks>
    /// Every image above is what a patch does on its own, and for this one that
    /// is the picture of nobody touching it — which is worth approving too, but
    /// is not what the preset is for. This is the other half: the same program,
    /// evaluated with a note in the block, which is the only way the live path
    /// gets an approved picture at all.
    /// </remarks>
    [Fact]
    public async Task The_played_preset_renders_as_approved_with_a_note_held()
    {
        var patch = Presets.All.Single(p => p.Name == "Played").Build(NodeCatalog.BuiltIn);
        var program = patch.CompileForVideo(NodeCatalog.BuiltIn).Program;

        var live = new LiveValues(program.LiveInputs);

        // A4, held, having been struck once. Velocity is left at nothing, which
        // is what a typist plays with and what this patch reads none of.
        live.Set(MidiSignal.Key(MidiSources.Keyboard, MidiSignal.Pitch), 69f);
        live.Set(MidiSignal.Key(MidiSources.Keyboard, MidiSignal.Gate), 1f);
        live.Set(MidiSignal.Key(MidiSources.Keyboard, MidiSignal.Strikes), 1f);

        var renderer = new SynthRenderer();
        var stride = Width * 4;
        var buffer = new byte[stride * Height];

        renderer.Render(program, 0d, Width, Height, buffer, stride, live);

        var png = new MemoryStream();
        PngWriter.WriteBgra(png, buffer, Width, Height, stride);
        png.Position = 0;

        await Verify(png, "png").UseDirectory("snapshots");
    }
}
