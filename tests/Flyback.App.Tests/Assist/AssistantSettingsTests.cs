using System.Reflection;
using Flyback.App.Assist;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Assist;

/// <summary>
/// The first file this application has ever written about itself, and the one
/// thing that must never appear in it.
/// </summary>
public class AssistantSettingsTests : IDisposable
{
    private readonly string path = Path.Combine(
        Path.GetTempPath(),
        "flyback-settings-" + Guid.NewGuid().ToString("N"),
        "assistant.json");

    public void Dispose()
    {
        var folder = Path.GetDirectoryName(path);

        if (folder is not null && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void Choices_survive_being_written_and_read()
    {
        new AssistantSettings
        {
            Provider = "anthropic",
            Model = "claude-opus-5",
            BaseUrl = "https://example.invalid/v1",
            Vision = false,
            Hearing = true,
            EarModel = "some-ear",
            Effort = AssistantEffort.High,
            RememberKey = true,
        }.Save(path);

        var read = AssistantSettings.Load(path);

        read.Provider.ShouldBe("anthropic");
        read.Model.ShouldBe("claude-opus-5");
        read.BaseUrl.ShouldBe("https://example.invalid/v1");
        read.Vision.ShouldBeFalse();
        read.Hearing.ShouldBeTrue();
        read.EarModel.ShouldBe("some-ear");
        read.Effort.ShouldBe(AssistantEffort.High);
        read.RememberKey.ShouldBeTrue();
    }

    [Fact]
    public void A_file_that_is_not_there_means_the_defaults()
    {
        var settings = AssistantSettings.Load(path);

        settings.Provider.ShouldBeEmpty();
        settings.Vision.ShouldBeTrue();

        // Off where sight is on: a picture reaches every endpoint this adapter
        // speaks to, and a sound reaches only the few models built to take one.
        settings.Hearing.ShouldBeFalse();
        settings.Effort.ShouldBe(AssistantEffort.Medium);
    }

    /// <summary>
    /// Hand-edited, half-written, or left by a version that thought differently.
    /// None of those is worth refusing to start over.
    /// </summary>
    [Fact]
    public void A_file_that_makes_no_sense_means_the_defaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json at all");

        AssistantSettings.Load(path).Effort.ShouldBe(AssistantEffort.Medium);
    }

    /// <summary>
    /// The guard for ADR-0034. This file holds choices; a credential goes to the
    /// operating system's own store or nowhere. If somebody later adds a string
    /// property here that looks like a secret, this is what says no.
    /// </summary>
    [Fact]
    public void Nothing_that_could_hold_a_secret_is_written_here()
    {
        string[] suspicious = ["key", "secret", "token", "password", "credential"];

        var strings = typeof(AssistantSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => p.Name)
            .ToArray();

        foreach (var name in strings)
        foreach (var word in suspicious)
        {
            name.Contains(word, StringComparison.OrdinalIgnoreCase).ShouldBeFalse(
                $"'{name}' looks like somewhere a credential would end up, and this file is written in plain text.");
        }
    }

    [Fact]
    public void What_is_written_out_contains_no_secret()
    {
        new AssistantSettings { Provider = "anthropic", Model = "claude-opus-5", RememberKey = true }.Save(path);

        var written = File.ReadAllText(path);

        written.ShouldContain("anthropic");
        written.ShouldNotContain("sk-");
        written.ShouldNotContain("ApiKey");
    }
}
