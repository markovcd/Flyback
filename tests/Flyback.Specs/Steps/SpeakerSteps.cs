using System.Globalization;
using Flyback.Core.Graph;
using Reqnroll;
using Shouldly;
using Flyback.Specs.Support;

namespace Flyback.Specs.Steps;

/// <summary>What the speakers play, heard sample by sample across any edits.</summary>
[Binding]
public sealed class SpeakerSteps(PatchContext context)
{
    private const double Tolerance = 1e-3;

    [When("it plays for {float} seconds")]
    [When("it has played {float} seconds")]
    [When("it plays on for {float} seconds")]
    public void WhenItPlays(float seconds) => context.Play(Samples(seconds));

    [When("it plays one more sample")]
    public void WhenOneMoreSample() => context.Play(1);

    /// <summary>
    /// Start, then a tick on every twenty-fourth of a beat up to and including the
    /// last, with the sound played on between them as it would be in the room.
    /// </summary>
    [When(@"^the drum machine starts and plays (\d+) beats? at (\d+) bpm$")]
    public void WhenTheMachinePlays(int beats, int bpm)
    {
        context.Clock.Start();
        context.Push();

        var tick = 60d / bpm / MidiClock.TicksPerBeat;
        var from = context.Now;

        for (var k = 0; k <= beats * MidiClock.TicksPerBeat; k++)
        {
            var at = from + k * tick;

            context.PlayUntil(at);
            context.Clock.Tick(at);
            context.Push();
        }
    }

    [When("the drum machine plays a note on channel {int}")]
    public void WhenTheMachinePlaysOn(int channel) => context.Strike(channel, 60, true);

    [When("the drum machine lets the note on channel {int} go")]
    public void WhenTheMachineLetsGo(int channel) => context.Strike(channel, 60, false);

    [When("the drum machine stops")]
    public void WhenTheMachineStops()
    {
        context.Clock.Stop();
        context.Push();
    }

    /// <summary>Exactly zero, not nearly zero: see AudioRendererTests.</summary>
    [Then("the speakers are silent")]
    public void ThenSilent() => context.RenderAudio().ShouldAllBe(v => v == 0f);

    [Then("the speakers are not silent")]
    public void ThenNotSilent() => context.RenderAudio().Any(v => Math.Abs(v) > 0.01f).ShouldBeTrue();

    /// <summary>Each of the chord's four outputs on the left speaker in turn.</summary>
    [Then(@"^its notes are (\S+), (\S+), (\S+) and (\S+)$")]
    public void ThenItsNotes(string first, string second, string third, string fourth)
    {
        string[] notes = [first, second, third, fourth];

        for (var n = 0; n < notes.Length; n++)
        {
            context.Wire("chord", $"hz{n + 1}", "screen", "left");

            context.Listen(0, 1)[0].ShouldBe(Pitch.Frequency(Note(notes[n])), 1e-2, $"note {n + 1}");
        }
    }

    /// <summary>A note as it is written, "G#4", as its number.</summary>
    private static float Note(string written)
    {
        var sharp = written.Length > 1 && written[1] == '#';
        var octave = int.Parse(written[(sharp ? 2 : 1)..], CultureInfo.InvariantCulture);
        var step = "C D EF G A B".IndexOf(written[0]) + (sharp ? 1 : 0);

        return (octave + 1) * Pitch.Classes + step;
    }

    [Then("the note that comes out is {float}")]
    public void ThenTheNote(float note) => context.SampleAt(0).ShouldBe(note, Tolerance);

    /// <summary>
    /// Heard from scratch at both times, so the only difference between them is
    /// how large the clock reads.
    /// </summary>
    [Then("a second of it an hour in sounds as its first second did")]
    public void ThenAnHourInSoundsTheSame()
    {
        var first = context.Listen(0, PatchContext.SampleRate);
        var late = context.Listen(3600, PatchContext.SampleRate);

        first.Max(Math.Abs).ShouldBeGreaterThan(0.5, "the tone is not sounding");

        for (var i = 0; i < first.Length; i++)
            late[i].ShouldBe(first[i], 1e-6, $"sample {i}");
    }

