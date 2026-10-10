using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Picture;

internal static class ColorAssertions
{
    /// <summary>Three channels at once, so a failure names the color rather than a register.</summary>
    public static void ShouldBe(
        this (float R, float G, float B) actual, (float R, float G, float B) expected, float tolerance)
    {
        actual.R.ShouldBe(expected.R, tolerance, $"red of {actual}");
        actual.G.ShouldBe(expected.G, tolerance, $"green of {actual}");
        actual.B.ShouldBe(expected.B, tolerance, $"blue of {actual}");
    }
}
