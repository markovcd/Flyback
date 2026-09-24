using System.CommandLine;
using Reqnroll;
using Shouldly;
using Flyback.Cli;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Flyback.Specs.Support;

namespace Flyback.Specs.Steps;

/// <summary>
/// flyback-cli asked about patch files on disk, the way a script or an agent asks
/// it: by its arguments, answered by its exit code and what it writes.
/// </summary>
[Binding]
public sealed class CliSteps(PatchContext context) : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-cli-specs");

    private int code;
    private string said = string.Empty;

    [Given("the patch is saved as {string}")]
    public void GivenSaved(string name) => File.WriteAllText(Path(name), PatchIO.ToJson(context.Patch));

    [Given("the text saved as {string}:")]
    public void GivenTextSaved(string name, string text) => File.WriteAllText(Path(name), text);

    [When("flyback-cli checks {string}")]
    public void WhenChecked(string name) => Run("check", Path(name));

    [When("flyback-cli compares {string} with {string}")]
    public void WhenCompared(string first, string second) => Run("compare", Path(first), Path(second));

    [Then("the command succeeds")]
    public void ThenSucceeds() => code.ShouldBe(Exit.Ok, said);

    [Then("the command says the patch has problems")]
    public void ThenProblems() => code.ShouldBe(Exit.Problems, said);

    [Then("it points at line {int}")]
    public void ThenPointsAt(int line) => said.ShouldContain($":{line}:");

    [Then("it says they are the same instrument")]
    public void ThenSame() => said.ShouldContain("are the same instrument");

    [Then("it says they are not the same instrument")]
    public void ThenNotSame() => said.ShouldContain("are not the same instrument");

    public void Dispose() => folder.Delete(recursive: true);

    private string Path(string name) => System.IO.Path.Combine(folder.FullName, name);

    private void Run(params string[] arguments)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        code = Cli.Program.Run(
            arguments,
            new Cli.Plugins(() => PluginCatalog.Empty, folder.FullName, null),
            new InvocationConfiguration { Output = output, Error = error });

        said = output + Environment.NewLine + error;
    }
}
