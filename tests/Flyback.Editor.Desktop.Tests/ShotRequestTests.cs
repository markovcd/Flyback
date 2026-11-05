using Flyback.Editor.Desktop.Shots;
using Flyback.Ui.Controls;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Desktop.Tests;

public sealed class ShotRequestTests
{
    private static (ShotRequest? Request, string Said) Parse(params string[] args)
    {
        var error = new StringWriter();
        return (ShotRequest.Parse(["--shot", "a.png", .. args, "--preset", "Plasma"], error), error.ToString());
    }

    [Fact]
    public void A_shot_carries_the_second_and_size_it_was_given()
    {
        var (request, _) = Parse("--at", "2.5", "--size", "640x360");

        request.ShouldNotBeNull();
        request.At.ShouldBe(2.5d);
        (request.Width, request.Height).ShouldBe((640, 360));
    }

    [Fact]
    public void A_shot_can_ask_for_the_modules_over_the_picture()
    {
        Parse("--at", "1", "--size", "640x360").Request.ShouldNotBeNull().Live.ShouldBeFalse();
        Parse("--at", "1", "--size", "640x360", "--live").Request.ShouldNotBeNull().Live.ShouldBeTrue();
    }

    [Fact]
    public void A_shot_can_draw_every_box_shut_or_open_but_not_both()
    {
        Parse("--size", "640x360", "--shut").Request.ShouldNotBeNull().Shut.ShouldBeTrue();

        var (request, said) = Parse("--size", "640x360", "--open", "--shut");

        request.ShouldBeNull();
        said.ShouldContain("--open or --shut");
    }

    [Fact]
    public void A_shot_is_drawn_in_the_color_scheme_it_names()
    {
        Parse("--size", "640x360").Request.ShouldNotBeNull().Scheme.ShouldBeNull();
        Parse("--size", "640x360", "--colors", "graphite").Request.ShouldNotBeNull().Scheme!.Name.ShouldBe("Graphite");
    }

    [Fact]
    public void A_shot_is_drawn_in_a_scheme_file_with_what_it_lacks_said()
    {
        var file = Path.Combine(Path.GetTempPath(), $"flyback-scheme-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, """{ "format": 1, "name": "Sodium", "accent": "#FF9A1F" }""");

        try
        {
            var (request, said) = Parse("--size", "640x360", "--colors", file);

            request.ShouldNotBeNull().Scheme!.Name.ShouldBe("Sodium");
            request.Scheme!.Window.ShouldBe(SchemeLibrary.Default.Window);
            said.ShouldContain("grounds.window is missing");
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData("Teal")]
    [InlineData("7")]
    public void A_scheme_there_is_none_of_is_refused_with_the_ones_there_are(string colors)
    {
        var (request, said) = Parse("--size", "640x360", "--colors", colors);

        request.ShouldBeNull();
        said.ShouldContain("Phosphor, Amber, Graphite, Midnight, Paper");
    }

    /// <summary>A second that never comes would leave the run-up waiting a minute for nothing.</summary>
    [Theory]
    [InlineData("Infinity")]
    [InlineData("1e309")]
    [InlineData("NaN")]
    [InlineData("-1")]
    public void A_second_that_is_not_one_is_refused(string at)
    {
        var (request, said) = Parse("--at", at, "--size", "640x360");

        request.ShouldBeNull();
        said.ShouldContain($"--at {at}");
    }

    /// <summary>The window is a frame like any other, held to what a frame may hold.</summary>
    [Theory]
    [InlineData("27000x27000")]
    [InlineData("100000x2000")]
    public void A_window_larger_than_a_frame_may_be_is_refused(string size)
    {
        var (request, said) = Parse("--at", "1", "--size", size);

        request.ShouldBeNull();
        said.ShouldContain($"--size {size}");
    }
}
