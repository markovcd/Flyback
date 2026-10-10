using Flyback.Engine.Render;
using Shouldly;
using Xunit;

namespace Flyback.Viewer.Desktop.Tests;

/// <summary>What <c>--report</c> says: one <c>name: value</c> line for each thing the run measured.</summary>
public class ViewerReportTests
{
    private static readonly ViewerReport Pictured = new(10, "OpenGL", null, 592, 18.44, true, "ALSA", 2, 12.34, new SoundTiming(520, 3));

    [Fact]
    public void A_run_says_what_it_held_and_what_drew_it()
    {
        Pictured.Lines().ShouldBe(
        [
            "seconds: 10.0",
            "picture: OpenGL",
            "fps: 59.2",
            "slowest-frame-ms: 18.4",
            "sound: ALSA",
            "sound-oversample: 2",
            "sound-speed: 12.3",
            "sound-late-buffers: 3 of 520",
        ]);
    }

    [Fact]
    public void A_picture_that_fell_back_says_why()
    {
        var fell = Pictured with { Renderer = "CPU", GpuRefusal = "No OpenGL context. Falling back to the processor." };

        fell.Lines().ShouldContain("picture: CPU");
        fell.Lines().ShouldContain("gpu-refused: No OpenGL context. Falling back to the processor.");
    }

    [Fact]
    public void A_run_with_no_picture_and_no_sound_says_so()
    {
        new ViewerReport(5, null, null, 0, 0, false, null, 1, 0, default).Lines().ShouldBe(
        [
            "seconds: 5.0",
            "picture: none",
            "sound: none",
        ]);
    }

    [Fact]
    public void No_time_played_is_no_frames_a_second()
    {
        (Pictured with { Seconds = 0 }).FramesPerSecond.ShouldBe(0);
    }
}
