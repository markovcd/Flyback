using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Reqnroll;
using Reqnroll.UnitTestProvider;
using Shouldly;
using Flyback.App;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Plugins.Hosting;
using Flyback.Specs.Support;

namespace Flyback.Specs.Steps;

/// <summary>
/// The web viewer's build, run under Node by its own <c>hear.mjs</c>, against the
/// same preset rendered here the way the desktop renders it.
/// </summary>
[Binding]
public sealed class WebViewerSteps(Session session, IUnitTestRuntimeProvider runtime) : IDisposable
{
    private static readonly Lazy<PluginCatalog> Installed = new(PluginHost.Load);

    /// <summary>The size the viewer opens a patch at, which a Scan hears as its aspect.</summary>
    private const int Width = 960, Height = 540;

    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-web-");
    private float[] heard = [];
    private double seconds;
    private JsonNode? said;
    private (string Name, float Value)? turned;
    private (int Note, float From, float To)? struck;

    private List<(string Name, string Heading)> listed = [];

    [When("it plays in the web viewer for {float} second(s)")]
    public void WhenPlayedInTheBrowser(float length) => Play(length);

    [When("it plays in the web viewer for {float} second(s) with its {string} knob at {float}")]
    public void WhenPlayedWithAKnobTurned(float length, string knob, float value)
    {
        turned = (knob, value);
        Play(length, "--knob", $"{knob}={value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
    }

    [When("it plays in the web viewer for {float} second(s) with note {int} held from {float} to {float} seconds")]
    public void WhenPlayedWithANote(float length, int note, float from, float to)
    {
        struck = (note, from, to);

        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        Play(length, "--note", $"{note}:{from.ToString(invariant)}:{to.ToString(invariant)}");

        ((bool?)said!["played"]).ShouldBe(true, "the preset reads no computer keyboard, so a note proves nothing");
    }

    private void Play(float length, params string[] more)
    {
        var output = Path.Combine(folder.FullName, "heard.f32");
        var status = Hear([
            "--preset", session.Presets.Single().Name,
            "--seconds", length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--size", $"{Width}x{Height}",
            "--out", output,
            .. more,
        ]);

        said = JsonNode.Parse(status);

        // The interpreter plays the same samples, so without this a script that failed to build would pass unseen.
        ((string?)said!["soundBackend"]).ShouldBe("javascript");

        heard = MemoryMarshal.Cast<byte, float>(File.ReadAllBytes(output)).ToArray();
        seconds = length;
    }

    /// <summary>
    /// One step of 16 bits is the most a sample may be off by: the maths library
    /// under a browser rounds its last bit where the desktop's may not, and a WAV
    /// cannot hear a difference smaller than that.
    /// </summary>
    [Then("its sound is the desktop's to within one step of 16 bits")]
    [Then("its sound is the desktop's with the same knob turned, to within one step of 16 bits")]
    [Then("its sound is the desktop's with the same note played, to within one step of 16 bits")]
    public void ThenTheDesktopsSound()
    {
        var desktop = Desktop(turned, struck);

        heard.Length.ShouldBe(desktop.Length);
        desktop.ShouldContain(sample => sample != 0f, "the desktop heard silence, so agreeing with it proves nothing");

        var worst = desktop.Zip(heard, (a, b) => Math.Abs(a - b)).Max();
        worst.ShouldBeLessThanOrEqualTo(1f / 32768f);
    }

    [Then("it is not the sound with the knob where it rests")]
    [Then("it is not the sound with nothing played")]
    public void ThenItWasHeard()
    {
        var resting = Desktop(null, null);

        var furthest = resting.Zip(heard, (a, b) => Math.Abs(a - b)).Max();
        furthest.ShouldBeGreaterThan(16f / 32768f, "what was done to it changed nothing a listener could hear");
    }

    [Then("the web viewer says what the preset is for")]
    public void ThenItIsDescribed()
    {
        var preset = session.Presets.Single();

        preset.Description.ShouldNotBeNullOrWhiteSpace();
        ((string?)said!["description"]).ShouldBe(preset.Description);
    }

    /// <summary>
    /// The preset rendered the way the desktop renders it, with <paramref name="knob"/>
    /// turned and <paramref name="note"/> held on the computer keyboard where there are.
    /// </summary>
    private float[] Desktop((string Name, float Value)? knob, (int Note, float From, float To)? note)
    {
        var modules = Installed.Value.Modules;
        var (patch, samples, pictures) = PresetLibrary.Open(session.Presets.Single(), null, modules);

        var program = patch.CompileForAudio(modules, samples: samples, pictures: pictures, played: true).Program;
        var picture = patch.CompileForVideo(modules, samples: samples, pictures: pictures, played: true).Program;
        var live = new LiveValues(program.LiveInputs);
        var shown = new LiveValues(picture.LiveInputs);
        LiveValues[] blocks = [shown, live];
        patch.Seed(live);

        var keys = new VoicePool(MidiSources.Keyboard);

        if (knob is var (name, value))
        {
            var control = patch.Controls?.SingleOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            control.ShouldNotBeNull($"the preset has no knob called {name}");
            live.Set(control.Key, value);
        }

        var speakers = new AudioRenderer { Aspect = SynthRenderer.AspectOf(Width, Height) };
        var memory = speakers.DelayMemoryFor(program);
        var desktop = new float[(int)Math.Round(seconds * speakers.SampleRate) * 2];

        var down = note is var (_, from, _) ? (int)Math.Round(from * speakers.SampleRate) : int.MaxValue;
        var up = note is var (_, _, to) ? (int)Math.Round(to * speakers.SampleRate) : int.MaxValue;

        // In the viewer's buffers, since the knobs and keys are read once a buffer.
        for (var at = 0; at < desktop.Length; at += 2048)
        {
            var frame = at / 2;

            if (down <= frame)
            {
                keys.Down(note!.Value.Note, ComputerKeyboard.Velocity, blocks);
                foreach (var block in blocks) keys.WriteTo(block, blocks);
                down = int.MaxValue;
            }

            if (up <= frame)
            {
                keys.Up(note!.Value.Note);
                foreach (var block in blocks) keys.WriteTo(block, blocks);
                up = int.MaxValue;
            }

            speakers.Render(program, desktop.AsSpan(at, Math.Min(2048, desktop.Length - at)), memory, live);
        }

        return desktop;
    }

    [When("the web viewer lists its presets")]
    public void WhenTheViewerLists() => listed =
    [
        .. JsonNode.Parse(Hear("--presets"))!.AsArray()
            .Select(preset => ((string)preset!["name"]!, (string)preset["heading"]!)),
    ];

    [Then("they are the editor's, in its order and under its headings, less the blank canvas")]
    public void ThenTheEditorsList()
    {
        var editor = PresetLibrary.Ordered(session.Presets, null)
            .Where(preset => preset.Kind != PresetKind.Blank)
            .Select(preset => (preset.Name, PresetKinds.Heading(preset.Kind)))
            .ToList();

        editor.Count.ShouldBeGreaterThan(1);
        listed.ShouldBe(editor);
    }

    /// <summary>Runs the web viewer's build under Node with <paramref name="arguments"/>, and answers what it printed.</summary>
    private string Hear(params string[] arguments)
    {
        var node = Node();
        if (node is null) runtime.TestIgnore("no Node on this machine to run the web viewer with.");

        var build = typeof(WebViewerSteps).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "WebViewer").Value!;

        var start = new ProcessStartInfo(node!, [Path.Combine(build, "hear.mjs"), .. arguments])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = build,
        };

        using var process = Process.Start(start)!;
        var said = process.StandardError.ReadToEndAsync();
        var printed = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        process.ExitCode.ShouldBe(0, said.Result);

        return printed;
    }

    /// <summary>Node on the path, or the one the WebAssembly workload brings with it.</summary>
    private static string? Node()
    {
        var name = OperatingSystem.IsWindows() ? "node.exe" : "node";

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (directory.Length > 0 && File.Exists(Path.Combine(directory, name)))
                return Path.Combine(directory, name);

        var packs = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "packs"));
        if (!Directory.Exists(packs)) return null;

        return Directory.EnumerateDirectories(packs, "Microsoft.NET.Runtime.Emscripten.*.Node.*")
            .SelectMany(pack => Directory.EnumerateFiles(pack, name, SearchOption.AllDirectories))
            .FirstOrDefault();
    }

    public void Dispose() => folder.Delete(recursive: true);
}
