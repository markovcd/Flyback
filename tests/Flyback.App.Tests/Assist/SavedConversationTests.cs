using System.Text.Json.Nodes;
using Flyback.App.Assist;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Assist;

/// <summary>
/// The file a conversation is put away in: what goes in comes back, and what is not
/// one of these is refused rather than half read.
/// </summary>
public class SavedConversationTests
{
    private const string History = """[{"role":"user","parts":[{"text":"make a hard techno patch"}]}]""";

    private static readonly Guid Knob = Guid.NewGuid();

    private static SavedConversation Sample(string? history = History) => new(
        "gemini",
        SavedConversation.SettingsOf(Values(("model", "gemini-3.6-flash"))),
        3,
        new WorkbenchState(
            """{"nodes":[]}""",
            """{"nodes":[{"id":"x"}]}""",
            new Dictionary<string, Guid> { ["knob1"] = Knob },
            4,
            9),
        history,
        [
            new TranscriptLine(Voice.You, "make a hard techno patch"),
            new TranscriptLine(Voice.Said, "Here is a kick at 150 bpm."),
            new TranscriptLine(Voice.Aside, "Applied. Ctrl+Z puts the patch back as it was."),
        ]);

    private static AssistantValues Values(params (string Key, string Value)[] pairs) =>
        new(pairs.ToDictionary(pair => pair.Key, pair => pair.Value));

    [Fact]
    public void What_is_saved_is_what_comes_back()
    {
        var saved = Sample();

        var back = SavedConversation.Read(saved.ToJson()).ShouldNotBeNull();

        back.Provider.ShouldBe("gemini");
        back.Settings.ShouldBe(saved.Settings);
        back.Turns.ShouldBe(3);
        back.Bench.Edits.ShouldBe(4);
        back.Bench.ToolCalls.ShouldBe(9);
        back.Bench.Handles["knob1"].ShouldBe(Knob);
        back.Transcript.ShouldBe(saved.Transcript);

        JsonNode.DeepEquals(JsonNode.Parse(back.History!), JsonNode.Parse(History)).ShouldBeTrue();
        JsonNode.DeepEquals(JsonNode.Parse(back.Bench.Working), JsonNode.Parse(saved.Bench.Working)).ShouldBeTrue();
    }

    /// <summary>
    /// A bundle is a zip anybody can open, so what is in it reads as what it is:
    /// the provider's account and the patches are JSON inside the file, not strings
    /// of JSON with every quote escaped.
    /// </summary>
    [Fact]
    public void The_history_and_the_patches_are_written_as_the_JSON_they_are()
    {
        var written = JsonNode.Parse(Sample().ToJson())!;

        written["history"].ShouldBeOfType<JsonArray>();
        written["start"].ShouldBeOfType<JsonObject>();
        written["working"].ShouldBeOfType<JsonObject>();
    }

    [Fact]
    public void A_conversation_its_provider_kept_nothing_of_comes_back_with_nothing() =>
        SavedConversation.Read(Sample(history: null).ToJson()).ShouldNotBeNull().History.ShouldBeNull();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a conversation")]
    [InlineData("[]")]
    [InlineData("""{"shape":2,"provider":"gemini","settings":"","start":{},"working":{}}""")]
    [InlineData("""{"shape":1,"settings":"","start":{},"working":{}}""")]
    [InlineData("""{"shape":1,"provider":"gemini","settings":""}""")]
    public void Anything_that_is_not_one_reads_as_none(string? json) =>
        SavedConversation.Read(json).ShouldBeNull();

    [Fact]
    public void Settings_are_the_same_however_they_were_built_up() =>
        SavedConversation.SettingsOf(Values(("model", "a"), ("effort", "high")))
            .ShouldBe(SavedConversation.SettingsOf(Values(("effort", "high"), ("model", "a"))));

    [Fact]
    public void A_setting_that_differs_is_a_different_fingerprint() =>
        SavedConversation.SettingsOf(Values(("model", "a")))
            .ShouldNotBe(SavedConversation.SettingsOf(Values(("model", "b"))));

    /// <summary>
    /// What a conversation was set up with is not something to hand to whoever is
    /// sent the bundle — an endpoint can be a private address.
    /// </summary>
    [Fact]
    public void The_settings_themselves_are_not_written_down()
    {
        var saved = Sample() with
        {
            Settings = SavedConversation.SettingsOf(Values(("endpoint", "https://models.internal.example"))),
        };

        saved.ToJson().ShouldNotContain("models.internal.example");
    }
}
