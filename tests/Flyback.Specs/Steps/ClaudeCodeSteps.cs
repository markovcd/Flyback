using Flyback.Plugins.Assist;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The shipped Claude Code assistant, found on a path of the scenario's own.</summary>
[Binding]
public sealed class ClaudeCodeSteps : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-claude-specs");

    private ScenarioPath? path;
    private IPatchAssistant? assistant;

    private IPatchAssistant Assistant => assistant.ShouldNotBeNull();

    [Given("Claude Code is installed")]
    public void GivenInstalled()
    {
        File.WriteAllText(Path.Combine(folder.FullName, OperatingSystem.IsWindows() ? "claude.exe" : "claude"), string.Empty);
        Point();
    }

    [When("the assistants are listed")]
    public void WhenListed() =>
        assistant = ShippedPlugins.Loaded
            .Assistants.FirstOrDefault(a => a.Id == "claude-code");

    [Then("Claude Code is among them")]
    public void ThenAmongThem() => Assistant.Name.ShouldBe("Claude Code");

    [Then("Claude Code is available")]
    public void ThenAvailable() => Assistant.Unavailable(AssistantConfig.Unset).ShouldBeNull();

    [Then("Claude Code asks for no key")]
    public void ThenNoKey() => Assistant.NeedsKey.ShouldBeFalse();

    /// <summary>Puts the scenario's folder on the path, where a program is found first.</summary>
    private void Point() => path ??= new ScenarioPath(folder.FullName);

    public void Dispose()
    {
        path?.Dispose();
        folder.Delete(recursive: true);
    }
}
