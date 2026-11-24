using System.CommandLine;
using Flyback.Cli.Common;
using Flyback.Core.Graph;
using Flyback.Host;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Settings;
using Flyback.Plugins.Video;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Specs.Steps;

/// <summary>flyback-cli send playing a patch into a picture output a plugin offers, read from the output's side.</summary>
[Binding]
public sealed class PictureOutputSteps(EditorDriver editor) : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-send-specs");

    private readonly Recording recording = new();

    private PluginCatalog catalog = PluginCatalog.Empty;

    private int code;
    private string said = string.Empty;

    [Given("rings drawn across the screen, saved as {string}")]
    public void GivenRingsSaved(string name) =>
        File.WriteAllText(Path.Combine(folder.FullName, name), "rings() |> out.color");

    [Given("a plugin that offers a picture output")]
    public void GivenAnOutput() =>
        catalog = new PluginCatalog([], [], NodeCatalog.BuiltIn, [.. Engine.Graph.Presets.All], [], videoOutputs: [recording]);

    [When("flyback-cli sends {string} to the picture output for {int} second(s) at {int} frames a second")]
    public void WhenSent(string name, int seconds, int fps)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        code = InProcessCli.Run(
            ["send", Path.Combine(folder.FullName, name), "--to", recording.Id, "--size", "64x36", "--fast",
                "--seconds", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture), "--fps", fps.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            new PluginRegistry(() => catalog, folder.FullName, null),
            new InvocationConfiguration { Output = output, Error = error });

        said = output + Environment.NewLine + error;
    }

    [Then("the picture output was sent {int} frames of {int} by {int} pixels")]
    public void ThenSent(int count, int width, int height)
    {
        code.ShouldBe(Exit.Ok, said);
        recording.Opened.ShouldBe(new VideoFormat(width, height, recording.Opened!.Value.FramesPerSecond));
        recording.Times.Count.ShouldBe(count);
        recording.Lengths.ShouldAllBe(length => length >= width * height * 4);
    }

    [Then("each frame shows the patch a tenth of a second after the one before")]
    public void ThenTimed() =>
        recording.Times.Zip(recording.Times.Skip(1), (a, b) => (b - a).TotalSeconds)
            .ShouldAllBe(step => Math.Abs(step - 0.1) < 1e-9);

    [Given("the editor's settings send the picture to it")]
    public void GivenTheEditorSends()
    {
        var path = Path.Combine(folder.FullName, "settings.json");

        new OutputSettings { PictureOutput = recording.Id }.Save(path);
        editor.Setup = editor.Setup with { Plugins = catalog, Folders = editor.Setup.Folders with { SettingsPath = path } };
    }

    [Then("the editor says it is sending the picture to the picture output")]
    public void ThenTheEditorSends() => editor.Reported.ShouldContain($"Sending the picture to {recording.Name}.");

    public void Dispose() => folder.Delete(recursive: true);

    /// <summary>A picture output that keeps when each frame was of, and how many bytes it held.</summary>
    private sealed class Recording : IVideoOutput
    {
        public string Id => "specs.recording";

        public string Name => "Recording";

        public bool IsSupported => true;

        public VideoFormat? Opened { get; private set; }

        public List<TimeSpan> Times { get; } = [];

        public List<int> Lengths { get; } = [];

        public IVideoFeed Create(VideoFormat format, SettingValues settings)
        {
            Opened = format;
            return new Feed(this);
        }

        private sealed class Feed(Recording owner) : IVideoFeed
        {
            public void Send(in VideoFrame frame)
            {
                owner.Times.Add(frame.Time);
                owner.Lengths.Add(frame.Bgra.Length);
            }

            public void Dispose() { }
        }
    }
}
