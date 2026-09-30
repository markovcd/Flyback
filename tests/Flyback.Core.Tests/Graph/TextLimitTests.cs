using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

public class TextLimitTests
{
    [Fact]
    public void Text_within_the_limit_is_kept_whole() =>
        TextLimit.Clip("hum", 3).ShouldBe("hum");

    [Fact]
    public void Text_over_the_limit_is_cut_to_it() =>
        TextLimit.Clip("drone", 3).ShouldBe("dro");

    [Fact]
    public void A_cut_that_would_split_a_pair_leaves_the_whole_character_out() =>
        TextLimit.Clip("ab\U0001F3B5c", 3).ShouldBe("ab");

    [Fact]
    public void A_cut_just_after_a_pair_keeps_it() =>
        TextLimit.Clip("ab\U0001F3B5c", 4).ShouldBe("ab\U0001F3B5");

    [Fact]
    public void A_limit_of_nothing_leaves_nothing() =>
        TextLimit.Clip("hum", 0).ShouldBeEmpty();
}
