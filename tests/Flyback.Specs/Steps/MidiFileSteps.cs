using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>A MIDI File playing a file made for the scenario, in a folder of its own.</summary>
[Binding]
public sealed class MidiFileSteps(PatchContext context) : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "flyback-specs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    [Given("a MIDI File playing a note held for half a second")]
    public void GivenOneNote() => Hear(voice: 0, (60, 0, 0.5));

    [Given("a MIDI File on voice {int} playing two notes held together for a second")]
    public void GivenAChord(int voice) => Hear(voice, (60, 0, 1), (64, 0, 1));

    [Given("a MIDI File playing a file that is not there")]
    public void GivenNoFile() => Hear(voice: 0);

    [Then("the patch complains that the MIDI file cannot be read")]
    public void ThenItComplains() =>
        context.Sound.Issues.ShouldContain(issue => issue.Message.Contains("cannot read") && issue.Message.Contains("tune.mid"));

    /// <summary>The module's gate on the left speaker, so the sound is whether the note is held.</summary>
    private void Hear(int voice, params (int Note, double Start, double Length)[] notes)
    {
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, "tune.mid");

        if (notes.Length > 0) MidiFiles.Write(path, notes);

        var player = context.Add("song", NodeCatalog.MidiFileTypeId);

        MidiFileExtra.Set(player, path);
        player.SetState(MidiLineExtra.Name, new System.Text.Json.Nodes.JsonObject { ["voice"] = voice, ["channel"] = 0 });

        context.Add("screen", "output");
        context.Wire("song", "gate", "screen", "left");
        context.SetInput("screen", "volume", 1f);
    }
}
