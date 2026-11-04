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

    /// <summary>
    /// Metal hands back each frame a frame late, so a picture held still must not stop
    /// on the one before: a patch opened while paused shows itself, not the last patch.
    /// </summary>
    [AvaloniaFact]
    [TestCategory(TestCategory.Gpu)]
    public void A_paused_preview_shows_the_patch_it_was_last_handed()
    {
        TestCategory.Gpu.Require(MetalHere(out var why), $"No Metal here. {why}");

        var host = new PreviewHost(() => new MetalPreviewSurface());
        var sink = new Sink();
        var window = Show(host, width: 320);

        host.Resolution = new PixelSize(160, 90);
        host.Clock = () => 0;
        host.Program = Program("Plasma");
        host.BeginCapture(sink).ShouldBeNull();

        Settle(host, window);
        var before = sink.Last;

        host.Program = Program("Grid");
        Settle(host, window);

        sink.Last.ShouldNotBe(before);
        sink.Last.ShouldBe(Drawn(Program("Grid"), 160, 90));
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

        /// <summary>The last frame taken, RGBA and bottom row first.</summary>
        public byte[] Last { get; private set; } = [];

        public void Accept(ReadOnlySpan<byte> rgba, int width, int height)
        {
            Width = width;
            Height = height;
            Alpha = rgba[3];
            Last = rgba.ToArray();
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

    /// <summary>Ticks as the timer would until a dozen ticks in a row have drawn nothing, as a picture held still does.</summary>
    private static void Settle(PreviewHost host, Avalonia.Controls.Window window)
    {
        var drawn = Task.CompletedTask;
        var frames = host.Frames;
        var quiet = 0;

        Pump(() =>
        {
            if (!drawn.IsCompleted) return false;

            quiet = host.Frames == frames ? quiet + 1 : 0;
            frames = host.Frames;

            if (quiet >= 12) return true;

            if (host.Child is PreviewSurface surface) drawn = surface.Tick();

            return false;
        }, window);
    }

    /// <summary>The frame <paramref name="program"/> draws at nought, waited for, as a recording takes it: RGBA, bottom row first.</summary>
    private static byte[] Drawn(CompiledPatch program, int width, int height)
    {
        using var renderer = MetalHeadlessRenderer.Open(out var why) ?? throw new InvalidOperationException(why);

        var bgra = new byte[width * height * 4];
        renderer.Render(program, 0, width, height, bgra, width * 4);

        var rgba = new byte[bgra.Length];
        var row = width * 4;

        for (var y = 0; y < height; y++)
            for (var x = 0; x < row; x += 4)
            {
                var from = y * row + x;
                var to = (height - 1 - y) * row + x;
                rgba[to + 0] = bgra[from + 2];
                rgba[to + 1] = bgra[from + 1];
                rgba[to + 2] = bgra[from + 0];
                rgba[to + 3] = bgra[from + 3];
            }

        return rgba;
    }

    private static bool MetalHere(out string? why)
    {
        using var device = MetalDevice.Open(out why);

        return device is not null;
    }

    private static CompiledPatch Program(string preset) =>
        Presets.All.Single(p => p.Name == preset).Build(NodeCatalog.BuiltIn).CompileForVideo(NodeCatalog.BuiltIn).Program;
}
