using Reqnroll;
using Shouldly;
using Flyback.App.Audio;
using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Specs.Support;

namespace Flyback.Specs.Steps;

/// <summary>A patch opened the way the editor and the viewer open one, heard through a device that hands it back.</summary>
[Binding]
public sealed class OpeningSteps : IDisposable
{
    private const int Buffer = 480;

    private readonly IlCompiler compiler = new();
    private readonly Loopback device = new();
    private AudioEngine? engine;
    private Patch? patch;
    private float[]? played;

    [Given("the {string} preset is opened with the sound on")]
    public void GivenOpened(string name)
    {
        patch = Presets.All.Single(p => p.Name == name).Build(NodeCatalog.Current);

        engine = new AudioEngine(device) { Compiler = compiler };
        engine.Update(patch, held: true);
        engine.Start();
    }

    [When("it is listened to for {float} seconds")]
    public async Task WhenListenedTo(float seconds)
    {
        // A held buffer costs nothing, so the whole length would be pulled before
        // the build finished. One buffer while it is building, then the rest.
        var first = Pull(device, (float)Buffer / GlobalConstants.SampleRate);
        await compiler.Settled();

        played = [.. first, .. Pull(device, seconds)];
    }

    [Then("what it played is the preset from its beginning")]
    public void ThenFromTheBeginning()
    {
        var sound = played.ShouldNotBeNull();

        // Held buffers are exact silence and a whole buffer each; what follows them
        // is the preset from nought, as an engine that never waited plays it.
        var waited = 0;
        while (waited < sound.Length && sound.AsSpan(waited, Buffer * 2).IndexOfAnyExcept(0f) < 0) waited += Buffer * 2;

        sound.Length.ShouldBeGreaterThan(waited, "Nothing was heard at all.");

        var reference = new Loopback();
        using var plain = new AudioEngine(reference);
        plain.Update(patch.ShouldNotBeNull());
        plain.Start();

        var expected = Pull(reference, (sound.Length - waited) / 2f / GlobalConstants.SampleRate);

        sound.AsSpan(waited).ToArray().ShouldBe(expected);
    }

    public void Dispose()
    {
        engine?.Dispose();
        compiler.Dispose();
    }

    private static float[] Pull(Loopback from, float seconds)
    {
        var frames = (int)Math.Round(seconds * GlobalConstants.SampleRate);
        var sound = new float[frames * NodeCatalog.AudioChannels];

        for (var at = 0; at < frames; at += Buffer)
            from.Pull(sound.AsSpan(at * 2, Math.Min(Buffer, frames - at) * 2));

        return sound;
    }
}
