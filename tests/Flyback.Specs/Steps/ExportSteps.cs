using System.CommandLine;
using Reqnroll;
using Shouldly;
using Flyback.App.Audio;
using Flyback.Cli;
using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Hosting;
using Flyback.Specs.Support;

namespace Flyback.Specs.Steps;

/// <summary>
/// Exports made the way somebody makes one, with <c>flyback-cli render</c>, and
/// the sound the editor plays, heard through a device that hands it back.
/// </summary>
[Binding]
public sealed class ExportSteps(PatchContext context) : IDisposable
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

    [When("it is exported as {string} once compiled and once interpreted")]
    public void WhenExportedBothWays(string name)
    {
        Export(name, "compiled");
        Export(name, "interpreted", Seconds, "--interpreted");
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
        using var engine = new AudioEngine(device);

        engine.Update(context.Patch);
        engine.Start();

        var frames = (int)Math.Round(seconds * GlobalConstants.SampleRate);
        var sound = new float[frames * NodeCatalog.AudioChannels];

        for (var at = 0; at < frames; at += buffer)
            device.Pull(sound.AsSpan(at * 2, Math.Min(buffer, frames - at) * 2));

        played = sound;
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

        var code = Cli.Program.Run(
            args,
            new Cli.Plugins(() => PluginCatalog.Empty, folder.FullName, null),
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
