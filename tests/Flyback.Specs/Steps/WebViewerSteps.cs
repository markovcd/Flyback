using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Reqnroll;
using Reqnroll.UnitTestProvider;
using Shouldly;
using Flyback.Ui;
using Flyback.Core;
using Flyback.Core.Tests.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Render;
using Flyback.Plugins.Hosting;
using Flyback.Specs.Support;
using Flyback.Host;

namespace Flyback.Specs.Steps;

/// <summary>
/// The web viewer's build, run under Node by its own <c>hear.mjs</c>, against the
/// same preset rendered here the way the desktop renders it.
/// </summary>
[Binding]
public sealed class WebViewerSteps(Session session, PatchContext context, IUnitTestRuntimeProvider runtime) : IDisposable
{
    private static readonly Lazy<PluginCatalog> Installed = new(PluginHost.Load);

    /// <summary>The size the viewer opens a patch at, which a Scan hears as its aspect.</summary>
    private const int Width = 960, Height = 540;

    /// <summary>How long one run of the build may take; a run takes seconds.</summary>
    private static readonly TimeSpan Cap = TimeSpan.FromMinutes(2);

    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-web-");
    private float[] heard = [];
    private int oversample = AudioRenderer.DefaultOversample;
    private float[] handed = [];
    private double seconds;
    private JsonNode? said;
    private string? microphone;
    private (string Name, float Value)? turned;
    private (int Note, float From, float To)? struck;

    /// <summary>The desktop's sound through the same edits, where the web editor's were played.</summary>
    private Func<float[]>? edited;

    private List<(string Name, string Heading, string Description)> listed = [];
    private (int Exit, string Printed, string Said)? refused;

    [When("it plays in the web viewer for {float} second(s)")]
    public void WhenPlayedInTheBrowser(float length) => Play(length);

    [When("it plays in the web viewer for {float} second(s), keeping what its sound hands the picture")]
    public void WhenPlayedKeepingTheState(float length)
    {
        var state = Path.Combine(folder.FullName, "handed.f32");
        Play(length, "--state", state);
        handed = MemoryMarshal.Cast<byte, float>(File.ReadAllBytes(state)).ToArray();
    }

    /// <summary>
    /// The Scope's buffer, last in what the worker packs, against the one the desktop's
    /// engine fills from the same second of sound.
    /// </summary>
    [Then("the picture is handed the Scope's chart the desktop draws, to within one step of 16 bits")]
    public void ThenTheDesktopsChart()
    {
        var modules = Installed.Value.Modules;
        var (patch, samples, pictures) = PresetLibrary.Open(session.Presets.Single(), null, modules);

        var program = patch.CompileForAudio(modules, samples: samples, pictures: pictures, played: true).Program;
        var picture = patch.CompileForVideo(modules, samples: samples, pictures: pictures, played: true).Program;
        var live = new LiveValues(program.LiveInputs);
        patch.Seed(live);

        var speakers = new AudioRenderer { Aspect = SynthRenderer.AspectOf(Width, Height) };
        var memory = speakers.DelayMemoryFor(program);
        var sound = new float[(int)Math.Round(seconds * speakers.SampleRate) * 2];

        for (var at = 0; at < sound.Length; at += 2048)
            speakers.Render(program, sound.AsSpan(at, Math.Min(2048, sound.Length - at)), memory, live);

        Traces.Refresh(picture, program, memory);

        var chart = picture.Taps.ShouldHaveSingleItem().Trace.Samples;
        chart.ShouldContain(value => value != 0f, "the desktop's Scope charted silence, so agreeing with it proves nothing");

        handed.Length.ShouldBeGreaterThanOrEqualTo(chart.Length);
        var web = handed[^chart.Length..];

        chart.Zip(web, (a, b) => Math.Abs(a - b)).Max().ShouldBeLessThanOrEqualTo(1f / 32768f);
    }

    [When("it plays in the web viewer for {float} second(s) worked out at {int} times the output rate")]
    public void WhenPlayedAt(float length, int factor)
    {
        oversample = factor;
        Play(length, "--oversample", factor.ToString(System.Globalization.CultureInfo.InvariantCulture));

        ((int?)said!["oversample"]).ShouldBe(factor);
    }

    [When("it plays in the web viewer for {float} second(s) after a warm-up of {float} second(s)")]
    public void WhenPlayedAfterAWarmUp(float length, float warm) =>
        Play(length, "--warm", warm.ToString(System.Globalization.CultureInfo.InvariantCulture));

    [When("it plays in the web viewer for {float} second(s) worked out at {int} times the output rate, judging itself as it plays")]
    public void WhenPlayedJudged(float length, int factor)
    {
        oversample = factor;
        Play(length, "--oversample", factor.ToString(System.Globalization.CultureInfo.InvariantCulture), "--judge");
    }

