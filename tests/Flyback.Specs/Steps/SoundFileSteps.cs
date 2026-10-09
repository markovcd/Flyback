using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Render;
using Flyback.Specs.Support;
using Reqnroll;
using Reqnroll.UnitTestProvider;

namespace Flyback.Specs.Steps;

/// <summary>A Sample playing a sound file made for the scenario, in a folder of its own.</summary>
[Binding]
public sealed class SoundFileSteps(PatchContext context, IUnitTestRuntimeProvider runtime) : IDisposable
{
    private static readonly string? Encoder = Ffmpeg.Resolve(null);

    private readonly string folder = Path.Combine(Path.GetTempPath(), "flyback-specs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    [Given("a Sample playing an MP3 made from half a second of a tone")]
    public void GivenAnMp3Playing() => Hear(Mp3(0.5), "out");

    [Given("the length of an MP3 made from half a second of a tone")]
    public void GivenTheLengthOfAnMp3() => Hear(Mp3(0.5), "length");

    private void Hear(string path, string port)
    {
        SampleExtra.Set(context.Add("clip", NodeCatalog.SampleTypeId), path);
        context.Add("screen", "output");
        context.Wire("clip", port, "screen", "left");
        context.SetInput("screen", "volume", 1f);
    }

    /// <summary>A 440 Hz tone at half level, encoded as a take's MP3 is.</summary>
    private string Mp3(double seconds)
    {
        Needs.Tool(runtime, Encoder is not null, "no ffmpeg on this machine");

        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, "tone.mp3");
        var rate = GlobalConstants.SampleRate;
        var tone = new float[(int)(seconds * rate)];

        for (var i = 0; i < tone.Length; i++) tone[i] = 0.5f * MathF.Sin(2f * MathF.PI * 440f * i / rate);

        using var clip = ClipWriter.Open(new ClipTarget(
            path, ClipFormats.ById("mp3")!, SampleRate: rate, Channels: 1, Ffmpeg: Encoder));

        clip.WriteAudio(tone);

        return path;
    }
}
