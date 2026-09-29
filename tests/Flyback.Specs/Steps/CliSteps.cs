using System.CommandLine;
using System.IO.Compression;
using System.Security.Cryptography;
using Reqnroll;
using Shouldly;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Plugins.Hosting;
using Flyback.Specs.Support;

using Flyback.Cli.Common;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Specs.Steps;

/// <summary>
/// flyback-cli asked about patch files on disk, the way a script or an agent asks
/// it: by its arguments, answered by its exit code and what it writes.
/// </summary>
[Binding]
public sealed class CliSteps(PatchContext context) : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-cli-specs");

    private const string PackageName = "figures.fbkp";

    private int code;
    private string said = string.Empty;
    private PackageSigner? signer;

    [Given("the patch is saved as {string}")]
    public void GivenSaved(string name) => File.WriteAllText(Path(name), PatchIO.ToJson(context.Patch));

    [Given("the text saved as {string}:")]
    public void GivenTextSaved(string name, string text) => File.WriteAllText(Path(name), text);

    [When("flyback-cli checks {string}")]
    public void WhenChecked(string name) => Run("check", Path(name));

    [When("flyback-cli compares {string} with {string}")]
    public void WhenCompared(string first, string second) => Run("compare", Path(first), Path(second));

    [Given("a plugin package signed by its author")]
    public void GivenPackage() => File.WriteAllBytes(Path(PackageName), Package());

    [Given("a plugin package changed after it was signed")]
    public void GivenChangedPackage()
    {
        using var memory = new MemoryStream();
        memory.Write(Package());

        using (var archive = new ZipArchive(memory, ZipArchiveMode.Update, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("any/added.txt").Open());
            writer.Write("added after signing");
        }

        File.WriteAllBytes(Path(PackageName), memory.ToArray());
    }

    [When("flyback-cli describes the preset {string}")]
    public void WhenDescribed(string name) => Run("info", "--preset", name);

    [When("flyback-cli draws the stills")]
    public void WhenStillsDrawn() => Run("stills", "--out", Path("stills"));

    [Then("the index lists every preset, each picture with its still")]
    public void ThenEveryPresetIsIndexed()
    {
        var index = StillIndex.Read(File.ReadAllText(Path(System.IO.Path.Combine("stills", StillIndex.FileName)))).ShouldNotBeNull();

        index.Current.ShouldBeTrue();
        index.Presets.Select(entry => entry.Name).ShouldBe(PluginCatalog.Empty.Presets.Select(preset => preset.Name));
        index.Presets.ShouldContain(entry => entry.Still == StillKind.Picture);

        foreach (var entry in index.Presets.Where(entry => entry.Still == StillKind.Picture))
            File.Exists(Path(System.IO.Path.Combine("stills", entry.File.ShouldNotBeNull()))).ShouldBeTrue(entry.Name);
    }

    [When("flyback-cli describes the package")]
    public void WhenPackageDescribed() => Run("plugin", "describe", Path(PackageName));

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

    [Then("it says what the picture costs")]
    public void ThenPictureCost() => said.ShouldMatch(@"picture\s+\d+ ops");

    [Then("it names the modules the package declares")]
    public void ThenNamesModules() => said.ShouldContain("Plate (flyback.figures.plate)");

    [Then("it names the key that signed it")]
    public void ThenNamesKey() => said.ShouldContain($"key {signer!.Fingerprint}");

    [Then("the command says the package is refused")]
    public void ThenRefused()
    {
        code.ShouldBe(Exit.Problems, said);
        said.ShouldContain("refused");
    }

    public void Dispose() => folder.Delete(recursive: true);

    /// <summary>The shipped Figures build, packed for any system and signed with a key of its own.</summary>
    private byte[] Package()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        signer = PackageSigner.Of(key);

        return PackageSigner.Sign(PluginPackage.Pack([(PluginPackage.AnyPlatform, System.IO.Path.Combine(PluginHost.DefaultDirectory, "Figures"))]), key);
    }

    private string Path(string name) => System.IO.Path.Combine(folder.FullName, name);

    private void Run(params string[] arguments)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        code = Cli.Program.Run(
            arguments,
            new PluginRegistry(() => PluginCatalog.Empty, folder.FullName, null),
            new InvocationConfiguration { Output = output, Error = error });

        said = output + Environment.NewLine + error;
    }
}
