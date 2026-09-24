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
    private IReadOnlyList<NodeDef> shipped = [];

    [When("the assistant looks up the Filter")]
    public async Task WhenTheAssistantLooksItUp()
    {
        var bench = new PatchWorkbench(Modules, new Patch());

        var outcome = await bench.InvokeAsync(
            "describe_module",
            System.Text.Json.JsonDocument.Parse($$"""{"type_id":"{{NodeCatalog.FilterTypeId}}"}""").RootElement,
            CancellationToken.None);

        outcome.Ok.ShouldBeTrue(outcome.Text);
        read = outcome.Text;
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
        var helped = filter.Inputs.Select(port => SocketHelp.For(port, input: true))
            .Concat(filter.Outputs.Select(port => SocketHelp.For(port, input: false)))
            .Where(help => help.Length > 0)
            .ToList();

        helped.ShouldNotBeEmpty();

        foreach (var help in helped) read.ShouldContain(help);
    }

    [Given("every shipped module")]
    public void GivenEveryModule() => shipped = Modules.All;

    [Then("the assistant is told what each standard socket is for once")]
    public void ThenEachStandardIsToldOnce()
    {
        read = new PatchWorkbench(Modules, new Patch()).Briefing;

        var lines = read.Split(Environment.NewLine);

        // A line, not a substring: a module's own help may open the way a standard one does.
        foreach (var help in SocketHelp.Inputs.Values.Concat(SocketHelp.Outputs.Values).Append(SocketHelp.Domain).Distinct())
            lines.Count(line => line.EndsWith($": {help}", StringComparison.Ordinal)).ShouldBe(1, help);
    }

    [Then("a module tells the assistant about a socket only where it means something of its own")]
    public void ThenOnlyOwnHelpIsToldOnTheModule() => EachSocket((port, input) =>
        port.Help.Length > 0 && !SocketHelp.Own(port, input) ? "repeats the standard help it would get anyway" : null);

    [Then("each one says what it is for")]
    public void ThenEachIsDescribed() => Each(def => def.Description.Length > 0 ? null : "no description");

    /// <summary>
    /// The name is beside the help wherever it is read, so help that opens with it
    /// reads the name twice.
    /// </summary>
    [Then("no socket's help opens with the socket's own name")]
    public void ThenNoHelpOpensWithItsName() => EachSocket((port, input) =>
        new[] { $"'{port.Name}'", $"{port.Name} is ", $"{port.Name} are ", $"{port.Name}:" }
            .Any(opening => SocketHelp.For(port, input).StartsWith(opening, StringComparison.OrdinalIgnoreCase))
            ? "opens with its own name"
            : null);

    [Then("every socket's help is written in sentences")]
    public void ThenHelpIsSentences() => EachSocket((port, input) => SocketHelp.For(port, input) switch
    {
        "" => null,
        var help when !char.IsLower(help[0]) && help.TrimEnd().EndsWith('.') => null,
        _ => "is not a sentence",
    });

    private void EachSocket(Func<PortSpec, bool, string?> fault) => Each(def =>
    {
        var faults = def.Inputs.Select(port => (Port: port, Input: true))
            .Concat(def.Outputs.Select(port => (Port: port, Input: false)))
            .Select(p => fault(p.Port, p.Input) is { } said ? $"'{p.Port.Name}' {said}: {SocketHelp.For(p.Port, p.Input)}" : null)
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
