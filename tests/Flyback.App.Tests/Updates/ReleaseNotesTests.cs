using Flyback.App.Updates;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Updates;

/// <summary>
/// What an update says changed, read from the changelog the new version was built with.
/// </summary>
public sealed class ReleaseNotesTests
{
    private const string Changelog = """
        # Changelog

        ## Unreleased

        ### Modules
        - Something not out yet.

        ## 0.5.0 — 2026-10-14

        - Fifth.

        ## 0.4.0 — 2026-09-30

        ### Modules
        - Added `Echo`.

        ### Fixes
        - A fix.

        ## 0.3.0 — 2026-09-17

        - Third.
        """;

    private static readonly Version Fifth = new(0, 5, 0);

    private static IEnumerable<string> Headings(ReleaseNotes notes) => notes.Sections.Select(section => section.Heading);

    [Fact]
    public void A_release_reads_its_own_section_and_no_other()
    {
        var notes = ReleaseNotes.Of(new Version(0, 4, 0), since: null, Changelog).ShouldNotBeNull();

        notes.Sections.ShouldBe([new("0.4.0 — 2026-09-30", "### Modules\n- Added `Echo`.\n\n### Fixes\n- A fix.")]);
    }

    [Fact]
    public void An_update_that_skipped_a_release_reads_both_newest_first()
    {
        var notes = ReleaseNotes.Of(Fifth, since: new Version(0, 3, 0), Changelog).ShouldNotBeNull();

        Headings(notes).ShouldBe(["0.5.0 — 2026-10-14", "0.4.0 — 2026-09-30"]);
    }

    [Fact]
    public void An_update_from_the_release_before_reads_one()
    {
        Headings(ReleaseNotes.Of(Fifth, since: new Version(0, 4, 0), Changelog).ShouldNotBeNull())
            .ShouldBe(["0.5.0 — 2026-10-14"]);
    }

    [Fact]
    public void A_copy_no_older_than_the_release_reads_only_the_release()
    {
        var notes = ReleaseNotes.Of(new Version(0, 4, 0), since: Fifth, Changelog).ShouldNotBeNull();

        Headings(notes).ShouldBe(["0.4.0 — 2026-09-30"]);
        notes.Since.ShouldBeNull();
    }

    [Fact]
    public void A_release_the_changelog_does_not_name_has_no_notes()
    {
        ReleaseNotes.Of(new Version(0, 6, 0), since: new Version(0, 3, 0), Changelog)
            .ShouldBeNull("an Unreleased section is not this version's, whatever came before it");
        ReleaseNotes.Of(new Version(0, 4, 1), since: null, Changelog).ShouldBeNull("0.4.0 is not 0.4.1's heading");
    }

    [Fact]
    public void The_changelog_is_built_in()
    {
        ReleaseNotes.Of(new Version(0, 3, 0), since: new Version(0, 1, 0)).ShouldNotBeNull().Sections.Count.ShouldBe(2);
    }
}
