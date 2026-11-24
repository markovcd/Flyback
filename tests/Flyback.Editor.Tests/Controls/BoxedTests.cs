using Flyback.Editor.Controls;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Controls;

/// <summary>A knob's number as its box shows it: the digits the number is written with, not its binary expansion.</summary>
public class BoxedTests
{
    [Theory]
    [InlineData(0.2f, "0.2")]
    [InlineData(-1.5f, "-1.5")]
    [InlineData(440f, "440")]
    [InlineData(1e-30f, "0")]
    public void A_float_is_boxed_as_it_is_written(float value, string boxed) =>
        Boxed.Of(value).ShouldBe(decimal.Parse(boxed, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void A_double_is_boxed_as_it_is_written() => Boxed.Of(0.1).ShouldBe(0.1m);

    [Fact]
    public void Past_what_a_box_holds_is_its_furthest_and_not_a_number_is_nought()
    {
        Boxed.Of(float.MaxValue).ShouldBe(79000000000000000000000000000m);
        Boxed.Of(double.NegativeInfinity).ShouldBe(-79000000000000000000000000000m);
        Boxed.Of(float.NaN).ShouldBe(0m);
    }
}
