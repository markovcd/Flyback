using Flyback.App.Statistics;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Statistics;

/// <summary>The file the settings window's Usage section is kept in.</summary>
public sealed class UsageSettingsFileTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "flyback-usage-settings-" + Guid.NewGuid().ToString("N"));

    private string File => Path.Combine(folder, "usage.json");

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void Statistics_are_counted_unless_switched_off()
    {
        UsageSettings.Load(File).SendUsageStatistics.ShouldBeTrue();
    }

    [Fact]
    public void Switched_off_stays_off()
    {
        new UsageSettings { SendUsageStatistics = false }.Save(File);

        UsageSettings.Load(File).SendUsageStatistics.ShouldBeFalse();
    }

    [Fact]
    public void An_unreadable_file_is_the_defaults()
    {
        Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(File, "{ not json");

        UsageSettings.Load(File).SendUsageStatistics.ShouldBeTrue();
    }

    [Fact]
    public void A_build_that_is_not_a_release_counts_nothing()
    {
        Usage.Start(new UsageSettings { SendUsageStatistics = true })
            .ShouldBeSameAs(Usage.Off, "the tests are not a release build");
    }

    [Fact]
    public void Switched_off_it_is_never_started()
    {
        Usage.Start(new UsageSettings { SendUsageStatistics = false }).ShouldBeSameAs(Usage.Off);
    }
}
