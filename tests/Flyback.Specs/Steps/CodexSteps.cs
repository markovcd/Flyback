using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The shipped Codex assistant, found on a path of the scenario's own.</summary>
[Binding]
public sealed class CodexSteps : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-codex-specs");
    private readonly string? path = Environment.GetEnvironmentVariable("PATH");

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
        assistant = PluginHost.Load(PluginHost.DefaultDirectory, PluginTrust.Shipped(PluginHost.DefaultDirectory))
            .Assistants.FirstOrDefault(a => a.Id == "codex");

    [Then("Codex is among them")]
    public void ThenAmongThem() => Assistant.Name.ShouldBe("Codex");

    [Then("Codex is available")]
    public void ThenAvailable() => Assistant.Unavailable(AssistantConfig.Unset).ShouldBeNull();

    [Then("Codex asks for no key")]
    public void ThenNoKey() => Assistant.NeedsKey.ShouldBeFalse();

    /// <summary>Puts the scenario's folder on the path, where a program is found first.</summary>
    private void Point() => Environment.SetEnvironmentVariable("PATH", folder.FullName + Path.PathSeparator + path);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", path);
        folder.Delete(recursive: true);
    }
}
