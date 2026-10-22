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
        Usage.Start(new UsageSettings { CountUsage = false, Asked = true }, running: null, local: "0.1.0-dev+37fc87f")
            .ShouldBeSameAs(Usage.Off);
    }

    [Fact]
    public void Switched_off_it_is_never_started()
    {
        Usage.Start(new UsageSettings { CountUsage = false, Asked = true }).ShouldBeSameAs(Usage.Off);
    }

    /// <summary>Constructed and never sent to: nothing here reaches the network.</summary>
    [Fact]
    public void Nobody_having_answered_a_release_asks()
    {
        Usage.Start(new UsageSettings(), ReleaseFeed.Released("0.1.0")).Asks.ShouldBeTrue();
    }

    [Fact]
    public void Nobody_having_answered_a_build_with_nowhere_to_send_does_not_ask()
    {
        Usage.Start(new UsageSettings(), running: null).ShouldBeSameAs(Usage.Off);
    }

    [Fact]
    public void Switched_on_a_release_does_not_ask_again()
    {
        Usage.Start(new UsageSettings { CountUsage = true }, ReleaseFeed.Released("0.1.0")).Asks.ShouldBeFalse();
    }

    [Fact]
    public void Both_answers_are_kept()
    {
        new UsageSettings { Asked = true }.Save(File);

        var loaded = UsageSettings.Load(File);

        loaded.Asked.ShouldBeTrue();
        loaded.CountUsage.ShouldBeFalse();
    }

    /// <summary>A file from before the question was asked: a tick is an answer, an empty box is not.</summary>
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void A_file_from_before_the_question_is_answered_only_by_a_tick(string ticked, bool answered)
    {
        Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(File, "{ \"usage\": { \"countUsage\": " + ticked + " } }");

        UsageSettings.Load(File).Answered.ShouldBe(answered);
    }
}
