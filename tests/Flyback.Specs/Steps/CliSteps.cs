using System.CommandLine;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Reqnroll;
using Shouldly;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Plugins.Hosting;
using Flyback.Specs.Support;

using Flyback.Cli.Commands;
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

    private const string Bundle = "preset.fbkb";

    private const string ShotName = "shot.png";

    private static readonly Lazy<PluginCatalog> Shipped =
        new(() => PluginHost.Load(PluginHost.DefaultDirectory, PluginTrust.Shipped(PluginHost.DefaultDirectory)));

    private string? packed;

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

    [When("flyback-cli describes {string}")]
    public void WhenFileDescribed(string name) => Run("info", Path(name));

    [When("flyback-cli packs the preset {string}")]
    public void WhenPresetPacked(string name)
    {
        packed = name;
        RunShipped("pack", "--preset", name, "--out", Path(Bundle));
    }

    [When("flyback-cli prints the preset {string}")]
    public void WhenPresetPrinted(string name) => Run("print", "--preset", name);

    [Then("the bundle holds the preset and every file it carries")]
    public void ThenBundleHoldsPreset()
    {
        var preset = Shipped.Value.Presets.Single(p => p.Name == packed);

        using var archive = File.OpenRead(Path(Bundle));
        var bundle = PatchBundle.Read(archive, Shipped.Value.Modules);

        bundle.Patch.Nodes.Count.ShouldBe(preset.Build(Shipped.Value.Modules).Nodes.Count);
        preset.Files.ShouldNotBeNull();
        bundle.Files.Values.Select(Convert.ToBase64String).Order()
            .ShouldBe(preset.Files().Values.Select(Convert.ToBase64String).Order());
    }

    [Then("the command fails, listing the presets there are")]
    public void ThenFailsListingPresets()
    {
        code.ShouldBe(Exit.Failed, said);

        foreach (var preset in PluginCatalog.Empty.Presets) said.ShouldContain($"    {preset.Name}");
    }

    [When("flyback-cli draws the stills")]
    public void WhenStillsDrawn() => Run("stills", "--out", Path("stills"));

    [Then("the index lists every preset in the editor's order, under its heading, each picture with its still")]
    public void ThenEveryPresetIsIndexed()
    {
        var json = File.ReadAllText(Path(System.IO.Path.Combine("stills", StillIndex.FileName)));
        var index = StillIndex.Read(json).ShouldNotBeNull();
        var ordered = PluginCatalog.Empty.Presets.OrderBy(preset => preset.Kind).ToList();

        index.Current.ShouldBeTrue();
        index.Presets.Select(entry => entry.Name).ShouldBe(ordered.Select(preset => preset.Name));
        System.Text.Json.Nodes.JsonNode.Parse(json)!["presets"]!.AsArray().Select(entry => (string)entry!["heading"]!)
            .ShouldBe(ordered.Select(preset => PresetKinds.Heading(preset.Kind)));
        index.Presets.ShouldContain(entry => entry.Still == StillKind.Picture);

        foreach (var entry in index.Presets.Where(entry => entry.Still == StillKind.Picture))
            File.Exists(Path(System.IO.Path.Combine("stills", entry.File.ShouldNotBeNull()))).ShouldBeTrue(entry.Name);
    }

    [When("flyback-cli describes the package")]
    public void WhenPackageDescribed() => Run("plugin", "describe", Path(PackageName));

    [When("flyback-cli shoots {string} at {int} second(s)")]
    public void WhenShot(string name, int seconds)
    {
        var before = ShotCommand.Beside;
        ShotCommand.Beside = BuiltEditor;

        try
        {
            Run("shot", Path(name), "--at", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture), "-o", Path(ShotName));
        }
        finally
        {
            ShotCommand.Beside = before;
        }
    }

    /// <summary>
    /// The editor in its own build output, which is laid out as it ships. This folder holds a
    /// copy of it too, but beside the site's framework, which is not the editor's.
    /// </summary>
    private static string BuiltEditor()
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory);
        var (framework, configuration) = (output.Name, output.Parent!.Name);
        var root = output;

        while (!File.Exists(System.IO.Path.Combine(root.FullName, "Flyback.slnx"))) root = root.Parent!;

        return System.IO.Path.Combine(
            root.FullName, "src", "Flyback.App", "bin", configuration, framework,
            OperatingSystem.IsWindows() ? "Flyback.exe" : "Flyback");
    }

    [Then("the shot is {int} by {int} with a black picture in it")]
    public void ThenShotBlack(int width, int height)
    {
        var shot = Shot();

        (shot.Width, shot.Height).ShouldBe((width, height));
        White(shot).ShouldBeLessThan(0.01);
    }

    /// <summary>The preview is a tenth of the window or more, and nothing else in it is pure white in bulk.</summary>
    [Then("the shot has a white picture in it")]
    public void ThenShotWhite() => White(Shot()).ShouldBeGreaterThan(0.05);

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

    [Then("it says the patch plays for {word}")]
    public void ThenPlaysFor(string length) =>
        Regex.IsMatch(said, $@"length\s+{Regex.Escape(length)}\r?$", RegexOptions.Multiline).ShouldBeTrue(said);

    [Then("it says the patch sets no length")]
    public void ThenNoLength() => said.ShouldMatch(@"length\s+not set");

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

    private LoadedImage Shot() => PngReader.Read(Path(ShotName), out _).ShouldNotBeNull();

    /// <summary>The share of the picture's pixels that are white.</summary>
    private static double White(LoadedImage image)
    {
        var white = 0;

        for (var i = 0; i < image.Pixels.Length; i += 3)
            if (image.Pixels[i] > 0.98f && image.Pixels[i + 1] > 0.98f && image.Pixels[i + 2] > 0.98f) white++;

        return white / (image.Pixels.Length / 3d);
    }

    private void Run(params string[] arguments) => Run(() => PluginCatalog.Empty, arguments);

    /// <summary>Runs with the plugins that ship, as the installed program loads them.</summary>
    private void RunShipped(params string[] arguments) => Run(() => Shipped.Value, arguments);

    private void Run(Func<PluginCatalog> catalog, string[] arguments)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        code = Cli.Program.Run(
            arguments,
            new PluginRegistry(catalog, folder.FullName, null),
            new InvocationConfiguration { Output = output, Error = error });

        said = output + Environment.NewLine + error;
    }
}