    [Then("a second of it a week in is noise as loud as its first second")]
    public void ThenAWeekInIsStillNoise()
    {
        var first = context.Listen(0, PatchContext.SampleRate);
        var late = context.Listen(7 * 24 * 3600, PatchContext.SampleRate);

        late.ShouldAllBe(v => Math.Abs(v) <= 1d, "the noise is stuck past the rail");
        late.Distinct().Count().ShouldBeGreaterThan(PatchContext.SampleRate / 4);
        (Rms(late) / Rms(first)).ShouldBeInRange(0.9, 1.1);
    }

    /// <summary>White noise changes sign on about every other sample; a tone or a drift almost never does.</summary>
    [Then("the sound is hiss")]
    public void ThenHiss()
    {
        var heard = context.Listen(0, PatchContext.SampleRate);
        var crossings = heard.Zip(heard.Skip(1)).Count(pair => Math.Sign(pair.First) != Math.Sign(pair.Second));

        crossings.ShouldBeGreaterThan(heard.Length / 3);
    }

    private static double Rms(double[] signal) => Math.Sqrt(signal.Average(v => v * v));

    [Then("both speakers play the same sound")]
    public void ThenBothSpeakersMatch()
    {
        var buffer = context.RenderAudio();

        buffer.Any(v => Math.Abs(v) > 0.01f).ShouldBeTrue("the speakers are silent");

        for (var frame = 0; frame < buffer.Length / 2; frame++)
            buffer[frame * 2 + 1].ShouldBe(buffer[frame * 2], $"frame {frame}");
    }

    [Then("the speakers play {float}")]
    public void ThenTheSpeakersPlay(float expected) => context.SampleAt(0).ShouldBe(expected, Tolerance);

    [Then("the sound is about {float} at {float} seconds")]
    public void ThenTheSoundAt(float expected, float seconds) =>
        context.SampleAt(Samples(seconds)).ShouldBe(expected, Tolerance, $"at {seconds} s");

    [Then(@"^each sample builds on the last: (.+)$")]
    public void ThenEachSampleBuilds(string list)
    {
        var expected = list.Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();

        for (var i = 0; i < expected.Length; i++)
            context.SampleAt(i).ShouldBe(expected[i], Tolerance, $"sample {i}");
    }

    [Then("that sample is about {float}")]
    public void ThenThatSample(float expected) => context.Heard[^1].ShouldBe(expected, Tolerance);

    /// <summary>
    /// A click is a step between neighboring samples steeper than the wave itself
    /// can go: a unit sine at f Hz moves at most 2πf / rate in one sample.
    /// </summary>
    [Then("the sound never clicks")]
    public void ThenNoClick()
    {
        var heard = context.Heard;
        var steepest = 2 * Math.PI * context.HighestFrequency / PatchContext.SampleRate * 1.05;

        heard.Count.ShouldBeGreaterThan(1, "nothing has been played");

        for (var i = 1; i < heard.Count; i++)
            Math.Abs(heard[i] - heard[i - 1])
                .ShouldBeLessThanOrEqualTo(steepest, $"between samples {i - 1} and {i}");
    }

    [Then("about half of its notes play")]
    public void ThenAboutHalfPlay() => ((double)Notes().Count / Offered()).ShouldBeInRange(0.4, 0.6);

    [Then("none of its notes play")]
    public void ThenNonePlay() => Notes().ShouldBeEmpty();

    [Then("every one of its notes plays")]
    public void ThenEveryOnePlays() => Notes().Count.ShouldBe(Offered());

    /// <summary>
    /// As long as the longest, to the sample the step's edges may round to, and as
    /// loud: a note cut in or out halfway is neither.
    /// </summary>
    [Then("every note that plays, plays whole")]
    public void ThenEveryNoteIsWhole()
    {
        var notes = Notes();
        var longest = notes.Max(note => note.Length);

        notes.ShouldAllBe(note => note.Length >= longest - 1 && note.Max() > 0.99);
    }

    private int Offered() => (int)Math.Round(context.Now * context.NotesPerSecond);

    /// <summary>Every stretch of sound between two silences.</summary>
    private List<double[]> Notes()
    {
        var notes = new List<double[]>();
        var note = new List<double>();

        foreach (var sample in context.Heard.Append(0d))
        {
            if (sample > 0d)
            {
                note.Add(sample);
            }
            else if (note.Count > 0)
            {
                notes.Add([.. note]);
                note.Clear();
            }
        }

        return notes;
    }

    private static int Samples(float seconds) => (int)Math.Round(seconds * PatchContext.SampleRate);
}
