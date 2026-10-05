using Flyback.Plugins.Assist;
using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Codex.Tests;

/// <summary>What the assistant says about itself before anything is asked of it.</summary>
public class AssistantTests
{
    private static readonly ICodexCli Anything = new ScriptedCli();

    private static CodexAssistant Installed => new(() => Anything);

    private static AssistantConfig Configured(string? model = null) => new(
        KeyedTransport.None,
        model is null
            ? SettingValues.None
            : new SettingValues(new Dictionary<string, string> { [AssistantSchema.ModelKey] = model }));

    [Fact]
    public void It_needs_no_key_and_sends_nowhere()
    {
        var assistant = Installed;

        assistant.NeedsKey.ShouldBeFalse();
        assistant.Endpoint(SettingValues.None).ShouldBeNull();
        assistant.Credential.EnvironmentVariable.ShouldBeEmpty();
    }

    [Fact]
    public void It_is_available_when_codex_is_installed_whatever_the_key()
    {
        Installed.Unavailable(Configured()).ShouldBeNull();
    }

    [Fact]
    public void It_says_how_to_install_codex_when_it_is_not()
    {
        new CodexAssistant(() => null).Unavailable(Configured()).ShouldNotBeNull().ShouldContain("not installed");
    }

    [Fact]
    public void A_model_that_could_read_as_a_flag_is_unavailable()
    {
        Installed.Unavailable(Configured("--dangerously-skip-permissions")).ShouldNotBeNull();
    }

    [Fact]
    public void The_form_asks_for_no_address_and_offers_no_ear()
    {
        var keys = Installed.Form(SettingValues.None).Select(f => f.Key).ToList();

        keys.ShouldContain(AssistantSchema.ModelKey);
        keys.ShouldContain(AssistantSchema.EffortKey);
        keys.ShouldNotContain(AssistantSchema.EndpointKey);
        keys.ShouldNotContain(AssistantSchema.HearingKey);
    }

    [Fact]
    public void It_looks_and_does_not_listen()
    {
        var senses = Installed.Senses(SettingValues.None);

        senses.Vision.ShouldBeTrue();
        senses.Hearing.ShouldBe(Listener.None);
    }

    [Fact]
    public void It_starts_on_the_model_codex_picks()
    {
        Installed.Schema.DefaultModel.ShouldBe(CodexCli.DefaultModel);
        Installed.Unavailable(Configured()).ShouldBeNull();
    }
}
