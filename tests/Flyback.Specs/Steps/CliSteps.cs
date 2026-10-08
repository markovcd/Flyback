using System.CommandLine;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Reqnroll;
using Reqnroll.UnitTestProvider;
using Shouldly;
using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Language;
using Flyback.Engine.Render;
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
public sealed class CliSteps(PatchContext context, IUnitTestRuntimeProvider runtime) : IDisposable
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

    /// <summary>A mono 16-bit WAV of a sine, a second long at the engine's rate.</summary>
    [Given("a {float} Hz tone saved as {string}")]
    public void GivenToneSaved(float hertz, string name)
    {
        var tone = LineInSteps.Tone(hertz, GlobalConstants.SampleRate, 1);
        var data = new byte[tone.Length * 2];

        for (var i = 0; i < tone.Length; i++)
            BitConverter.TryWriteBytes(data.AsSpan(i * 2), (short)(tone[i] * short.MaxValue));

        using var file = File.Create(Path(name));
        using var writer = new BinaryWriter(file);

        writer.Write("RIFF"u8);
        writer.Write(36 + data.Length);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(GlobalConstants.SampleRate);
        writer.Write(GlobalConstants.SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(data.Length);
        writer.Write(data);
    }

    /// <summary>A sine across and the same sine a quarter turn on up, at half full scale.</summary>
    [Given("a circle played as oscilloscope music onto a Beam, saved as {string}")]
    public void GivenCircleSaved(string name) =>
        File.WriteAllText(Path(name), """
            let left = sine(freq: 100, amp: 0.5)
            let right = sine(freq: 100, phase: 0.25, amp: 0.5)
            left |> out.left
            right |> out.right
            beam(x: left, y: right) |> out.color
            """);

    [When("flyback-cli draws a still of {string} at {float} second(s)")]
    public void WhenStill(string name, float seconds) => Still(Run, name, seconds);

    [When("flyback-cli, with the plugins, draws a still of {string} at {float} second(s)")]
    public void WhenStillWithPlugins(string name, float seconds) => Still(RunShipped, name, seconds);

    private void Still(Action<string[]> run, string name, float seconds)
    {
        run([
            "render", Path(name), "-o", Path(ShotName), "--size", "640x360", "--processor",
            "--at", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ]);

        code.ShouldBe(0, said);
    }

    [Then("the still is a ring half as wide as the picture is tall, dark inside and out")]
    public void ThenRing()
    {
        var still = Shot();

        // The brightest green across a few pixels either side of a radius, so a
        // line a pixel wide is found wherever it falls between pixel centers.
        double Green(double x, double y)
        {
            Span<double> rgb = stackalloc double[3];
            var most = 0d;

            for (var step = -4; step <= 4; step++)
            {
                var along = 1d + step * 0.01d;
                still.At(x * along, y * along, rgb);
                most = Math.Max(most, rgb[1]);
            }

            return most;
        }

        new[] { Green(0.5, 0), Green(0, 0.5), Green(-0.5, 0), Green(0, -0.5) }.ShouldAllBe(lit => lit > 0.3);

        Green(0.05, 0).ShouldBeLessThan(0.02);
        Green(0.9, 0).ShouldBeLessThan(0.02);
    }

    [Then("the still is lit round a square half as wide as the picture is tall, dark inside and out")]
    public void ThenSquare()
    {
        var still = Shot();

        // The brightest green a few pixels either side of each side's middle and corner.
        double Green(double x, double y)
        {
            Span<double> rgb = stackalloc double[3];
            var most = 0d;

            for (var step = -4; step <= 4; step++)
            {
                var along = 1d + step * 0.01d;
                still.At(x * along, y * along, rgb);
                most = Math.Max(most, rgb[1]);
            }

            return most;
        }

        new[] { Green(0.5, 0), Green(0, 0.5), Green(-0.5, 0), Green(0, -0.5), Green(0.5, 0.5), Green(-0.5, -0.5) }
            .ShouldAllBe(lit => lit > 0.3);

        Green(0.05, 0).ShouldBeLessThan(0.02);
        Green(0.25, 0.25).ShouldBeLessThan(0.02);
        Green(0.9, 0).ShouldBeLessThan(0.02);
    }

    /// <summary>
    /// A loop is a trace that goes out to an edge and back, so a line just inside
    /// that edge crosses it twice for every loop.
    /// </summary>
    [Then("the still has {int} loops along its top and {int} along its side")]
    public void ThenLoops(int across, int up)
    {
        var still = Shot();

        int Crossings(Func<double, (double X, double Y)> along)
        {
            Span<double> rgb = stackalloc double[3];
            var crossings = 0;
            var lit = false;

            for (var step = -99; step <= 99; step++)
            {
                var (x, y) = along(step / 100d);
                still.At(x, y, rgb);

                if (rgb[1] > 0.25 && !lit) crossings++;
                lit = rgb[1] > 0.25;
            }

            return crossings;
        }

        Crossings(t => (t, 0.8)).ShouldBe(across * 2, "crossings near the top");
        Crossings(t => (0.8, t)).ShouldBe(up * 2, "crossings near the side");
    }

    [Then("the still is black")]
    public void ThenStillBlack() => Shot().Pixels.ShouldAllBe(value => value == 0f);

    [Then("{string} plays a {float} Hz tone")]
    public void ThenPlaysATone(string written, float hertz)
    {
        var clip = WavReader.Read(Path(written), out var fault).ShouldNotBeNull(fault.ToString());

        LineInSteps.RisingCrossings([.. clip.Samples.Skip(200)], clip.SampleRate).ShouldBe(hertz, hertz * 0.02);
    }

    [Then("{string} is silent")]
    public void ThenIsSilent(string written) =>
        WavReader.Read(Path(written), out var fault).ShouldNotBeNull(fault.ToString()).Samples.ShouldAllBe(v => v == 0f);

    [Given("the editor's settings oversample the sound as they please")]
    public void GivenNoOversampleSetting() => File.WriteAllText(Path("settings.json"), "{}");

    [Given("the editor's settings oversample the sound {int} times")]
    public void GivenOversampleSetting(int factor) => new Ui.OutputSettings { Oversample = factor }.Save(Path("settings.json"));

    [When("flyback-cli renders {string} as {string} for {float} seconds")]
    public void WhenRendered(string patch, string into, float seconds) => WhenRenderedWith(patch, into, seconds, "");

    [When("flyback-cli renders {string} as {string} for {float} seconds, {}")]
    public void WhenRenderedWith(string patch, string into, float seconds, string flags)
    {
        Run([
            "render", Path(patch), "-o", Path(into),
            "--seconds", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--settings", Path("settings.json"),
            .. flags.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Beside),
        ]);

        code.ShouldBe(0, said);
    }

    [When("flyback-cli renders the preset {string} as {string} for {float} seconds, {}")]
    public void WhenPresetRenderedWith(string name, string into, float seconds, string flags)
    {
        RunShipped([
            "render", "--preset", name, "-o", Path(into),
            "--seconds", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--settings", Path("settings.json"),
            .. flags.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Beside),
        ]);

        code.ShouldBe(0, said);
    }

    /// <summary>The file against the patch rendered here at <paramref name="factor"/>, and unlike it at every other factor.</summary>
    [Then("{string} is the patch's sound worked out at {int} times the output rate")]
    public void ThenWorkedOutAt(string written, int factor)
    {
        var heard = WavReader.Read(Path(written), out var fault).ShouldNotBeNull(fault.ToString()).Samples;
        var program = PatchLanguage.Build(File.ReadAllText(Path("saw.fbks")), NodeCatalog.BuiltIn).Patch.CompileForAudio().Program;

        float Furthest(int at)
        {
            var stereo = new float[heard.Length * 2];
            new AudioRenderer(oversample: at).Render(program, stereo);

            return heard.Select((sample, i) => MathF.Abs(sample - (stereo[i * 2] + stereo[i * 2 + 1]) / 2)).Max();
        }

        Furthest(factor).ShouldBeLessThanOrEqualTo(2f / 32768f, "a step of 16 bits either way");

        foreach (var other in AudioRenderer.Oversamples.Where(other => other != factor))
            Furthest(other).ShouldBeGreaterThan(8f / 32768f, $"{other}× would have written the same file, so this proves nothing");
    }

    [When("flyback-cli checks {string}")]
    public void WhenChecked(string name) => Run("check", Path(name));

    [When("flyback-cli, with the plugins, checks {string}")]
    public void WhenCheckedWithPlugins(string name) => RunShipped("check", Path(name));

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

    [When("flyback-cli measures {string}")]
    public void WhenMeasured(string name) => Run("measure", Path(name), "--seconds", "3", "--json");

    [Then("it says {string} swings from {int} to {int}, {int} times a second")]
    public void ThenSwings(string socket, int low, int high, int hz)
    {
        var dot = socket.LastIndexOf('.');
        var json = said[..said.LastIndexOf('}')] + "}";

        var sound = JsonNode.Parse(json)!["measurements"]!.AsArray()
            .Single(m => (string?)m!["module"] == socket[..dot] && (string?)m["socket"] == socket[(dot + 1)..])!["sound"]![0]!;

        ((double)sound["min"]!).ShouldBe(low, 1e-3);
        ((double)sound["max"]!).ShouldBe(high, 1e-3);
        ((double)sound["hz"]!).ShouldBe(hz, 0.01);
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

    [When("flyback-cli saves the preset {string} as {string}")]
    public void WhenPresetSaved(string name, string into) => RunShipped("save", "--preset", name, "--out", Path(into));

    [Then("{string} opens as the preset {string}")]
    public void ThenOpensAsPreset(string saved, string name)
    {
        var preset = Shipped.Value.Presets.Single(p => p.Name == name).Build(Shipped.Value.Modules);
        var opened = PatchFile.Open(new FileInfo(Path(saved)), null).Patch.ShouldNotBeNull().Patch;

        opened.CompileForAudio(Shipped.Value.Modules).Program.Ops
            .ShouldBe(preset.CompileForAudio(Shipped.Value.Modules).Program.Ops);
    }

    [Then("the command says to save it as a bundle to take its recordings along")]
    public void ThenSaysBundle()
    {
        code.ShouldBe(Exit.Problems, said);
        said.ShouldContain($"save it as {PatchBundle.Extension}");
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
    public void WhenStillsDrawn()
    {
        if (Ffmpeg.OnPath() is null) runtime.TestIgnore("no ffmpeg on this machine, and the stills are WebP");

        Run("stills", "--out", Path("stills"));
    }

    [Then("the index lists every preset in the editor's order, under its heading, each picture with its still")]
    public void ThenEveryPresetIsIndexed()
    {
        var json = File.ReadAllText(Path(System.IO.Path.Combine("stills", StillIndex.FileName)));
        var index = StillIndex.Read(json).ShouldNotBeNull();
        var ordered = PluginCatalog.Empty.Presets.OrderBy(preset => preset.Kind).ToList();

        index.Current.ShouldBeTrue();
        index.Presets.Select(entry => entry.Name).ShouldBe(ordered.Select(preset => preset.Name));
        JsonNode.Parse(json)!["presets"]!.AsArray().Select(entry => (string)entry!["heading"]!)
            .ShouldBe(ordered.Select(preset => PresetKinds.Heading(preset.Kind)));
        index.Presets.ShouldContain(entry => entry.Still == StillKind.Picture);

        foreach (var entry in index.Presets.Where(entry => entry.Still == StillKind.Picture))
            File.Exists(Path(System.IO.Path.Combine("stills", entry.File.ShouldNotBeNull()))).ShouldBeTrue(entry.Name);
    }

    [Then("the index gives every preset the tags the presets page filters by")]
    public void ThenEveryPresetIsTaggedInTheIndex()
    {
        var index = StillIndex.Read(File.ReadAllText(Path(System.IO.Path.Combine("stills", StillIndex.FileName)))).ShouldNotBeNull();
        var tagged = PluginCatalog.Empty.Presets.ToDictionary(preset => preset.Name, preset => preset.Tags);

        foreach (var entry in index.Presets)
            entry.Tags.ShouldNotBeNull(entry.Name).ShouldBe(tagged[entry.Name].ShouldNotBeNull(entry.Name), entry.Name);
    }

    [When("flyback-cli describes the package")]
    public void WhenPackageDescribed() => Run("plugin", "describe", Path(PackageName));

    [When("flyback-cli shoots {string} at {int} second(s)")]
    public void WhenShot(string name, int seconds) => Shoot(name, seconds);

    [When("flyback-cli shoots {string} at {int} second(s), cropped to the modules")]
    public void WhenShotCropped(string name, int seconds) => Shoot(name, seconds, "--crop");

    [When("flyback-cli shoots {string} at {int} second(s), with the assistant's column open")]
    public void WhenShotWithAssistant(string name, int seconds) => Shoot(name, seconds, "--assistant");

    [Then("the shot has the assistant's column at its left")]
    public void ThenAssistantColumn() => AssistantBadge(Shot()).ShouldBeTrue();

    [Then("the shot has no assistant's column")]
    public void ThenNoAssistantColumn() => AssistantBadge(Shot()).ShouldBeFalse();

    /// <summary>Whether the column's teal badge sits at its top left, beside the word Assistant.</summary>
    private static bool AssistantBadge(LoadedImage shot)
    {
        var at = (73 * shot.Width + 26) * 3;
        var (r, g, b) = (shot.Pixels[at], shot.Pixels[at + 1], shot.Pixels[at + 2]);

        return g - r > 0.3f && b - r > 0.3f;
    }

    [When("flyback-cli shoots {string} with the editor {string}")]
    public void WhenShotWithEditor(string name, string editor) =>
        Run(["shot", Path(name), "-o", Path(ShotName), "--editor", Path(editor)]);

    [Then("the command says the editor is not there")]
    public void ThenSaysNoEditor()
    {
        code.ShouldBe(Exit.Failed, said);
        said.ShouldContain("the editor is not here");
    }

    private void Shoot(string name, int seconds, params string[] more)
    {
        Run(["shot", Path(name), "--at", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture), "-o", Path(ShotName), "--editor", BuiltEditor(), .. more]);
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
            root.FullName, "src", "Flyback.Editor.Desktop", "bin", configuration, framework,
            OperatingSystem.IsWindows() ? "Flyback.exe" : "Flyback");
    }

    [Then("the shot is {int} by {int} with a black picture in it")]
    public void ThenShotBlack(int width, int height)
    {
        var shot = Shot();

        (shot.Width, shot.Height).ShouldBe((width, height));
        White(shot).ShouldBeLessThan(0.01);
    }

    /// <summary>A Sine and the Output beside it, with nothing of the toolbar or the preview around them.</summary>
    [Then("the shot is two modules side by side, smaller than the window")]
    public void ThenShotCropped()
    {
        var shot = Shot();

        shot.Width.ShouldBeLessThan(1440 * 2 / 3);
        shot.Height.ShouldBeLessThan(900 / 2);
        shot.Width.ShouldBeGreaterThan(shot.Height * 2);
    }

    /// <summary>The preview is a tenth of the window or more, and nothing else in it is pure white in bulk.</summary>
    [Then("the shot has a white picture in it")]
    public void ThenShotWhite() => White(Shot()).ShouldBeGreaterThan(0.05);

    [Then("the command succeeds")]
    public void ThenSucceeds() => code.ShouldBe(Exit.Ok, said);

    [Then("the command says the patch has problems")]
    public void ThenProblems() => code.ShouldBe(Exit.Problems, said);

    [Then("it names {string}")]
    public void ThenNames(string name) => said.ShouldContain(name);

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

    /// <summary>A flag's value that names a file saved by an earlier step is that file; anything else is itself.</summary>
    private string Beside(string argument) => File.Exists(Path(argument)) ? Path(argument) : argument;

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