    /// <summary>Every chunk of 1,024 frames the worker rendered to be heard, and only those, counted to how it keeps pace.</summary>
    [Then("every chunk it played was timed, and none of the warm-up")]
    [Then("every chunk heard, before the edit and after, was timed")]
    public void ThenEveryChunkTimed()
    {
        var chunks = (long)Math.Ceiling(Math.Round(seconds * GlobalConstants.SampleRate) / 1024);

        ((long?)said!["timed"]).ShouldBe(chunks);
        ((long?)said["late"]).ShouldNotBeNull().ShouldBeLessThanOrEqualTo(chunks);
    }

    [Then("it is still worked out at {int} times the output rate, and not said to be behind")]
    public void ThenKeptWhereItWas(int factor)
    {
        ((int?)said!["oversample"]).ShouldBe(factor, $"{said["late"]} of {said["timed"]} chunks came late");
        ((bool?)said["behind"]).ShouldBe(false);
    }

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

    /// <summary>
    /// The preset handed over as text and then edited, as the web editor hands its
    /// worker each edit, with its Output's volume the one thing changed.
    /// </summary>
    [When("it plays in the web editor for {float} second(s), its Output's volume set to {float} by an edit at {float} seconds")]
    public void WhenEditedInThePage(float length, float volume, float at)
    {
        var modules = Installed.Value.Modules;
        var patch = PresetLibrary.Open(session.Presets.Single(), null, modules).Patch;

        patch.IncomingTo(patch.Output.Id, NodeCatalog.OutputVolumePort).ShouldBeNull("the Output's volume is wired, so setting it changes nothing");

        var before = PatchIO.ToJson(patch, modules);
        patch.Output.InputValues[NodeCatalog.OutputVolumePort] = volume;
        var after = PatchIO.ToJson(patch, modules);

        var first = Path.Combine(folder.FullName, "before.fbk");
        var second = Path.Combine(folder.FullName, "after.fbk");
        File.WriteAllText(first, before);
        File.WriteAllText(second, after);

        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        Play(length, "--edit", $"0:{first}", "--edit", $"{at.ToString(invariant)}:{second}");

        edited = () => Edited([before, after], at);
    }

    /// <summary>
    /// The desktop's sound through the same edits, as its engine takes one: the renderer
    /// runs on, the memory is carried into the new program, and the knobs rest where the
    /// editor's panel puts them. In the buffers the web build renders.
    /// </summary>
    private float[] Edited(string[] patches, float at)
    {
        const int buffer = 1024;

        var modules = Installed.Value.Modules;
        var speakers = new AudioRenderer { Aspect = SynthRenderer.AspectOf(Width, Height) };
        CompiledPatch program = CompiledPatch.Silent;
        DelayState? memory = null;
        var live = LiveValues.None;

        void Take(string text)
        {
            var patch = PatchIO.Read(text, modules).Patch;

            program = patch.CompileForAudio(modules, played: true).Program;
            speakers.Prepare(program);
            memory = speakers.DelayMemoryFor(program, memory);
            live = new LiveValues(program.LiveInputs);
            patch.Seed(live);
        }

        Take(patches[0]);

        var frames = (int)Math.Round(seconds * speakers.SampleRate);
        var editAt = (int)Math.Round(at * speakers.SampleRate);
        var sound = new float[frames * 2];

        for (var frame = 0; frame < frames; frame += buffer)
        {
            if (editAt <= frame)
            {
                Take(patches[1]);
                editAt = int.MaxValue;
            }

            speakers.Render(program, sound.AsSpan(frame * 2, Math.Min(buffer, frames - frame) * 2), memory, live);
        }

        return sound;
    }

