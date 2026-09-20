using Flyback.Plugins.Assist;
using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// What a survey survives being written into a settings file and read back out
/// of one, and what a schema does once there is one.
/// </summary>
/// <remarks>
/// The file is hand-editable, which is the whole reason half of these exist: a
/// survey that threw on a stray comma would take the settings panel with it, and
/// a survey that came back half-read would offer a model box built from whatever
/// parsed before the mistake.
/// </remarks>
public class ModelSurveyTests
{
    [Fact]
    public void A_survey_survives_the_round_trip()
    {
        ModelReport[] found =
        [
            new("gemini-3.6-flash") { Hearing = true, Least = 0, Most = 24576 },
            new("gemini-3.1-pro-preview") { Vision = false },
        ];

        var read = Survey.Read(Survey.Write(found));

        read.Count.ShouldBe(2);
        read[0].Id.ShouldBe("gemini-3.6-flash");
        read[0].Hearing.ShouldBeTrue();
        read[0].Least.ShouldBe(0);
        read[0].Most.ShouldBe(24576);
        read[1].Vision.ShouldBeFalse();
        read[1].Least.ShouldBeNull();
    }

    /// <summary>
    /// What goes in the file is the four facts and nothing derived from them.
    /// Somebody opens this file and reads it, and a list that carried each model
    /// twice would be a list nobody could check.
    /// </summary>
    [Fact]
    public void Nothing_derived_is_written_down()
    {
        var written = Survey.Write([new ModelReport("gemini-3.6-flash") { Hearing = true }]);

        written.ShouldNotContain("Suggestion");
        written.ShouldContain("Hearing");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"models\":[]}")]
    [InlineData("[{\"Id\":")]
    public void Nothing_readable_is_nothing_rather_than_a_throw(string stored) =>
        Survey.Read(stored).ShouldBeEmpty();

    [Fact]
    public void A_model_with_no_name_is_not_a_model() =>
        Survey.Read("[{\"Id\":\"\"},{\"Id\":\"gemini-3.6-flash\"}]")
            .ShouldHaveSingleItem()
            .Id.ShouldBe("gemini-3.6-flash");

    [Fact]
    public void A_schema_nobody_surveyed_is_the_schema_as_written()
    {
        var schema = Written();

        schema.Surveyed(SettingValues.None).ShouldBeSameAs(schema);
    }

    [Fact]
    public void A_survey_replaces_what_was_written_down()
    {
        var surveyed = Written().Surveyed(Stored(new ModelReport("gemini-9-flash") { Hearing = true }));

        surveyed.SuggestedModels.Select(m => m.Id).ShouldBe(["gemini-9-flash"]);
        surveyed.Ears.Select(m => m.Id).ShouldBe(["gemini-9-flash"]);
    }

    /// <summary>
    /// The state a survey exists to get out of: a written-down default that
    /// answers 404. Leaving it in place would hand a fresh window the one model
    /// known not to work.
    /// </summary>
    [Fact]
    public void A_default_the_survey_did_not_find_moves_to_one_it_did()
    {
        var surveyed = Written().Surveyed(Stored(new ModelReport("gemini-9-flash")));

        surveyed.DefaultModel.ShouldBe("gemini-9-flash");
    }

    [Fact]
    public void A_default_the_survey_found_stays_where_it_was()
    {
        var surveyed = Written().Surveyed(
            Stored(new ModelReport("gemini-9-flash"), new ModelReport("written-flash")));

        surveyed.DefaultModel.ShouldBe("written-flash");
    }

    /// <summary>
    /// A survey nobody can read leaves the schema alone rather than emptying the
    /// model box, which is the same safe direction <see cref="Survey.Read"/>
    /// takes and for the same reason.
    /// </summary>
    [Fact]
    public void A_survey_nobody_can_read_changes_nothing()
    {
        var schema = Written();

        schema.Surveyed(SettingValues.None.With(Survey.Key, "{ oh dear")).ShouldBeSameAs(schema);
    }

    // --- asking about the one that is chosen ----------------------------------

    /// <summary>
    /// The translation a window cannot do for itself: it says "the chosen one"
    /// and the provider says which, because which setting names the model is the
    /// provider's business (ADR-0069).
    /// </summary>
    [Fact]
    public void Asking_for_the_chosen_one_names_the_model_that_is_set()
    {
        var asked = Written().Asking(
            new SurveyOptions { Chosen = true },
            SettingValues.None.With(AssistantSchema.ModelKey, "written-pro"));

        asked.Only.ShouldBe(["written-pro"]);
    }

    /// <summary>
    /// With a survey written down and no model picked, the box shows what the
    /// survey found rather than what the schema was written with. A probe of "the
    /// one on the form" that asked about the other one would be a button that
    /// spends money on the wrong question.
    /// </summary>
    [Fact]
    public void Asking_for_the_chosen_one_names_what_the_box_actually_shows()
    {
        var stored = Stored(new ModelReport("gemini-9-flash"));

        var asked = Written().Asking(new SurveyOptions { Chosen = true }, stored);

        asked.Only.ShouldBe(["gemini-9-flash"]);
    }

    /// <summary>
    /// A list somebody named by hand and the whole shortlist are both left
    /// exactly as they were: this resolves one question and touches nothing else.
    /// </summary>
    [Fact]
    public void Asking_for_anything_else_is_passed_through()
    {
        var options = new SurveyOptions(Only: ["one", "two"], All: true);

        Written().Asking(options, SettingValues.None).ShouldBeSameAs(options);
    }

    /// <summary>Everything listed is not what was chosen, so the narrower one wins.</summary>
    [Fact]
    public void The_chosen_one_is_asked_about_rather_than_everything_listed()
    {
        var asked = Written().Asking(
            new SurveyOptions(All: true) { Chosen = true },
            SettingValues.None);

        asked.Only.ShouldBe(["written-flash"]);
        asked.All.ShouldBeFalse();
    }

    private static AssistantSchema Written() => new(
        "written-flash",
        [new AssistantModel("written-flash", Hearing: true), new AssistantModel("written-pro")],
        "SOME_API_KEY",
        "A key from somewhere.");

    private static SettingValues Stored(params ModelReport[] found) =>
        SettingValues.None.With(Survey.Key, Survey.Write(found));
}
