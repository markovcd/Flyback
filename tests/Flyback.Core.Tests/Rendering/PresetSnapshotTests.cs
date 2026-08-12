using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;

namespace Flyback.Core.Tests.Rendering;

/// <summary>
/// Automates the check that was previously done by eye: render each preset and
/// compare it against an approved image. This is the only test that covers the
/// full path — catalogue, compiler, interpreter, coordinate conventions and
/// feedback history — as a single observable result.
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
}
