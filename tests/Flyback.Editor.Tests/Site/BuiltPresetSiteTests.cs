using Flyback.Editor.Site;
using Flyback.Editor.Statistics;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Site;

/// <summary>The preset site a copy asks is fixed when it is built.</summary>
public sealed class BuiltPresetSiteTests
{
#if DEBUG
    private const string Expected = "http://localhost:8790/";
#else
    private const string Expected = "https://flybackmodular.app/";
#endif

    [Fact]
    public void A_debug_build_asks_the_local_site_and_any_other_the_public_one() =>
        PresetSite.Built.ShouldBe(new Uri(Expected));

    [Fact]
    public void The_editor_starts_on_the_site_it_was_built_for() =>
        EditorSetup.ThisMachine(Usage.Off).Host.PresetSite.ShouldBe(PresetSite.Built);
}
