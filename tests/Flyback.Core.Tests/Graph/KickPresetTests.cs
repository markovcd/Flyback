using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The Kick preset, rendered and listened to: struck twice a second, with the
/// pitch falling out from under each strike and the level decaying to silence
/// before the next one.
/// </summary>
/// <remarks>
/// A preset is the one place the modules are checked against each other rather
/// than on their own, and a drum is the case worth doing that for — every part
/// of it is a time, and a time that is wrong sounds wrong rather than looking
/// wrong. What is asserted here is the sound and not the wiring: the tempo as an
/// interval between strikes, the sweep as a pitch that falls, the envelope as a
/// level that reaches nothing and stays there.
/// </remarks>
public class KickPresetTests
{
    private const int Rate = GlobalConstants.SampleRate;

    /// <summary>Loud enough to be the drum rather than the tail of the one before.</summary>
    private const float Audible = 0.05f;

    private static float[] Render(double seconds)
    {
        var audio = Presets.Kick(NodeCatalog.BuiltIn).CompileForAudio();

        audio.HasErrors.ShouldBeFalse(string.Join("; ", audio.Issues.Select(i => i.Message)));

        var program = audio.Program;
        var registers = program.AllocateRegisters();
        var state = new DelayState([], Rate, program.PhaseCount, program.UnitCount);
        var samples = new float[(int)(seconds * Rate)];

        for (var i = 0; i < samples.Length; i++)
        {
            program.Evaluate(0, 0, i / (double)Rate, registers, default, state);
            samples[i] = (float)registers[program.OutputBase];
        }

        return samples;
    }

    /// <summary>Where each strike begins: the first audible sample after a silence.</summary>
    private static int[] Strikes(float[] samples)
    {
        var onsets = new List<int>();
        var quiet = true;

        for (var i = 0; i < samples.Length; i++)
        {
            var loud = MathF.Abs(samples[i]) > Audible;

            if (loud && quiet) onsets.Add(i);
            if (loud) quiet = false;

            // A whole cycle of the lowest note this patch reaches is 22 ms, so
            // ten of silence is the drum having stopped rather than the waveform
            // passing through nothing on its way round.
            else if (i > 0 && Silent(samples, i, Rate / 100)) quiet = true;
        }

        return [.. onsets];
    }

    private static bool Silent(float[] samples, int from, int count)
    {
        for (var i = from; i < Math.Min(from + count, samples.Length); i++)
            if (MathF.Abs(samples[i]) > Audible) return false;

        return true;
    }

    /// <summary>Upward zero crossings over a window, which is the pitch in hertz.</summary>
    private static float Pitch(float[] samples, int from, int count)
    {
        var crossings = 0;

        for (var i = from + 1; i < Math.Min(from + count, samples.Length); i++)
            if (samples[i - 1] <= 0f && samples[i] > 0f) crossings++;

        return crossings / (count / (float)Rate);
    }

    /// <summary>
    /// 120 beats a minute is two a second, and what makes it exactly that is the
    /// Tempo module: 120 into it, two hertz out of it, straight onto the Pulse
    /// that triggers the drum.
    /// </summary>
    [Fact]
    public void It_strikes_twice_a_second()
    {
        var strikes = Strikes(Render(4d));

        strikes.Length.ShouldBe(8, "four seconds at 120 is eight beats");

        for (var beat = 1; beat < strikes.Length; beat++)
            ((strikes[beat] - strikes[beat - 1]) / (float)Rate)
                .ShouldBe(0.5f, 0.005f, $"beat {beat} should land half a second after {beat}");
    }

    /// <summary>
    /// The whole difference between a drum and a beep. The pitch envelope is the
    /// short one, so the note starts up around two hundred hertz and has fallen
    /// to the bottom of its sweep before the level is half gone.
    /// </summary>
    [Fact]
    public void The_pitch_falls_away_under_the_strike()
    {
        var samples = Render(1d);
        var onset = Strikes(samples)[0];

        var beater = Pitch(samples, onset, Rate / 100);
        var shell = Pitch(samples, onset + Rate / 5, Rate / 10);

        beater.ShouldBeGreaterThan(100f, "it should start well above where it settles");
        shell.ShouldBeInRange(35f, 60f, "and settle at the bottom of the sweep");
        beater.ShouldBeGreaterThan(shell * 2f, "the fall is what makes it a drum");
    }

