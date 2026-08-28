using Avalonia.Headless.XUnit;
using Flyback.App.Controls;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// Which renderer draws the picture, and the one case where the choice is not
/// the person's: a program that reads a sound file cannot be drawn by a shader.
/// </summary>
/// <remarks>
/// The tables travel with the interpreter's program and there is no texture for
/// one, so the shader would draw silence where the interpreter draws a waveform.
/// Two backends showing different pictures is the thing ADR-0035 does not allow
/// — it lets them differ in their last bits — so the shader is stood down for as
/// long as such a patch is up, rather than the eye being refused the clip.
/// <para>
/// A headless test never has a working GPU, so what is checked here is the
/// intent rather than the pixels: which backend the host has been asked for
/// against which it settles on. That is the whole of the decision.
/// </para>
/// </remarks>
public class PreviewBackendTests : UiTest
{
    private readonly string folder = Directory.CreateTempSubdirectory("flyback-preview").FullName;

    /// <summary>A program that reads a clip, and one that does not.</summary>
    private (CompiledPatch Plain, CompiledPatch Playing) Programs()
    {
        var path = Path.Combine(folder, "clip.wav");
        WavWriter.Write(path, new float[1000], 1000, 1);

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
    public void A_program_that_reads_a_clip_is_drawn_on_the_processor()
    {
        var host = new PreviewHost();
        var (plain, playing) = Programs();

        // Only meaningful where the shader was on offer in the first place; a
        // headless run may have refused it outright, and then there is nothing
        // to stand down.
        if (!host.GpuAvailable) return;

        host.Program = plain;
        host.Backend.ShouldBe(PreviewBackend.Gpu);

        host.Program = playing;
        host.Backend.ShouldBe(PreviewBackend.Cpu);
    }

    /// <summary>
    /// And it is the program's doing rather than a setting, so the shader comes
    /// back when the patch stops needing the processor.
    /// </summary>
    [AvaloniaFact]
    public void The_shader_comes_back_when_the_clip_leaves_the_picture()
    {
        var host = new PreviewHost();
        var (plain, playing) = Programs();

        if (!host.GpuAvailable) return;

        host.Program = playing;
        host.Program = plain;

        host.Backend.ShouldBe(PreviewBackend.Gpu);
    }

    /// <summary>
    /// The choice is remembered while it cannot be acted on, so the button goes
    /// on saying what was asked for — and does not quietly become the setting.
    /// </summary>
    [AvaloniaFact]
    public void What_was_asked_for_survives_being_stood_down()
    {
        var host = new PreviewHost();
        var (plain, playing) = Programs();

        if (!host.GpuAvailable) return;

        host.Program = playing;

        host.Wanted.ShouldBe(PreviewBackend.Gpu);
        host.Backend.ShouldBe(PreviewBackend.Cpu);

        host.Program = plain;
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
