using Flyback.App.PluginPackages;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.PluginPackages;

public sealed class PluginChangeTests
{
    [Theory]
    [InlineData("1.2.0", "1.10.0", -1)]
    [InlineData("1.2", "1.2.0", 0)]
    [InlineData("2.0.0", "1.99.99", 1)]
    [InlineData("1.0.0-beta", "1.0.0", -1)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.10", -1)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta", 1)]
    [InlineData("1.0.0-alpha", "1.0.0-beta", -1)]
    [InlineData("1.0.0-1", "1.0.0-alpha", -1)]
    [InlineData("0.1.0-dev", "0.1.0-dev", 0)]
    public void Versions_are_put_in_the_order_they_were_released(string left, string right, int order)
    {
        PluginChanges.Compare(left, right).ShouldBe(order);
        PluginChanges.Compare(right, left).ShouldBe(-order);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("1.x")]
    [InlineData("1.0-")]
    [InlineData("")]
    public void A_version_that_is_not_one_has_no_order(string version)
    {
        PluginChanges.Compare(version, "1.0.0").ShouldBeNull();
    }
}
