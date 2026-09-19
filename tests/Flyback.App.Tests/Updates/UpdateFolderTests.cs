using Flyback.App.Updates;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Updates;

/// <summary>
/// What waits between a download and an install, and the decisions a start makes
/// from it before any window exists.
/// </summary>
public sealed class UpdateFolderTests : IDisposable
{
    private readonly string scratch = Path.Combine(Path.GetTempPath(), "flyback-update-folder-" + Guid.NewGuid().ToString("N"));

    private UpdateFolder Folder => new(scratch);

    private static readonly Version Running = new(0, 3, 0);

    public void Dispose()
    {
        if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
        if (Directory.Exists(scratch + "-copy")) Directory.Delete(scratch + "-copy", recursive: true);
    }

    private void Stage(string version) => Directory.CreateDirectory(Path.Combine(scratch, version, "win-x64"));

    [Fact]
    public void The_newest_version_newer_than_this_one_is_pending()
    {
        Stage("0.2.0");
        Stage("0.4.0");
        Stage("0.10.0");
        Directory.CreateDirectory(Path.Combine(scratch, "0.11.0.partial"));

        Folder.Pending(Running).ShouldBe(new Version(0, 10, 0));
    }

    [Fact]
    public void A_version_that_failed_too_often_is_left_for_the_next_release()
    {
        Stage("0.4.0");
        var version = new Version(0, 4, 0);

        for (var failure = 1; failure < UpdateFolder.Attempts; failure++)
        {
            Folder.Failed(version).ShouldBe(failure);
            Folder.Pending(Running).ShouldBe(version, $"still tried after {failure} failure(s)");
        }

        Folder.Failed(version);
        Folder.Pending(Running).ShouldBeNull();
    }

    [Fact]
    public void Tidying_clears_what_the_running_version_has_no_use_for_and_says_the_note_once()
    {
        Stage("0.3.0");
        Stage("0.4.0");
        File.WriteAllText(Path.Combine(scratch, "flyback-0.4.0-win-x64.zip.partial"), "");
        Folder.Note("Updated to Flyback 0.3.0.", replaced: new Version(0, 2, 0));

        Folder.Tidy(Running).ShouldBe(("Updated to Flyback 0.3.0.", new Version(0, 2, 0)));
        Folder.Tidy(Running).ShouldBe((null, null), "the note is said once");

        Folder.Ready().ShouldBe([new Version(0, 4, 0)]);
        Directory.GetFiles(scratch).ShouldBeEmpty();
    }

    [Fact]
    public void Tidying_a_folder_that_does_not_exist_is_nothing()
    {
        Folder.Tidy(Running).ShouldBe((null, null));
    }

    [Fact]
    public void A_copy_says_which_release_it_is_from_its_files()
    {
        var tests = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

        Here(tests).ProductVersion().ShouldBe(Flyback.App.Controls.About.Version);
        Here(scratch).ProductVersion().ShouldBeNull("there is no copy there");
    }

    private static Installation Here(string root) => new(root, "Flyback.exe", "win-x64", Bundle: false);

    [Fact]
    public void Switched_off_nothing_is_handed_off_and_what_was_downloaded_goes()
    {
        Stage("0.4.0");

        Updater.HandOff([], new UpdateSettings { CheckForUpdates = false }, Here(scratch), Running, Folder)
            .ShouldBeFalse();

        Folder.Ready().ShouldBeEmpty();
    }

    [Fact]
    public void A_build_that_is_not_a_release_hands_off_nothing()
    {
        Stage("0.4.0");

        Updater.HandOff([], new UpdateSettings(), Here(scratch), running: null, Folder).ShouldBeFalse();
    }

    /// <summary>
    /// A failed install starts the old copy again, and that launch must open rather
    /// than try again at once — the note it has not said yet is how it knows.
    /// </summary>
    [Fact]
    public void An_install_just_tried_is_not_tried_again_in_the_same_start()
    {
        Stage("0.4.0");
        Folder.Note("Could not update to Flyback 0.4.0.");

        Updater.HandOff([], new UpdateSettings(), Here(scratch), Running, Folder).ShouldBeFalse();
    }

    /// <summary>What the new version does once it runs: its files go in, and the next window is told.</summary>
    [Fact]
    public void Applying_installs_and_leaves_a_note()
    {
        var payload = Path.Combine(scratch, "0.4.0", "win-x64");
        var installed = scratch + "-copy";

        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(installed);
        File.WriteAllText(Path.Combine(payload, "Flyback.exe"), "new");
        File.WriteAllText(Path.Combine(installed, "Flyback.exe"), "old");

        Updater.Apply(Here(payload), Here(installed), new Version(0, 4, 0), Folder);

        File.ReadAllText(Path.Combine(installed, "Flyback.exe")).ShouldBe("new");
        Folder.Tidy(new Version(0, 4, 0)).ShouldBe(("Updated to Flyback 0.4.0.", null), "the copy replaced is no release");
    }

    [Fact]
    public void Applying_where_it_cannot_counts_a_failure_and_says_why()
    {
        var payload = Path.Combine(scratch, "0.4.0", "win-x64");
        Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(payload, "Flyback.exe"), "new");

        // A file where the installed copy's folder should be: nothing can be put in it.
        var installed = scratch + "-copy";
        File.WriteAllText(installed, "not a folder");

        Updater.Apply(Here(payload), Here(installed), new Version(0, 4, 0), Folder);

        Folder.Failures(new Version(0, 4, 0)).ShouldBe(1);
        Folder.Tidy(Running).Note.ShouldNotBeNull().ShouldStartWith("Could not update to Flyback 0.4.0, and will try again");

        File.Delete(installed);
    }
}
