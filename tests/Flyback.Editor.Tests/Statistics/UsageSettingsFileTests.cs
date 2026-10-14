using Flyback.Editor.Statistics;
using Flyback.Editor.Updates;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Statistics;

/// <summary>The file the settings window's Usage section is kept in.</summary>
public sealed class UsageSettingsFileTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "flyback-usage-settings-" + Guid.NewGuid().ToString("N"));

    private string File => Path.Combine(folder, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void Statistics_are_not_counted_unless_switched_on()
    {
        UsageSettings.Load(File).CountUsage.ShouldBeFalse();
    }

    [Fact]
    public void Switched_on_stays_on()
    {
        new UsageSettings { CountUsage = true }.Save(File);

        UsageSettings.Load(File).CountUsage.ShouldBeTrue();
    }

    /// <summary>The old key was saved ticked whether anybody ticked it or not, so it is not a choice.</summary>
    [Fact]
    public void The_old_ticked_by_default_key_counts_nothing()
    {
        Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(File, """{ "usage": { "sendUsageStatistics": true } }""");

        UsageSettings.Load(File).CountUsage.ShouldBeFalse();
    }

    [Fact]
    public void An_unreadable_file_is_the_defaults()
    {
        Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(File, "{ not json");

        UsageSettings.Load(File).CountUsage.ShouldBeFalse();
    }

    [Fact]
    public void A_build_that_is_not_a_release_counts_nothing()
    {
        Usage.Start(new UsageSettings { CountUsage = true }, ReleaseFeed.Released("0.1.0+37fc87f"))
            .ShouldBeSameAs(Usage.Off);
    }

    /// <summary>Constructed and never sent to: nothing here reaches the network.</summary>
    [Fact]
    public void A_build_made_on_a_developers_machine_is_counted()
    {
        Usage.Start(new UsageSettings { CountUsage = true }, ReleaseFeed.Released("0.1.0-dev+37fc87f"), local: "0.1.0-dev+37fc87f")
            .ShouldNotBeSameAs(Usage.Off);
    }

    [Fact]
    public void A_build_made_on_a_developers_machine_is_not_counted_when_switched_off()
    {
        Usage.Start(new UsageSettings { CountUsage = false }, running: null, local: "0.1.0-dev+37fc87f")
            .ShouldBeSameAs(Usage.Off);
    }

    [Fact]
    public void Switched_off_it_is_never_started()
    {
        Usage.Start(new UsageSettings { CountUsage = false }).ShouldBeSameAs(Usage.Off);
    }
}
