using Shouldly;
using Xunit;
using static Flyback.Assist.Tests.Workbenches;

namespace Flyback.Assist.Tests;

public class PatchWorkbenchBriefingTests
{
    // --- the briefing -------------------------------------------------------

    /// <summary>
    /// The briefing is the cached prefix of every request in a run. A provider
    /// only keeps reading that cache while the bytes are identical, so a
    /// briefing that varies is not a cosmetic problem — it is paying to write
    /// the cache again on every single turn, silently.
    /// </summary>
    [Fact]
    public void The_briefing_is_the_same_text_every_time_it_is_built() =>
        Bench().Briefing.ShouldBe(Bench().Briefing);

    /// <summary>
    /// A model that stops reading partway through the long module list still
    /// learns every module exists from the index ahead of it.
    /// </summary>
    [Fact]
    public void Every_module_is_named_by_category_before_the_long_list()
    {
        var briefing = Bench().Briefing;
        var index = briefing.IndexOf("By category:", StringComparison.Ordinal);
        var list = briefing.IndexOf("\noutput | Output | Output", StringComparison.Ordinal);

        index.ShouldBeGreaterThan(0);
        list.ShouldBeGreaterThan(index);

        var head = briefing[index..list];
        head.ShouldContain("- Timing: ");
        head.ShouldContain("seq.notes");
        head.ShouldContain("audio.reverb");
    }
}
