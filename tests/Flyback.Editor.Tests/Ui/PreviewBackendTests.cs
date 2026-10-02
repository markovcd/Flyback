using Avalonia.Headless.XUnit;
using Flyback.Engine.Compile;
using Flyback.Ui.Controls;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Render;
using Shouldly;

namespace Flyback.Editor.Tests.Ui;

/// <summary>
/// Which renderer draws the picture: the one asked for, whatever the patch reads,
/// a sound file included, since the shader reads a clip as a texture.
/// </summary>
/// <remarks>
/// A headless test never has a working GPU, so what is checked is which backend the
/// host was asked for against which it settles on.
/// </remarks>
public class PreviewBackendTests : EditorTest
{
    private readonly string folder = Directory.CreateTempSubdirectory("flyback-preview").FullName;

    /// <summary>A program that reads a clip, and one that does not.</summary>
    private (CompiledPatch Plain, CompiledPatch Playing) Programs()
    {
        var path = Path.Combine(folder, "clip.wav");
        using (var file = File.Create(path)) WavWriter.Write(file, new float[1000], 1000, 1);

        var library = new SampleLibrary { Beside = folder };

        var plain = new PatchBuilder(NodeCatalog.BuiltIn);
        var knob = plain.Add("value", 0, 0);
        var plainSink = plain.Add(NodeCatalog.OutputTypeId, 200, 0);
        plain.Wire(knob, 0, plainSink, NodeCatalog.OutputColorPort);

        var playing = new PatchBuilder(NodeCatalog.BuiltIn);
        var player = playing.Add(NodeCatalog.SampleTypeId, 0, 0);
        SampleExtra.Set(player, path);
        var playingSink = playing.Add(NodeCatalog.OutputTypeId, 200, 0);
        playing.Wire(player, 0, playingSink, NodeCatalog.OutputColorPort);

        return (
            plain.Patch.CompileForVideo(NodeCatalog.BuiltIn, library).Program,
            playing.Patch.CompileForVideo(NodeCatalog.BuiltIn, library).Program);
    }

    [AvaloniaFact]
    public void A_program_that_reads_a_clip_stays_on_the_shader()
    {
        var host = new PreviewHost();
        var (plain, playing) = Programs();

        // Only meaningful where the shader was on offer in the first place; a
        // headless run may have refused it outright.
        if (!host.GpuAvailable) return;

        host.Program = plain;
        host.Program = playing;

        host.Backend.ShouldBe(PreviewBackend.Gpu);
    }

    /// <summary>
    /// Turning the shader off by hand still means off, whatever the patch does.
    /// </summary>
    [AvaloniaFact]
    public void Choosing_the_processor_is_not_undone_by_a_patch_that_could_use_a_shader()
    {
        var host = new PreviewHost();
        var (plain, playing) = Programs();

        host.Use(PreviewBackend.Cpu);

        host.Program = playing;
        host.Backend.ShouldBe(PreviewBackend.Cpu);

        host.Program = plain;
        host.Backend.ShouldBe(PreviewBackend.Cpu);
        host.Wanted.ShouldBe(PreviewBackend.Cpu);
    }
}
