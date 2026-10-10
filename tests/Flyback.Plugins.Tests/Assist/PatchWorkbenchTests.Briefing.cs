using System.Text.Json;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using Flyback.Engine.Render;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Assist;

public partial class PatchWorkbenchTests
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
}
