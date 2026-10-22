using Flyback.Editor.Shell;
using Flyback.Editor.Statistics;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The first-start question about usage counts, with a sink that keeps what would have been sent.</summary>
[Binding]
public sealed class UsageQuestionSteps(EditorDriver editor) : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-usage-specs");

    private readonly Kept sink = new();

    private string SettingsPath => Path.Combine(folder.FullName, "settings.json");

    [Given("Flyback could send usage counts and nobody has answered")]
    public void GivenItCouldSend() => Use(new Usage(sink, new Launch(First: true), hold: true));

    [Given("Flyback has nowhere to send usage counts")]
    public void GivenNowhere() => Use(Usage.Off);

    [When("the editor starts")]
    public void WhenItStarts() => editor.Open();

    [Then("the editor asks whether to share usage counts")]
    public void ThenItAsks() => editor.Asking.ShouldNotBeNull("nothing was asked").ShouldContain("usage counts");

    [Then("the editor does not ask about usage counts")]
    public void ThenItDoesNotAsk() => editor.Asking.ShouldBeNull();

    [Then("no usage count has been sent")]
    public void ThenNothingSent() => sink.Events.ShouldBeEmpty();

    [Then("the first start's usage count has been sent")]
    public void ThenTheStartIsSent()
    {
        var started = sink.Events.ShouldHaveSingleItem();

        started.Name.ShouldBe("started");
        started.Props["first"].ShouldBe(true);
    }

    /// <summary>A release reading the settings this run saved. Constructed and never sent to.</summary>
    [Then("the next start does not ask again")]
    public void ThenNotAgain() => Usage.Start(UsageSettings.Load(SettingsPath), new Version(1, 0, 0)).Asks.ShouldBeFalse();

    private void Use(Usage usage) =>
        editor.Setup = new EditorSetup { Folders = new() { SettingsPath = SettingsPath }, Usage = usage };

    public void Dispose()
    {
        editor.Dispose();
        folder.Delete(recursive: true);
    }

    private sealed class Kept : IUsageSink
    {
        public List<UsageEvent> Events { get; } = [];

        public void Send(UsageEvent happened) => Events.Add(happened);

        public void Drain(TimeSpan most) { }
    }
}
