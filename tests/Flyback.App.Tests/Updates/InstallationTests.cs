using Flyback.App.Updates;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Updates;

/// <summary>
/// The other programs an installed copy is not replaced while running: the
/// command line and the viewer, published beside the shell.
/// </summary>
public sealed class InstallationTests
{
    [Fact]
    public void The_viewer_counts_as_the_installation_being_in_use() =>
        Installation.OtherPrograms.ShouldContain("flyback-viewer");

    [Fact]
    public void The_command_line_counts_as_the_installation_being_in_use() =>
        Installation.OtherPrograms.ShouldContain("flyback-cli");

    /// <summary>
    /// Pinned against the assembly name the viewer is actually published with, so
    /// a rename there is a failing test here rather than a miss discovered live.
    /// </summary>
    [Fact]
    public void The_viewer_s_name_matches_what_it_is_published_as() =>
        Installation.OtherPrograms.ShouldContain(typeof(Flyback.Viewer.Program).Assembly.GetName().Name);
}
