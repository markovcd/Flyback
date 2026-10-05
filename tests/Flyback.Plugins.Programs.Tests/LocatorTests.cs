using Shouldly;
using Xunit;

namespace Flyback.Plugins.Programs.Tests;

/// <summary>Finding a program by looking in folders.</summary>
public class LocatorTests
{
    private const string Program = "some-program";

    [Fact]
    public void The_first_folder_that_holds_the_program_wins()
    {
        var empty = Directory.CreateTempSubdirectory("flyback-programs-empty");
        var first = Directory.CreateTempSubdirectory("flyback-programs-first");
        var second = Directory.CreateTempSubdirectory("flyback-programs-second");

        try
        {
            File.WriteAllText(Path.Combine(first.FullName, Program), string.Empty);
            File.WriteAllText(Path.Combine(second.FullName, Program), string.Empty);

            ProgramLocator.Find(Program, [empty.FullName, "", first.FullName, second.FullName])
                .ShouldBe(Path.Combine(first.FullName, Program));
        }
        finally
        {
            empty.Delete(true);
            first.Delete(true);
            second.Delete(true);
        }
    }

    [Fact]
    public void Nothing_is_found_where_it_is_not_and_a_bad_path_entry_is_skipped()
    {
        var empty = Directory.CreateTempSubdirectory("flyback-programs-empty");

        try
        {
            ProgramLocator.Find(Program, [empty.FullName, "bad\0entry"]).ShouldBeNull();
        }
        finally
        {
            empty.Delete(true);
        }
    }

    [Fact]
    public void The_path_is_read_a_folder_at_a_time_without_the_quotes_around_it()
    {
        var before = Environment.GetEnvironmentVariable("PATH");

        try
        {
            Environment.SetEnvironmentVariable(
                "PATH", string.Join(Path.PathSeparator, "\"/quoted folder\"", "/plain"));

            ProgramLocator.PathFolders().ShouldBe(["/quoted folder", "/plain"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", before);
        }
    }
}
