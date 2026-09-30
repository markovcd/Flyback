using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Flyback.App.Controls;
using Flyback.Core.Compile;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// A host with no OpenGL control, a page, hands the preview a GPU surface of its own
/// (ADR-0162), and the preview treats it as it treats the desktop's.
/// </summary>
public class GpuSurfaceTests : UiTest
{
    [AvaloniaFact]
    public void The_preview_draws_on_the_surface_it_was_given()
    {
        var surface = new Surface();
        var host = new PreviewHost(() => surface);

        host.Resolution = new PixelSize(320, 180);
        host.Program = CompiledPatch.Black;

        host.Child.ShouldBeSameAs(surface);
        host.Backend.ShouldBe(PreviewBackend.Gpu);
        surface.Resolution.ShouldBe(new PixelSize(320, 180));
    }

    [AvaloniaFact]
    public void The_renderer_is_named_for_the_api_drawing_and_then_the_cpu()
    {
        var surface = new Surface();
        var host = new PreviewHost(() => surface);

        host.Renderer.ShouldBe("WebGL");

        surface.Fail("This browser has no WebGL 2.");

        host.Renderer.ShouldBe("CPU");
    }

    [AvaloniaFact]
    public void A_given_surface_that_fails_gives_way_to_the_processor_for_good()
    {
        var surface = new Surface();
        var host = new PreviewHost(() => surface);
        string? said = null;
        host.BackendChanged += message => said = message;

        surface.Fail("This browser has no WebGL 2.");

        host.Backend.ShouldBe(PreviewBackend.Cpu);
        host.GpuAvailable.ShouldBeFalse();
        said.ShouldBe("This browser has no WebGL 2.");
    }

    [AvaloniaFact]
    public void In_a_page_a_surface_that_fails_is_not_replaced_by_the_processor()
    {
        var surface = new Surface(processorStandsIn: false);
        var host = new PreviewHost(() => surface);
        string? said = null;
        host.BackendChanged += message => said = message;

        surface.Fail("This browser has no WebGL 2.");

        host.Backend.ShouldBe(PreviewBackend.Gpu);
        host.Child.ShouldBeSameAs(surface);
        said.ShouldBe("This browser has no WebGL 2.");
    }

    [AvaloniaFact]
    public void In_a_page_the_processor_cannot_be_chosen()
    {
        var surface = new Surface(processorStandsIn: false);
        var host = new PreviewHost(() => surface);

        host.Use(PreviewBackend.Cpu);

        host.Backend.ShouldBe(PreviewBackend.Gpu);
        host.Child.ShouldBeSameAs(surface);
    }

    /// <summary>A GPU surface that draws nothing and fails when told to.</summary>
    private sealed class Surface(bool processorStandsIn = true) : Border, IGpuPreview
    {
        public event Action<string>? Failed;

        public string? Api => "WebGL";

        public bool ProcessorStandsIn => processorStandsIn;

        public double Time { get; set; }

        public Func<double>? Clock { get; set; }

        public double FramesPerSecond => 0;

        public double FrameMilliseconds => 0;

        public long Frames => 0;

        public double FrameRate { get; set; }

        public PixelSize Resolution { get; set; }

        public CompiledPatch Program { get; set; } = CompiledPatch.Black;

        LiveValues IPreviewSurface.Live { get; set; } = LiveValues.None;

        public void Refresh()
        {
        }

        public void Rewind() => Time = 0;

        public void Fail(string message) => Failed?.Invoke(message);
    }
}
