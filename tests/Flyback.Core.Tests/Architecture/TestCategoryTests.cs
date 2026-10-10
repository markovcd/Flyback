using Flyback.Tests;
using Shouldly;
using Xunit;

namespace Flyback.Core.Tests.Architecture;

/// <summary>A test that needs a tool is filed under it, so a run can pick it out or leave it out.</summary>
public class TestCategoryTests
{
    [Fact]
    [TestCategory(TestCategory.Ffmpeg)]
    public void A_test_filed_under_what_it_needs_passes_the_check() => TestCategory.Ffmpeg.Carried();

    [Fact]
    public void A_test_that_needs_a_tool_it_is_not_filed_under_fails_even_where_the_tool_is_missing() =>
        Should.Throw<InvalidOperationException>(() => TestCategory.Node.Require(false, "no Node here"))
            .Message.ShouldContain("[TestCategory(TestCategory.Node)]");

    [Fact]
    [TestCategory(TestCategory.Jack)]
    public void Filed_under_one_category_is_not_filed_under_another() =>
        Should.Throw<InvalidOperationException>(() => TestCategory.Gpu.Carried()).Message.ShouldContain("@gpu");
}
