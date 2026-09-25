using Flyback.App.Files;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests;

/// <summary>
/// Where unsaved work is kept against a crash, and how the next start tells what a
/// crash left from what a running window is keeping.
/// </summary>
/// <remarks>
/// A crash is <see cref="Recovery.Abandon"/>: the lock let go and the snapshot left,
/// which is all a process that ends without closing its window does.
/// </remarks>
public class RecoveryTests : IDisposable
{
    private readonly string folder = Path.Combine(
        Path.GetTempPath(),
        "flyback-recovery-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);

        GC.SuppressFinalize(this);
    }

    private static RecoveredWork Work(string name = "drift") =>
        new(name, @"C:\patches", "{}", "osc -> out", null, new Dictionary<string, byte[]> { ["a.wav"] = [1, 2, 3] });

    [Fact]
    public void What_a_running_window_keeps_is_not_offered()
    {
        using var running = Recovery.Open(folder)!;

        running.Keep(Work());

        Recovery.Orphans(folder).ShouldBeEmpty();
    }

    [Fact]
    public void What_a_crash_leaves_is_found_and_reads_back_whole()
    {
        var crashed = Recovery.Open(folder)!;

        crashed.Keep(Work());
        crashed.Abandon();

        var found = Recovery.Orphans(folder).ShouldHaveSingleItem();
        var work = Recovery.Read(found).ShouldNotBeNull();

        work.Name.ShouldBe("drift");
        work.Beside.ShouldBe(@"C:\patches");
        work.Source.ShouldBe("osc -> out");
        work.Files.ShouldNotBeNull()["a.wav"].ShouldBe([1, 2, 3]);
    }

    [Fact]
    public void Work_saved_before_the_crash_is_not_offered()
    {
        var crashed = Recovery.Open(folder)!;

        crashed.Keep(Work());
        crashed.Keep(null);
        crashed.Abandon();

        Recovery.Orphans(folder).ShouldBeEmpty();
        Directory.EnumerateFiles(folder).ShouldBeEmpty("a lock with nothing beside it is tidied away");
    }

    [Fact]
    public void An_older_write_arriving_late_does_not_replace_a_newer_one()
    {
        var crashed = Recovery.Open(folder)!;

        var older = crashed.Later(Work("older"));
        var newer = crashed.Later(Work("newer"));

        newer();
        older();
        crashed.Abandon();

        Recovery.Read(Recovery.Orphans(folder).Single())!.Name.ShouldBe("newer");
    }

    [Fact]
    public void A_window_that_closes_leaves_nothing_behind()
    {
        var closing = Recovery.Open(folder)!;

        closing.Keep(Work());
        closing.Dispose();

        Directory.EnumerateFiles(folder).ShouldBeEmpty();
    }

    [Fact]
    public void An_answered_orphan_is_gone()
    {
        var crashed = Recovery.Open(folder)!;

        crashed.Keep(Work());
        crashed.Abandon();

        Recovery.Forget(Recovery.Orphans(folder).Single());

        Directory.EnumerateFiles(folder).ShouldBeEmpty();
    }

    [Fact]
    public void The_most_recent_crash_is_offered_first()
    {
        foreach (var name in new[] { "first", "second" })
        {
            var crashed = Recovery.Open(folder)!;

            crashed.Keep(Work(name));
            crashed.Abandon();

            // Far enough apart for any file system's clock to tell them apart.
            Thread.Sleep(50);
        }

        Recovery.Orphans(folder).Select(path => Recovery.Read(path)!.Name).ShouldBe(["second", "first"]);
    }

    [Fact]
    public void A_snapshot_that_does_not_read_is_nothing_rather_than_a_failure()
    {
        Directory.CreateDirectory(folder);
        var broken = Path.Combine(folder, "broken.json");
        File.WriteAllText(broken, "{ not json");

        Recovery.Read(broken).ShouldBeNull();
    }
}
