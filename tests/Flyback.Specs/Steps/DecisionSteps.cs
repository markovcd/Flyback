using System.Text.Json.Nodes;
using Flyback.Cli.Commands;
using Flyback.Cli.Common;
using Flyback.Cli.Models;
using Flyback.Plugins.Decide;
using Flyback.Plugins.Hosting;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary><c>flyback-cli decide</c> over the decision models laid out under <c>plugins\</c>, with settings of the scenario's own.</summary>
[Binding]
public sealed class DecisionSteps : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-decide-specs");

    private PluginCatalog? catalog;
    private int code;
    private string said = string.Empty;
    private string complained = string.Empty;

    private string SettingsPath => Path.Combine(folder.FullName, "settings.json");

    public void Dispose() => folder.Delete(recursive: true);

    [Given("the decision models that are installed")]
    public void GivenTheModels()
    {
        catalog = PluginHost.Load(PluginHost.DefaultDirectory, PluginTrust.Shipped(PluginHost.DefaultDirectory));

        // The scripted one, which answers without a model or a network.
        new DecisionSettings { Model = "scripted" }.Save(SettingsPath);
    }

    [Given("decisions are turned off")]
    public void GivenOff() => new DecisionSettings { Model = DecisionSettings.Off }.Save(SettingsPath);

    [When("flyback-cli decides whether {string} is about money, as JSON")]
    public async Task WhenDecided(string state)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        code = await DecideCommand.Run(
            catalog.ShouldNotBeNull(),
            new DecideOptions(state, null, null, "Is this about money?", null, [], null, [], false, false, false, Json: true),
            TextReader.Null,
            output,
            error,
            CancellationToken.None,
            SettingsPath,
            Path.Combine(folder.FullName, "models"));

        said = output.ToString();
        complained = error.ToString();
    }

    [Then("the answer is a yes-no with a probability between 0 and 1")]
    public void ThenYesNo()
    {
        code.ShouldBe(Exit.Ok, complained);

        var answer = JsonNode.Parse(said)!["answers"]!.AsObject().ShouldHaveSingleItem().Value!;

        answer["type"]!.GetValue<string>().ShouldBe("noul");
        answer["noul"]!.GetValue<double>().ShouldBeInRange(0, 1);
    }

    [Then("the command fails, saying no decision model is in use")]
    public void ThenRefused()
    {
        code.ShouldBe(Exit.Failed);
        complained.ShouldContain("No decision model is in use");
    }
}
