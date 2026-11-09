using Flyback.Assist.Keys;
using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;

namespace Flyback.Assist.Tests;

/// <summary>What the assistants Flyback has say about themselves, before any of them is started.</summary>
public class ShippedAssistantsTests
{
    private static IPatchAssistant OpenAi =>
        Assistants.Shipped.Find("openai")!;

    [Fact]
    public void The_chat_completions_assistant_asks_for_its_endpoint()
    {
        var assistant = OpenAi;

        assistant.Credential.EnvironmentVariable.ShouldBe("OPENAI_API_KEY");

        // The endpoint is a field here rather than a constant, which is what
        // makes one adapter reach a dozen providers and a local runtime.
        var endpoint = assistant.Form(SettingValues.None)
            .OfType<SettingField.Text>()
            .ShouldHaveSingleItem();

        endpoint.Enabled.ShouldBeTrue();
        endpoint.Fallback.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Answered from the configuration alone. Nothing here opens a connection,
    /// which is what lets the panel say what is missing before anybody has paid
    /// for finding out.
    /// </summary>
    [Theory]
    [InlineData("", "gpt-4o", null, "key")]
    [InlineData("sk-something", "gpt-4o", "not-an-address", "http")]
    public void What_is_missing_is_said_without_asking_anybody(
        string key,
        string model,
        string? baseUrl,
        string expected)
    {
        var excuse = OpenAi.Unavailable(Configured(key, model, baseUrl));

        excuse.ShouldNotBeNull();
        excuse.ShouldContain(expected);
    }

    [Fact]
    public void A_complete_configuration_has_nothing_missing()
    {
        OpenAi.Unavailable(Configured("sk-something", "gpt-4o")).ShouldBeNull();
    }

    private static IPatchAssistant Gemini =>
        Assistants.Shipped.Find("gemini")!;

    [Fact]
    public void The_generate_content_assistant_says_its_endpoint_is_fixed()
    {
        var assistant = Gemini;

        assistant.Credential.EnvironmentVariable.ShouldBe("GEMINI_API_KEY");

        // The other answer to the question above, from the same declared form:
        // this format is spoken in one place, so the endpoint is a row that says
        // so rather than a row that is missing.
        var endpoint = assistant.Form(SettingValues.None)
            .OfType<SettingField.Text>()
            .ShouldHaveSingleItem();

        endpoint.Enabled.ShouldBeFalse();
        endpoint.Because.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void A_fixed_endpoint_leaves_only_the_key_to_ask_for()
    {
        Gemini.Unavailable(AssistantConfig.Unset).ShouldNotBeNull();
        Gemini.Unavailable(Configured("AIza-something", "gemini-3.6-flash")).ShouldBeNull();
    }

    /// <summary>
    /// A configuration as the panel hands one over: a key, and the answers to
    /// whatever the provider said it wanted, under the provider's own names.
    /// </summary>
    /// <remarks>
    /// An empty model is not among the cases above and cannot be: a setting
    /// nobody has answered falls back to what the schema declared, so a provider
    /// with a default has one whatever the file says. The guard for it is still
    /// in the adapter, for a schema that declares no default.
    /// </remarks>
    private static AssistantConfig Configured(string key, string model, string? baseUrl = null)
    {
        var values = new Dictionary<string, string> { [AssistantSchema.ModelKey] = model };

        if (baseUrl is not null) values[AssistantSchema.EndpointKey] = baseUrl;

        return new AssistantConfig(new KeyedClient(key, "https://assistant.test", new KeyCredential("", "")), new SettingValues(values));
    }

    /// <summary>
    /// The guard for ADR-0034, on the route ADR-0069 opened: a provider declares its
    /// own settings and the App writes the answers to a file in plain text, so a
    /// field named like a credential is a credential on disk.
    /// </summary>
    /// <remarks>
    /// Every assistant Flyback has, asked what it wants. The pressure
    /// to "just declare an apiKey field" will look harmless, and this is what says
    /// no.
    /// </remarks>
    [Fact]
    public void Nothing_a_provider_asks_for_could_hold_a_secret()
    {
        string[] suspicious = ["key", "secret", "token", "password", "credential"];

        foreach (var assistant in Assistants.Shipped.All)
            foreach (var field in assistant.Form(SettingValues.None))
                foreach (var word in suspicious)
                {
                    field.Key.Contains(word, StringComparison.OrdinalIgnoreCase).ShouldBeFalse(
                        $"{assistant.Id} declares '{field.Key}', which looks like somewhere a credential "
                        + "would end up — and these answers are written out in plain text.");
                }
    }
}
