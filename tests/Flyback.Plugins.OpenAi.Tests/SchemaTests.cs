using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.OpenAi.Tests;

/// <summary>
/// What this adapter knows about the models it names, and what it makes of a filled-in
/// form.
/// </summary>
/// <remarks>
/// A direct reference rather than a plugin loaded off disk, because none of this is
/// visible from out there: an assistant declares a form and answers about what a run
/// may be handed (ADR-0069), and the list of models behind both is the plugin's own
/// business.
/// </remarks>
public class SchemaTests
{
    private static AssistantSchema Schema => new OpenAiAssistant().Schema;

    private static AssistantValues Set(string model, bool hearing = false) =>
        new(new Dictionary<string, string>
        {
            [AssistantSchema.ModelKey] = model,
            [AssistantSchema.HearingKey] = AssistantField.Switch.Spell(hearing),
        });

    /// <summary>
    /// The default has to be one of the suggestions, or the box opens on a name
    /// its own list does not contain.
    /// </summary>
    [Fact]
    public void The_model_it_starts_on_is_one_of_the_ones_it_offers()
    {
        Schema.SuggestedModels.Select(m => m.Id).ShouldContain(Schema.DefaultModel);
        Schema.Known(Schema.DefaultModel).ShouldNotBeNull();
    }

    /// <summary>
    /// A model is an ear or a driver, and here nothing is both. That is not a
    /// rule this enforces — it is what these models are, and it is the whole
    /// reason the sound goes to a second one: driving with an ear would build
    /// blind, and a conversation driven by one is refused on its first turn for
    /// carrying no audio.
    /// </summary>
    [Fact]
    public void Nothing_that_listens_here_can_also_look()
    {
        var ears = Schema.Ears.ToArray();

        ears.ShouldNotBeEmpty("there is nothing to listen with otherwise");
        ears.ShouldAllBe(m => !m.Vision);

        // And the one that drives by default is the other way round.
        Schema.Known(Schema.DefaultModel)!.Vision.ShouldBeTrue();
    }

    /// <summary>
    /// The longest match wins, and this is the pair that makes it matter:
    /// <c>gpt-4o-audio-preview</c> begins with <c>gpt-4o</c>, so a shortest- or
    /// first-match rule would read the audio model as the one model in the list
    /// that would take away the capability it was chosen for.
    /// </summary>
    [Theory]
    [InlineData("gpt-4o", "gpt-4o", true, false)]
    [InlineData("gpt-4o-audio-preview", "gpt-4o-audio-preview", false, true)]
    [InlineData("gpt-audio", "gpt-audio", false, true)]
    [InlineData("llama3.1", "llama3.1", false, false)]
    public void What_a_model_accepts_is_read_off_the_name(
        string typed,
        string expected,
        bool vision,
        bool hearing)
    {
        var known = Schema.Known(typed).ShouldNotBeNull();

        known.Id.ShouldBe(expected);
        known.Vision.ShouldBe(vision);
        known.Hearing.ShouldBe(hearing);
    }

    /// <summary>
    /// A dated snapshot is the model it is a snapshot of. Matching whole names
    /// would make every one of these a stranger, on a form where a stranger
    /// means "you decide" rather than "it can".
    /// </summary>
    [Theory]
    [InlineData("gpt-4o-2024-11-20", "gpt-4o", false)]
    [InlineData("gpt-4o-audio-preview-2024-12-17", "gpt-4o-audio-preview", true)]
    [InlineData("gpt-4o-mini-audio-preview-2024-12-17", "gpt-4o-mini-audio-preview", true)]
    public void A_dated_snapshot_is_recognised_as_what_it_is_a_snapshot_of(
        string typed,
        string expected,
        bool hearing)
    {
        var known = Schema.Known(typed).ShouldNotBeNull();

        known.Id.ShouldBe(expected);
        known.Hearing.ShouldBe(hearing);
    }

    /// <summary>
    /// Null is "nobody here knows", not "it cannot" — the endpoint is a field, so most
    /// of what this reaches was never written down here.
    /// </summary>
    /// <remarks>
    /// The middle three are what the rule exists for: each begins with the name of a
    /// model that is written down and is not that model, so a bare prefix match would
    /// answer for all three — wrongly, in the direction that takes a switch away.
    /// </remarks>
    [Theory]
    [InlineData("mistral-large")]
    [InlineData("gpt-4o-transcribe")]
    [InlineData("gpt-4o-realtime-preview")]
    [InlineData("gpt-4o-search-preview")]
    [InlineData("")]
    [InlineData(null)]
    public void A_model_nobody_wrote_down_is_a_stranger_rather_than_a_refusal(string? typed) =>
        Schema.Known(typed).ShouldBeNull();

    /// <summary>
    /// The form takes away what the model in it would refuse, and says why.
    /// </summary>
    /// <remarks>
    /// The switch keeps whatever it was set to — a preference is parked rather
    /// than destroyed by passing through a model on the way to another — so what
    /// is pinned here is that it cannot be answered while it would mean nothing.
    /// </remarks>
    [Fact]
    public void A_model_that_takes_no_picture_leaves_nothing_to_decide_about_looking()
    {
        var looking = Schema.Form(Set("gpt-4o-audio-preview"))
            .OfType<AssistantField.Switch>()
            .First(field => field.Key == AssistantSchema.VisionKey);

        looking.Enabled.ShouldBeFalse();
        looking.Because.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// And the ear is only a question where the sound would go to a second
    /// model. Here every model that hears is one of those, so it is always
    /// asked; what governs it is whether anybody is listening at all.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void An_ear_is_a_live_choice_only_while_something_is_listening(bool hearing, bool live)
    {
        var ear = Schema.Form(Set("gpt-4o", hearing))
            .Single(field => field.Key == AssistantSchema.EarKey);

        ear.ShouldBeOfType<AssistantField.Pick>();
        ear.Enabled.ShouldBe(live);
    }

    /// <summary>
    /// What a run is handed, which is the one question the host still asks about
    /// a set of answers.
    /// </summary>
    /// <remarks>
    /// Listening is off unless it is asked for, and where it is asked for it
    /// goes to a second model — every ear here takes a sound and not a picture,
    /// so the model doing the building is never the one doing the hearing.
    /// </remarks>
    [Theory]
    [InlineData("gpt-4o", false, true, Listener.None)]
    [InlineData("gpt-4o", true, true, Listener.Another)]
    [InlineData("gpt-4o-audio-preview", true, false, Listener.Itself)]
    [InlineData("something-nobody-wrote-down", true, true, Listener.Another)]
    public void What_a_run_may_be_handed_falls_out_of_the_model_and_the_tick(
        string model,
        bool hearing,
        bool vision,
        Listener listener)
    {
        var senses = Schema.Senses(Set(model, hearing));

        senses.Vision.ShouldBe(vision);
        senses.Hearing.ShouldBe(listener);
    }
}
