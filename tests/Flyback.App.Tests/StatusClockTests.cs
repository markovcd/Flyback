using Shouldly;
using Xunit;

namespace Flyback.App.Tests;

/// <summary>The status bar's clock reads minutes:seconds.hundredths.</summary>
public class StatusClockTests
{
    [Theory]
    [InlineData(0d, "0:00.00")]
    [InlineData(9.5, "0:09.50")]
    [InlineData(65.25, "1:05.25")]
    [InlineData(3599.999, "59:59.99")]
    [InlineData(3600d, "60:00.00")]
    [InlineData(-1d, "0:00.00")]
    public void Time_is_minutes_seconds_and_fractions(double seconds, string shown) =>
        MainWindow.Clock(seconds).ShouldBe(shown);
}
