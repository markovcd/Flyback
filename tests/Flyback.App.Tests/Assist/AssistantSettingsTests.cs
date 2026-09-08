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

    /// <summary>
    /// What a provider was set to, in the provider's own words. Nothing here
    /// knows what any of it means — see ADR-0069.
    /// </summary>
    private static AssistantValues Answers => new(new Dictionary<string, string>
    {
        ["model"] = "claude-opus-5",
        ["endpoint"] = "https://example.invalid/v1",
        ["vision"] = "0",
        ["effort"] = "High",
    });

    /// <summary>
    /// A provider may keep something that is itself JSON — a survey of an
    /// endpoint is a list of models under one key — and writing that as a quoted
    /// string escapes every quote inside it, which turns the part of the file
    /// somebody most wants to read into the part they cannot.
    /// </summary>
    [Fact]
    public void A_value_that_is_itself_json_is_written_as_json()
    {
        var models = "[{\"Id\":\"gemini-3.6-flash\",\"Hearing\":true}]";
        var settings = new AssistantSettings();

        settings.Remember("gemini", new AssistantValues(new Dictionary<string, string>
        {
            ["model"] = "gemini-3.6-flash",
            ["models"] = models,
        }));

        settings.Save(path);

        var written = File.ReadAllText(path);

        written.ShouldNotContain("\\u0022");
        written.ShouldNotContain("\\\"");
        written.ShouldContain("\"models\": [{\"Id\":\"gemini-3.6-flash\",\"Hearing\":true}]");

        // The ordinary value beside it is still a string, because that is what
        // it is. Only a list or an object goes in unquoted.
        written.ShouldContain("\"model\": \"gemini-3.6-flash\"");
    }

    /// <summary>
    /// The boundary keeps its strings whatever the file looks like: a provider
    /// hands over text and is handed back the same text, or the survey it wrote
    /// would come back as something it could not parse.
    /// </summary>
    [Fact]
    public void A_value_that_is_itself_json_comes_back_as_the_same_text()
    {
        var models = "[{\"Id\":\"gemini-3.6-flash\",\"Hearing\":true}]";
        var settings = new AssistantSettings();

        settings.Remember("gemini", new AssistantValues(new Dictionary<string, string> { ["models"] = models }));
        settings.Save(path);

        AssistantSettings.Load(path).Of("gemini").Text("models").ShouldBe(models);
    }

    /// <summary>
    /// Written by a version that quoted it, read by one that does not have to.
    /// </summary>
    [Fact]
    public void A_value_quoted_by_an_older_file_still_reads()
    {
        var folder = Path.GetDirectoryName(path)!;

        Directory.CreateDirectory(folder);
        File.WriteAllText(
            path,
            """
            {
              "Provider": "gemini",
              "Choices": { "gemini": { "models": "[{\"Id\":\"gemini-3.6-flash\"}]" } }
            }
            """);

        AssistantSettings.Load(path).Of("gemini").Text("models").ShouldBe("[{\"Id\":\"gemini-3.6-flash\"}]");
    }

    /// <summary>
    /// A word that merely begins with a bracket is a word, and a number is a
    /// string that would not survive the trip back.
    /// </summary>
    [Theory]
    [InlineData("[unfinished")]
    [InlineData("{")]
    [InlineData("007")]
    [InlineData("true")]
    [InlineData("")]
    public void Anything_that_is_not_a_list_or_an_object_stays_a_string(string held)
    {
        var settings = new AssistantSettings();

        settings.Remember("gemini", new AssistantValues(new Dictionary<string, string> { ["odd"] = held }));
        settings.Save(path);

        AssistantSettings.Load(path).Of("gemini").All["odd"].ShouldBe(held);
    }

    [Fact]
    public void Choices_survive_being_written_and_read()
    {
        var settings = new AssistantSettings { Provider = "anthropic", RememberKey = true };

        settings.Remember("anthropic", Answers);
        settings.Save(path);

        var read = AssistantSettings.Load(path);

        read.Provider.ShouldBe("anthropic");
        read.RememberKey.ShouldBeTrue();
        read.Of("anthropic").ShouldBe(Answers);
    }

    /// <summary>
    /// One bag per provider, so trying a second one for an afternoon does not
    /// cost the endpoint somebody spent time on for the first — and so that two
    /// providers with a setting of the same name do not answer for each other.
    /// </summary>
    [Fact]
    public void One_providers_answers_are_not_anothers()
    {
        var settings = new AssistantSettings();

        settings.Remember("anthropic", Answers);
        settings.Remember("openai", new AssistantValues(new Dictionary<string, string> { ["model"] = "gpt-4o" }));
        settings.Save(path);

        var read = AssistantSettings.Load(path);

        read.Of("anthropic").Text("model").ShouldBe("claude-opus-5");
        read.Of("openai").Text("model").ShouldBe("gpt-4o");
        read.Of("openai").Text("endpoint").ShouldBeEmpty();
    }

    [Fact]
    public void A_file_that_is_not_there_means_the_defaults()
    {
        var settings = AssistantSettings.Load(path);

        settings.Provider.ShouldBeEmpty();
        settings.RememberKey.ShouldBeFalse();

        // Nothing set for anybody, which is what leaves every provider on
        // whatever its own form declares.
        settings.Of("anything").ShouldBe(AssistantValues.None);
    }

    /// <summary>
    /// Hand-edited, half-written, or left by a version that thought differently.
    /// None of those is worth refusing to start over.
    /// </summary>
    /// <remarks>
    /// A file from before the settings were the provider's own is the ordinary
    /// case of the last one: its properties are not these, so what it held is
    /// gone and every form opens on its declared defaults. The key is untouched
    /// — that was never in here.
    /// </remarks>
    [Theory]
    [InlineData("{ not json at all")]
    [InlineData("""{"Provider":"openai","Model":"gpt-4o","Vision":false,"Effort":"High"}""")]
    public void A_file_that_makes_no_sense_here_means_the_defaults(string written)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, written);

        AssistantSettings.Load(path).Of("openai").ShouldBe(AssistantValues.None);
    }

    /// <summary>
    /// The guard for ADR-0034. This file holds choices; a credential goes to the
    /// operating system's own store or nowhere. If somebody later adds a string
    /// property here that looks like a secret, this is what says no.
    /// </summary>
    /// <remarks>
    /// Half of the guard since ADR-0069, and the smaller half: what a provider
    /// declares is written here too, under names this class never sees. The
    /// other half asks every installed provider what it wants and refuses a
    /// field named like a credential.
    /// </remarks>
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
        var settings = new AssistantSettings { Provider = "anthropic", RememberKey = true };

        settings.Remember("anthropic", Answers);
        settings.Save(path);

        var written = File.ReadAllText(path);

        written.ShouldContain("anthropic");
        written.ShouldNotContain("sk-");
        written.ShouldNotContain("ApiKey");
    }
}
