using Reqnroll;
using Shouldly;
using Flyback.Plugins.Assist;

namespace Flyback.Specs.Steps;

/// <summary>What the assistant's settings keep, as a file written and read again.</summary>
[Binding]
public sealed class AssistantSteps : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"flyback-assistant-{Guid.NewGuid():N}.json");

    private AssistantSettings settings = new();

    [Given("the assistant's settings as they first are")]
    public void GivenFreshSettings() => settings = AssistantSettings.Load(path);

    [When("the briefing is turned off and the settings are kept")]
    public void WhenTheBriefingIsTurnedOff()
    {
        settings.ShowBriefing = false;
        settings.Save(path);
        settings = AssistantSettings.Load(path);
    }

    [When("the lookups are turned off and the settings are kept")]
    public void WhenTheLookupsAreTurnedOff()
    {
        settings.ShowLookups = false;
        settings.Save(path);
        settings = AssistantSettings.Load(path);
    }

    [Then("the conversation shows the briefing")]
    public void ThenTheBriefingShows() => settings.ShowBriefing.ShouldBeTrue();

    [Then("the conversation does not show the briefing")]
    public void ThenTheBriefingDoesNotShow() => settings.ShowBriefing.ShouldBeFalse();

    [Then("the conversation shows what it looks up")]
    public void ThenTheLookupsShow() => settings.ShowLookups.ShouldBeTrue();

    [Then("the conversation does not show what it looks up")]
    public void ThenTheLookupsDoNotShow() => settings.ShowLookups.ShouldBeFalse();

    public void Dispose() => File.Delete(path);
}
