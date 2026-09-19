using Flyback.App.Updates;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Updates;

/// <summary>
/// What a release says it changed, read from the changelog it was built with.
/// </summary>
public sealed class ReleaseNotesTests
{
    private const string Changelog = """
        # Changelog

        ## Unreleased

        ### Modules
        - Something not out yet.

        ## 0.4.0 — 2026-09-30

        ### Modules
        - Added `Echo`.

        ### Fixes
        - A fix.

        ## 0.3.0 — 2026-09-17

        - Older.
        """;

    [Fact]
    public void A_release_reads_its_own_section_and_no_other()
    {
        var notes = ReleaseNotes.Of(new Version(0, 4, 0), Changelog).ShouldNotBeNull();

        notes.Text.ShouldBe("### Modules\n- Added `Echo`.\n\n### Fixes\n- A fix.");
    }

    [Fact]
    public void A_release_the_changelog_does_not_name_has_no_notes()
    {
        ReleaseNotes.Of(new Version(0, 5, 0), Changelog).ShouldBeNull();
        ReleaseNotes.Of(new Version(0, 4, 1), Changelog).ShouldBeNull("0.4.0 is not 0.4.1's heading");
    }

    [Fact]
    public void The_changelog_is_built_in()
    {
        ReleaseNotes.Of(new Version(0, 3, 0)).ShouldNotBeNull().Text.ShouldNotBeEmpty();
    }
}
