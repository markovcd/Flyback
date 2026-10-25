using Avalonia;
using Avalonia.Headless.XUnit;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Flyback.Gpu;
using Flyback.Tests;
using Flyback.Ui.Controls;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Windows;

/// <summary>
/// The preview drawn through Metal (ADR-0214) where the host hands it the Metal
/// surface, as the iOS editor does: on a Mac it draws, and where there is no Metal the
/// processor takes over and the reason is said.
/// </summary>
public class MetalPreviewTests : EditorTest
{
    [AvaloniaFact]
    [TestCategory(TestCategory.Gpu)]
    public void The_preview_draws_through_Metal_and_says_so()
    {
        TestCategory.Gpu.Require(MetalHere(out var why), $"No Metal here. {why}");

        var host = new PreviewHost(() => new MetalPreviewSurface());
        var window = Show(host, width: 320);

        host.Resolution = new PixelSize(160, 90);
        host.Program = Program("Plasma");

        Draw(host, window, () => host.Frames >= 1);

        host.Backend.ShouldBe(PreviewBackend.Gpu);
        host.Renderer.ShouldBe(GraphicsApi.Metal);
    }

    /// <summary>A recording takes the frame as it takes it from OpenGL: the whole picture, bottom row first.</summary>
    [AvaloniaFact]
    [TestCategory(TestCategory.Gpu)]
    public void A_recording_takes_Metal_frames_as_it_takes_OpenGL_frames()
    {
        TestCategory.Gpu.Require(MetalHere(out var why), $"No Metal here. {why}");

        var host = new PreviewHost(() => new MetalPreviewSurface());
        var sink = new Sink();
        var window = Show(host, width: 320);

        host.Resolution = new PixelSize(160, 90);
        host.Program = Program("Plasma");
        host.BeginCapture(sink).ShouldBeNull();

        Draw(host, window, () => sink.Frames >= 1);

        sink.Width.ShouldBe(160);
        sink.Height.ShouldBe(90);
        sink.Alpha.ShouldBe((byte)255);
    }

    [AvaloniaFact]
    public void Where_there_is_no_Metal_the_processor_takes_over_and_says_why()
    {
        Assert.SkipWhen(MetalHere(out _), "This machine has Metal, so the preview draws through it.");

        var host = new PreviewHost(() => new MetalPreviewSurface());
        string? said = null;
        host.BackendChanged += message => said = message;
        var window = Show(host, width: 320);

        host.Resolution = new PixelSize(160, 90);
        host.Program = Program("Plasma");

        Draw(host, window, () => host.Backend == PreviewBackend.Cpu);

        host.GpuAvailable.ShouldBeFalse();
        said.ShouldNotBeNullOrEmpty();
    }

    /// <summary>Keeps what the last frame said about itself.</summary>
    private sealed class Sink : IFrameSink
    {
        public int Frames { get; private set; }

        public int Width { get; private set; }

        public int Height { get; private set; }

        public byte Alpha { get; private set; }

        public void Accept(ReadOnlySpan<byte> rgba, int width, int height)
        {
            Width = width;
            Height = height;
            Alpha = rgba[3];
            Frames++;
        }
    }

    /// <summary>
    /// Draws frames as the timer would until <paramref name="until"/> holds: the headless
    /// platform need not fire the timer, and a tick may land while a frame is in flight.
    /// </summary>
    private static void Draw(PreviewHost host, Avalonia.Controls.Window window, Func<bool> until)
    {
        var drawn = Task.CompletedTask;

        Pump(() =>
        {
            if (drawn.IsCompleted && host.Child is PreviewSurface surface) drawn = surface.Tick();

            return until();
        }, window);
    }

    private static bool MetalHere(out string? why)
    {
        using var device = MetalDevice.Open(out why);

        return device is not null;
    }

    private static CompiledPatch Program(string preset) =>
        Presets.All.Single(p => p.Name == preset).Build(NodeCatalog.BuiltIn).CompileForVideo(NodeCatalog.BuiltIn).Program;
}