    private void Play(float length, params string[] more)
    {
        var output = Path.Combine(folder.FullName, "heard.f32");
        var status = Hear([
            .. more.Contains("--edit") ? [] : new[] { "--preset", session.Presets.Single().Name },
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
    [Then("its sound is the desktop's through the same edit, to within one step of 16 bits")]
    [Then("its sound is the desktop's at that rate, to within one step of 16 bits")]
    public void ThenTheDesktopsSound()
    {
        var desktop = edited?.Invoke() ?? Desktop(turned, struck);

        heard.Length.ShouldBe(desktop.Length);
        desktop.ShouldContain(sample => sample != 0f, "the desktop heard silence, so agreeing with it proves nothing");

        var worst = desktop.Zip(heard, (a, b) => Math.Abs(a - b)).Max();
        var parted = desktop.Zip(heard, (a, b) => Math.Abs(a - b)).ToList().FindIndex(off => off > 1f / 32768f);
        worst.ShouldBeLessThanOrEqualTo(1f / 32768f, $"the two part at {parted / 2 / (double)GlobalConstants.SampleRate:0.#####} seconds");
    }

    [Then("it is not the sound with the knob where it rests")]
    [Then("it is not the sound with nothing played")]
    [Then("it is not the sound with nothing edited")]
    public void ThenItWasHeard()
    {
        var resting = Desktop(null, null);

        var furthest = resting.Zip(heard, (a, b) => Math.Abs(a - b)).Max();
        furthest.ShouldBeGreaterThan(16f / 32768f, "what was done to it changed nothing a listener could hear");
    }

    [Then("the web viewer gives it no length, so no end and no seek bar")]
    public void ThenItHasNoLength()
    {
        session.Presets.Single().Build(Installed.Value.Modules).Length.ShouldBeNull("the preset says a length, so this proves nothing");
        said!["length"].ShouldBeNull();
    }

    [When("a patch needing the {string} plugin is opened in the web viewer")]
    public void WhenAPatchNeedingAPluginIsOpened(string plugin)
    {
        var file = Path.Combine(folder.FullName, plugin + ".fbk");
        File.WriteAllBytes(file, PluginPatch.Needing(plugin));

        refused = Run(file, "--seconds", "0.1");
    }

    [Then("the web viewer refuses it, naming {string}")]
    public void ThenRefused(string plugin)
    {
        refused.ShouldNotBeNull();
        refused.Value.Exit.ShouldNotBe(0, "the patch played, its missing module as silence");
        refused.Value.Said.ShouldContain("Not opened.");
        refused.Value.Said.ShouldContain(plugin);
    }

    [When("the patch plays in the web viewer for {float} second(s)")]
    public void WhenThePatchPlays(float length)
    {
        var file = Path.Combine(folder.FullName, "patch.fbk");
        File.WriteAllText(file, PatchIO.ToJson(context.Patch, Installed.Value.Modules));

        var output = Path.Combine(folder.FullName, "heard.f32");

        said = JsonNode.Parse(Hear([
            file,
            "--seconds", length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--size", $"{Width}x{Height}",
            "--out", output,
            .. microphone is null ? [] : new[] { "--input", microphone },
        ]));

        heard = MemoryMarshal.Cast<byte, float>(File.ReadAllBytes(output)).ToArray();
    }

    [Given("the web viewer's microphone hears a {float} Hz tone")]
    public void GivenTheMicrophoneHears(float hertz)
    {
        microphone = Path.Combine(folder.FullName, "microphone.f32");

        var tone = LineInSteps.Tone(hertz, GlobalConstants.SampleRate, 1);
        File.WriteAllBytes(microphone, MemoryMarshal.AsBytes(tone.SelectMany(sample => new[] { sample, sample }).ToArray().AsSpan()).ToArray());
    }

    [Then("the web viewer says it reads a Line In")]
    public void ThenItReadsALineIn() => ((bool?)said!["lineIn"]).ShouldBe(true);

    [Then("the web viewer says it reads no Line In")]
    public void ThenItReadsNoLineIn() => ((bool?)said!["lineIn"]).ShouldBe(false);

    /// <summary>The left speaker's pitch, from its rising crossings of nought, past the first few frames where the filters settle.</summary>
    [Then("the web viewer plays a {float} Hz tone")]
    public void ThenTheWebViewerPlays(float hertz)
    {
        ((string?)said!["soundBackend"]).ShouldBe("javascript", "the script is what carries the microphone, and the interpreter's answer is the desktop's");

        LineInSteps.RisingCrossings([.. heard.Where((_, i) => i % 2 == 0).Skip(2400)], GlobalConstants.SampleRate)
            .ShouldBe(hertz, hertz * 0.02);
    }

    [Then("the web viewer says it has sound")]
    public void ThenItHasSound() => ((bool?)said!["hasSound"]).ShouldBe(true);

    [Then("the web viewer says it has no sound")]
    public void ThenItHasNoSound() => ((bool?)said!["hasSound"]).ShouldBe(false);

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

        var speakers = new AudioRenderer(oversample: oversample) { Aspect = SynthRenderer.AspectOf(Width, Height) };
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
            .Select(preset => ((string)preset!["name"]!, (string)preset["heading"]!, (string)preset["description"]!)),
    ];

    [Then("they are the editor's, in its order, under its headings and with their descriptions, less the blank canvas")]
    public void ThenTheEditorsList()
    {
        var editor = PresetLibrary.Ordered(session.Presets, null)
            .Where(preset => preset.Kind != PresetKind.Blank)
            .Select(preset => (preset.Name, PresetKinds.Heading(preset.Kind), preset.Description))
            .ToList();

        editor.Count.ShouldBeGreaterThan(1);
        listed.ShouldBe(editor);
    }

    /// <summary>Runs the web viewer's build under Node with <paramref name="arguments"/>, and answers what it printed.</summary>
    private string Hear(params string[] arguments)
    {
        var (exit, printed, said) = Run(arguments);
        exit.ShouldBe(0, said);

        return printed;
    }

    /// <summary>Runs the web viewer's build under Node, and answers how it ended and what it printed and said.</summary>
    private (int Exit, string Printed, string Said) Run(params string[] arguments)
    {
        var node = NodeJs.Path;
        Needs.Tool(runtime, node is not null, "no Node on this machine to run the web viewer with");

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
        var printed = process.StandardOutput.ReadToEndAsync();

        if (!process.WaitForExit(Cap))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException($"hear.mjs ran past {Cap.TotalSeconds:0} s and was ended: {said.Result}");
        }

        return (process.ExitCode, printed.Result, said.Result);
    }

    public void Dispose() => folder.Delete(recursive: true);
}
