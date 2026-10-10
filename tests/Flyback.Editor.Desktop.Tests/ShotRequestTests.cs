using Flyback.Editor.Desktop.Shots;
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