    /// <summary>
    /// Sustain is nothing, so each strike reaches silence on its own rather than
    /// waiting for its gate to close — and the next beat starts from silence
    /// rather than on top of what is left of the last one.
    /// </summary>
    [Fact]
    public void Each_strike_decays_to_silence_before_the_next()
    {
        var samples = Render(2d);
        var strikes = Strikes(samples);

        strikes.Length.ShouldBeGreaterThan(1);

        foreach (var onset in strikes)
        {
            // Two thirds of the way to the next beat, the drum is over: the
            // level envelope's decay is three hundred milliseconds of five
            // hundred.
            var settled = onset + Rate / 3;
            if (settled + Rate / 50 >= samples.Length) continue;

            Silent(samples, settled, Rate / 50)
                .ShouldBeTrue($"the strike at {onset / (float)Rate:0.00}s should be over by then");
        }
    }

    /// <summary>
    /// It is still a drum a long way into a session. The envelope measures how
    /// far the clock moved to know how far to travel, and the clock used to stop
    /// being readable after sixteen seconds — from which point every strike
    /// finished inside one sample and the kick was a click nobody could hear.
    /// </summary>
    /// <remarks>
    /// Rendered through the real <see cref="AudioRenderer"/> rather than by
    /// stepping the program here, because the interval is a property of how the
    /// renderer walks its clock and that is the thing under test.
    /// </remarks>
    [Fact]
    public void It_is_still_a_drum_once_the_clock_has_passed_sixteen()
    {
        var audio = Presets.Kick(NodeCatalog.BuiltIn).CompileForAudio();
        var renderer = new AudioRenderer();

        var buffer = new float[Rate / 10 * 2];      // a tenth of a second a block
        var early = 0f;
        var late = 0f;

        for (var block = 0; block < 300; block++)   // thirty seconds
        {
            renderer.Render(audio.Program, buffer.AsSpan(), AudioScan.TimeDriven);

            var peak = 0f;
            for (var i = 0; i < buffer.Length; i += 2) peak = MathF.Max(peak, MathF.Abs(buffer[i]));

            if (block < 100) early = MathF.Max(early, peak);
            if (block >= 200) late = MathF.Max(late, peak);
        }

        early.ShouldBeGreaterThan(0.5f, "it should be loud in the first ten seconds");
        late.ShouldBeGreaterThan(
            early * 0.9f, "and just as loud in the twenty-first to thirtieth");
    }

    [Fact]
    public void It_never_leaves_what_a_speaker_can_take()
    {
        var samples = Render(2d);

        samples.ShouldAllBe(s => float.IsFinite(s) && Math.Abs(s) <= 1f);
        samples.Max(MathF.Abs).ShouldBeGreaterThan(0.5f, "and it should be loud enough to hear");
    }

    /// <summary>The middle of the screen every ten milliseconds across one beat.</summary>
    private static double[] Beat()
    {
        var video = Presets.Kick(NodeCatalog.BuiltIn).CompileForVideo();

        video.HasErrors.ShouldBeFalse(string.Join("; ", video.Issues.Select(i => i.Message)));

        var registers = video.Program.AllocateRegisters();
        var frames = new double[50];

        for (var i = 0; i < frames.Length; i++)
        {
            video.Program.Evaluate(0, 0, i / 100d, registers, default);
            frames[i] = registers[video.Program.OutputBase];
        }

        return frames;
    }

    /// <summary>
    /// The disc is struck and fades, once a beat. Not the envelope, which has no
    /// memory on the video path and so can only hand over its gate — what is
    /// drawn is a saw at the same tempo, which is the shape the envelope would
    /// be if it could run and is a pure function of time so that it can.
    /// </summary>
    [Fact]
    public void The_picture_is_struck_on_the_beat_and_fades()
    {
        var frames = Beat();

        frames[1].ShouldBeGreaterThan(0.9, "brightest where the drum is struck");

        // Down all the way, and down the whole time: a flash that brightened
        // again halfway would be the ramp running the wrong way round.
        for (var i = 2; i < 35; i++)
            frames[i].ShouldBeLessThanOrEqualTo(
                frames[i - 1] + 1e-9, $"the flash rose again at {i * 10}ms");

        frames[40].ShouldBe(0d, 1e-9, "and is out before the next beat");
    }

    /// <summary>
    /// The reason it is a fade rather than the gate. A gate is open for all but a
    /// fiftieth of each beat, so what the eye got was a lamp that is on with a
    /// ten-millisecond gap in it — shorter than a frame, so whether any frame
    /// caught it was luck, and the disc appeared to blink at whatever rate the
    /// two beat against each other. What replaced it has to be far longer than a
    /// frame at any rate the preview runs at.
    /// </summary>
    [Fact]
    public void The_flash_is_far_longer_than_a_frame()
    {
        var lit = Beat().Count(v => v > 0.1);

        // Ten milliseconds a sample, so this is the flash in hundredths of a
        // second. A frame is a sixtieth at best and a thirtieth commonly.
        lit.ShouldBeGreaterThan(20, "the flash should outlast several frames");
        lit.ShouldBeLessThan(45, "and still be over before the next beat");
    }
}
