using System.CommandLine;
using Reqnroll;
using Shouldly;
using Flyback.Cli;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;

namespace Flyback.Specs.Steps;

/// <summary>What a module and its sockets say they are for, as the assistant and the command line read it.</summary>
[Binding]
public sealed class HelpSteps
{
    private static readonly ModuleCatalog Modules = PluginHost.Load().Modules;

    private string read = string.Empty;
    private ToolOutcome? answered;
    private IReadOnlyList<NodeDef> shipped = [];

    [When("the assistant looks up the Filter")]
    public Task WhenTheAssistantLooksItUp() => Asks("describe_module");

    [When("the assistant adds a Filter to the patch")]
    public Task WhenTheAssistantAddsOne() => Asks("add_module");

    [Then("what it read is handbook text")]
    public void ThenItIsHandbook() => answered!.Reference.ShouldBeTrue();

    [Then("what it did is not handbook text")]
    public void ThenItIsNotHandbook() => answered!.Reference.ShouldBeFalse();

    private async Task Asks(string tool)
    {
        var bench = new PatchWorkbench(Modules, new Patch());

        answered = await bench.InvokeAsync(
            tool,
            System.Text.Json.JsonDocument.Parse($$"""{"type_id":"{{NodeCatalog.FilterTypeId}}"}""").RootElement,
            CancellationToken.None);

        answered.Ok.ShouldBeTrue(answered.Text);
        read = answered.Text;
    }

    [When("\"flyback-cli modules\" describes the Filter")]
    public void WhenTheCommandLineDescribesIt()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var code = Cli.Program.Run(
            ["modules", NodeCatalog.FilterTypeId],
            new Cli.Plugins(() => PluginCatalog.Empty, Path.GetTempPath(), null),
            new InvocationConfiguration { Output = output, Error = error });

        code.ShouldBe(Exit.Ok, error.ToString());
        read = output.ToString();
    }

    [Then("it reads what each of the Filter's sockets is for, word for word")]
    public void ThenItReadsEachSocketsHelp()
    {
        var filter = Modules.Require(NodeCatalog.FilterTypeId);
        var helped = filter.Inputs.Select(port => port.Help)
            .Concat(filter.Outputs.Select(port => port.Help))
            .Where(help => help.Length > 0)
            .ToList();

        helped.ShouldNotBeEmpty();

        foreach (var help in helped) read.ShouldContain(help);
    }

    [Given("every shipped module")]
    public void GivenEveryModule() => shipped = Modules.All;

    [Then("the assistant's briefing says what each module is for")]
    public void ThenTheBriefingDescribesEachModule()
    {
        read = new PatchWorkbench(Modules, new Patch()).Briefing;

        Each(def => ExpressionFusion.Retired(def) || read.Contains(def.Description) ? null : "is not described");
    }

    /// <summary>A line, not a substring: a description may quote words a socket's help uses.</summary>
    [Then("it says what no socket is for")]
    public void ThenTheBriefingSaysNoSocketsHelp()
    {
        var lines = read.Split(Environment.NewLine).Select(line => line.Trim()).ToHashSet(StringComparer.Ordinal);

        EachSocket((port, _) => lines.Any(line => line.EndsWith($": {port.Help}", StringComparison.Ordinal)) ? "is in the briefing" : null);
    }

    [Then("each one says what it is for")]
    public void ThenEachIsDescribed() => Each(def => def.Description.Length > 0 ? null : "no description");

    [Then("everything a module carries says what it is for")]
    public void ThenEveryExtraIsHelped() => Each(def =>
    {
        var silent = def.Extras.SelectMany(extra => extra.Explained()).Where(said => said.Help.Length == 0).Select(said => $"'{said.Name}'").ToList();

        return silent.Count == 0 ? null : $"{string.Join(", ", silent)} says nothing";
    });

    [Then("every socket says what it is for")]
    public void ThenEverySocketIsHelped() => EachSocket((port, _) => port.Help.Length > 0 ? null : "says nothing");

    /// <summary>
    /// The name is beside the help wherever it is read, so help that opens with it
    /// reads the name twice.
    /// </summary>
    [Then("no socket's help opens with the socket's own name")]
    public void ThenNoHelpOpensWithItsName() => EachSocket((port, input) =>
        new[] { $"'{port.Name}'", $"{port.Name} is ", $"{port.Name} are ", $"{port.Name}:" }
            .Any(opening => port.Help.StartsWith(opening, StringComparison.OrdinalIgnoreCase))
            ? "opens with its own name"
            : null);

    [Then("every socket's help is written in sentences")]
    public void ThenHelpIsSentences() => EachSocket((port, input) => port.Help switch
    {
        "" => null,
        var help when !char.IsLower(help[0]) && help.TrimEnd().EndsWith('.') => null,
        _ => "is not a sentence",
    });

    private void EachSocket(Func<PortSpec, bool, string?> fault) => Each(def =>
    {
        var faults = def.Inputs.Select(port => (Port: port, Input: true))
            .Concat(def.Outputs.Select(port => (Port: port, Input: false)))
            .Select(p => fault(p.Port, p.Input) is { } said ? $"'{p.Port.Name}' {said}: {p.Port.Help}" : null)
            .OfType<string>()
            .ToList();

        return faults.Count == 0 ? null : string.Join("; ", faults);
    });

    /// <summary>Runs <paramref name="fault"/> on every shipped module and fails once, naming each that had one.</summary>
    private void Each(Func<NodeDef, string?> fault)
    {
        shipped.ShouldNotBeEmpty();

        var faults = shipped
            .Select(def => fault(def) is { } said ? $"{def.TypeId}: {said}" : null)
            .OfType<string>()
            .ToList();

        faults.ShouldBeEmpty(string.Join(Environment.NewLine, faults));
    }
}
