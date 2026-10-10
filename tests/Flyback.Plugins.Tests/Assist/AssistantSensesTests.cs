using Flyback.Plugins.Assist;
using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// What a shipped assistant says it can see and hear agrees with the form it
/// shows, whichever way the form is built. Checked across every shipped
/// provider, since the two answers are only worth anything together: a switch
/// the senses deny sends a person to tick something that does nothing, and a
/// sense the form never offers is advice about a switch that is not there.
/// </summary>
public class AssistantSensesTests
{
    private static IPatchAssistant Shipped(string id) => ShippedPlugins.Loaded.Assistants.Single(a => a.Id == id);

    /// <summary>
    /// A survey that found a model of each kind, written down as a probe would
    /// leave it, so a provider that reads surveys offers what was found.
    /// </summary>
    private static readonly string Surveyed = Survey.Write(
    [
        new ModelReport("found-silent"),
        new ModelReport("found-blind") { Vision = false },
        new ModelReport("found-hearing") { Hearing = true },
    ]);

    /// <summary>Every shipped assistant, with and without a survey, under every setting of the two switches.</summary>
    public static TheoryData<string, string?, string?, bool?, bool?> Configurations()
    {
        var data = new TheoryData<string, string?, string?, bool?, bool?>();

        foreach (var id in new[] { "claude-code", "codex", "gemini", "openai" })
            foreach (var hearing in new bool?[] { null, false, true })
                foreach (var vision in new bool?[] { null, false, true })
                {
                    data.Add(id, null, null, hearing, vision);

                    foreach (var model in new string?[] { null, "found-blind", "found-hearing" })
                        data.Add(id, Surveyed, model, hearing, vision);
                }

        return data;
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void An_ear_is_on_offer_exactly_where_the_form_shows_a_listening_switch_that_is_enabled_and_off(
        string id, string? survey, string? model, bool? hearing, bool? vision)
    {
        var assistant = Shipped(id);
        var values = Set(survey, model, hearing, vision);

        var senses = assistant.Senses(values);
        var listen = assistant.Form(values).OfType<SettingField.Switch>().SingleOrDefault(f => f.Key == AssistantSchema.HearingKey);

        senses.EarOffered.ShouldBe(listen is { Enabled: true } && !listen.Value(values.Text(AssistantSchema.HearingKey)));
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void It_sees_exactly_where_the_form_shows_the_looking_switch_enabled_and_on(
        string id, string? survey, string? model, bool? hearing, bool? vision)
    {
        var assistant = Shipped(id);
        var values = Set(survey, model, hearing, vision);

        var senses = assistant.Senses(values);
        var look = assistant.Form(values).OfType<SettingField.Switch>().Single(f => f.Key == AssistantSchema.VisionKey);

        senses.Vision.ShouldBe(look.Enabled && look.Value(values.Text(AssistantSchema.VisionKey)));
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void It_hears_exactly_where_the_form_shows_the_listening_switch_enabled_and_on(
        string id, string? survey, string? model, bool? hearing, bool? vision)
    {
        var assistant = Shipped(id);
        var values = Set(survey, model, hearing, vision);

        var senses = assistant.Senses(values);
        var listen = assistant.Form(values).OfType<SettingField.Switch>().SingleOrDefault(f => f.Key == AssistantSchema.HearingKey);

        (senses.Hearing != Listener.None).ShouldBe(listen is { Enabled: true } && listen.Value(values.Text(AssistantSchema.HearingKey)));
    }

    private static SettingValues Set(string? survey, string? model, bool? hearing, bool? vision)
    {
        var held = new Dictionary<string, string>();

        if (survey is not null) held[Survey.Key] = survey;
        if (model is not null) held[AssistantSchema.ModelKey] = model;
        if (hearing is { } h) held[AssistantSchema.HearingKey] = SettingField.Switch.Spell(h);
        if (vision is { } v) held[AssistantSchema.VisionKey] = SettingField.Switch.Spell(v);

        return new SettingValues(held);
    }
}
