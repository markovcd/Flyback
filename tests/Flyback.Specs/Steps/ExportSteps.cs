using System.CommandLine;
using Flyback.Engine.Graph;
using Reqnroll;
using Reqnroll.UnitTestProvider;
using Shouldly;
using Flyback.Ui.Audio;
using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Engine.Render;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Hosting;
using Flyback.Gpu;
using Flyback.Specs.Support;

using Flyback.Cli.Common;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Specs.Steps;

/// <summary>
/// Exports made the way somebody makes one, with <c>flyback-cli render</c>, and
/// the sound the editor plays, heard through a device that hands it back.
/// </summary>
[Binding]
public sealed class ExportSteps(PatchContext context, IUnitTestRuntimeProvider runtime) : IDisposable
{
    /// <summary>Small and short, so a clip is quick; the claims hold at any size.</summary>
    private const string Size = "64x36";

    private const string Fps = "8";

    /// <summary>How long a sound or a clip is unless a scenario says: a clip's own default is ten seconds.</summary>
    private const float Seconds = 1f;

    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-specs-");
    private readonly List<byte[]> exports = [];
    private float[]? played;

    [When("it is exported as {string} twice")]
    public void WhenExportedTwice(string name)
    {
        Export(name, "first");
        Export(name, "second");
    }

    [When("it is exported as {string} {float} seconds long")]
    public void WhenExportedFor(string name, float seconds) =>
        Export(name, "export", seconds);

    /// <summary>
    /// Pulled a device buffer at a time, as a sound card asks for it, through the
    /// same engine the editor and the viewer play with.
    /// </summary>
    [When("the editor plays it for {float} seconds")]
    public void WhenTheEditorPlays(float seconds)
    {
        const int buffer = 480;

        var device = new Loopback();
        using var engine = new AudioEngine(new AudioSetup(device));

        engine.Update(context.Patch);
        engine.Start();

        var frames = (int)Math.Round(seconds * GlobalConstants.SampleRate);
        var sound = new float[frames * NodeCatalog.AudioChannels];

        for (var at = 0; at < frames; at += buffer)
            device.Pull(sound.AsSpan(at * 2, Math.Min(buffer, frames - at) * 2));

        played = sound;
    }

    [When("it is exported as {string} on the graphics card and on the processor")]
    public void WhenExportedOnBoth(string name)
    {
        using (var probe = HeadlessRenderer.Open(out var why))
            Needs.Tool(runtime, probe is not null, $"no GPU on this machine. {why}");

        Export(name, "gpu", Seconds, "--gpu");
        Export(name, "processor", Seconds, "--processor");
    }

    /// <summary>
    /// A level or so of 255 on average and a few at the worst pixel, which is what
    /// ADR-0035 allows the two; a picture flipped, swizzled or a frame late is far off both.
    /// </summary>
    [Then("the two pictures differ by no more than a shade")]
    public void ThenWithinAShade()
    {
        exports.Count.ShouldBe(2);

        var gpu = PngReader.Read(new MemoryStream(exports[0]), out _).ShouldNotBeNull();
        var processor = PngReader.Read(new MemoryStream(exports[1]), out _).ShouldNotBeNull();

        processor.Pixels.Length.ShouldBe(gpu.Pixels.Length);

        var differences = gpu.Pixels.Select((v, i) => Math.Abs(v - processor.Pixels[i]) * 255f).ToArray();

        differences.Max().ShouldBeLessThanOrEqualTo(8f);
        differences.Average().ShouldBeLessThanOrEqualTo(0.5f);
    }

    [Then("the two files are the same to the byte")]
    public void ThenTheSame()
    {
        exports.Count.ShouldBe(2);
        exports[1].ShouldBe(exports[0]);
    }

    [Then("the exported sound is what the editor played, sample for sample")]
    public void ThenTheExportIsWhatPlayed()
    {
        // Silence matches silence, which would prove nothing.
        played.ShouldNotBeNull().ShouldContain(v => v != 0f);

        var heard = new MemoryStream();
        WavWriter.Write(heard, played, GlobalConstants.SampleRate, NodeCatalog.AudioChannels);

        exports.Single().ShouldBe(heard.ToArray());
    }

    private void Export(string name, string take, float seconds = Seconds, params string[] extra)
    {
        var patch = Path.Combine(folder.FullName, $"{take}.{PatchIO.FileExtension}");
        var into = Path.Combine(folder.FullName, $"{take}-{name}");

        File.WriteAllText(patch, PatchIO.ToJson(context.Patch));

        var error = new StringWriter();

        // A settings file that is not there, so the editor's own on this machine
        // cannot change what gets written.
        string[] args =
        [
            "render", patch, "-o", into, "--size", Size, "--fps", Fps, "--seconds", Invariant(seconds),
            "--settings", Path.Combine(folder.FullName, "none.json"), .. extra,
        ];

        var code = InProcessCli.Run(
            args,
            new PluginRegistry(() => PluginCatalog.Empty, folder.FullName, null),
            new InvocationConfiguration { Output = TextWriter.Null, Error = error });

        code.ShouldBe(Exit.Ok, error.ToString());
        exports.Add(File.ReadAllBytes(into));
    }

    private static string Invariant(float value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public void Dispose() => folder.Delete(recursive: true);

    /// <summary>A sound card that plays nothing and hands back what it was given.</summary>
    private sealed class Loopback : IAudioDevice
    {
        private AudioCallback? fill;

        public int SampleRate => GlobalConstants.SampleRate;

        public bool IsRunning => fill is not null;

        public void Start(AudioCallback callback) => fill = callback;

        public void Stop() => fill = null;

        public void Dispose() => Stop();

        public void Pull(Span<float> buffer) =>
            (fill ?? throw new InvalidOperationException("The engine never started the device."))(buffer);
    }
}
