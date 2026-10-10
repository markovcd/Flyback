using Flyback.Plugins.Assist;
using Flyback.Plugins.Programs;
using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.ClaudeCode.Tests;

/// <summary>What the assistant says about itself before anything is asked of it.</summary>
public class AssistantTests
{
    private static readonly IProgram Anything = new Nothing();

    private static ClaudeCodeAssistant Installed => new(() => Anything);

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
    public void It_is_available_when_claude_is_installed_whatever_the_key()
    {
        Installed.Unavailable(Configured()).ShouldBeNull();
    }

    [Fact]
    public void It_says_how_to_install_claude_when_it_is_not()
    {
        new ClaudeCodeAssistant(() => null).Unavailable(Configured()).ShouldNotBeNull().ShouldContain("not installed");
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
    public void The_model_box_offers_each_alias_claude_takes()
    {
        var model = Installed.Form(SettingValues.None).OfType<SettingField.Pick>().Single(f => f.Key == AssistantSchema.ModelKey);

        model.Options.Select(o => o.Id).ShouldBe(["sonnet", "opus", "fable", "haiku"]);
    }

    [Fact]
    public void It_looks_and_does_not_listen()
    {
        var senses = Installed.Senses(SettingValues.None);

        senses.Vision.ShouldBeTrue();
        senses.Hearing.ShouldBe(Listener.None);
    }

    /// <summary>A program that is there and answers nothing; only whether it is installed matters.</summary>
    private sealed class Nothing : IProgram
    {
        public Task<ProgramAnswer> Ask(ProgramQuestion question, CancellationToken cancel) =>
            throw new NotSupportedException();
    }
}
