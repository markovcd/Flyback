using Flyback.Plugins.Assist;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The shipped Codex assistant, found on a path of the scenario's own.</summary>
[Binding]
public sealed class CodexSteps : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-codex-specs");

    private ScenarioPath? path;
    private IPatchAssistant? assistant;

    private IPatchAssistant Assistant => assistant.ShouldNotBeNull();

    [Given("Codex is installed")]
    public void GivenInstalled()
    {
        File.WriteAllText(Path.Combine(folder.FullName, OperatingSystem.IsWindows() ? "codex.exe" : "codex"), string.Empty);
        Point();
    }

    [When("Codex is looked for among the assistants")]
    public void WhenListed() =>
        assistant = ShippedPlugins.Loaded
            .Assistants.FirstOrDefault(a => a.Id == "codex");

    [Then("Codex is among them")]
    public void ThenAmongThem() => Assistant.Name.ShouldBe("Codex");

    [Then("Codex is available")]
    public void ThenAvailable() => Assistant.Unavailable(AssistantConfig.Unset).ShouldBeNull();

    [Then("Codex asks for no key")]
    public void ThenNoKey() => Assistant.NeedsKey.ShouldBeFalse();

    /// <summary>Puts the scenario's folder on the path, where a program is found first.</summary>
    private void Point() => path ??= new ScenarioPath(folder.FullName);

    public void Dispose()
    {
        path?.Dispose();
        folder.Delete(recursive: true);
    }
}
