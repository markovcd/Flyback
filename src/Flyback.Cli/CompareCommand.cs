using System.Globalization;
using System.Text.Json;
using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;

namespace Flyback.Cli;

/// <summary>How long and how large two patches are played to be compared.</summary>
internal sealed record CompareOptions(
    double Seconds = 10d,
    int Width = 320,
    int Height = 180,
    double Fps = MovieRenderer.DefaultFrameRate,
    bool Json = false);

/// <summary>Where one sink of two patches first parted, or null where it never did.</summary>
/// <param name="Seconds">When they parted.</param>
/// <param name="Most">The largest difference: a sample's value, or a byte of a pixel.</param>
/// <param name="Count">How many samples or frames differ.</param>
/// <param name="Channel">Which channel the sound parted in.</param>
/// <param name="Frame">Which frame the picture parted in.</param>
internal sealed record Parting(double Seconds, double Most, long Count, string? Channel = null, int? Frame = null);

/// <summary>
/// Plays two patches side by side and says whether they are the same instrument:
/// the same samples and the same pixels, bit for bit.
/// </summary>
/// <remarks>
/// Played rather than compared as programs, because two ports of one patch rarely
/// compile to the same registers and still sound identical, and two identical
/// programs can load different files. The loop is <see cref="MovieRenderer"/>'s, so
/// a Meter or a Scope is lit by the sound it is played with, as in a clip.
/// </remarks>
internal static class CompareCommand
{
    public static int Run(
        Opened was,
        string wasName,
        Opened now,
        string nowName,
        CompareOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellation = default)
    {
        if (Playing.Start(was, wasName, options, error) is not { } first
            || Playing.Start(now, nowName, options, error) is not { } second)
            return Exit.Problems;

        var frames = (int)Math.Round(options.Seconds * options.Fps);
        var channels = NodeCatalog.AudioChannels;

        Parting? sound = null;
        Parting? picture = null;
        var played = 0L;

        for (var frame = 0; frame < frames; frame++)
        {
            if (cancellation.IsCancellationRequested)
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: stopped after {frame} of {frames} frames.");
                return Exit.Failed;
            }

            var due = (long)Math.Round((frame + 1) / options.Fps * first.SampleRate);
            var count = (int)(due - played) * channels;

            sound = Differ(first.Hear(count), second.Hear(count), played * channels, first.SampleRate, sound);
            played = due;

            var time = frame / options.Fps;
            picture = Differ(first.See(time), second.See(time), frame, time, picture);
        }

        var same = sound is null && picture is null;

        if (options.Json)
        {
            output.WriteLine(JsonSerializer.Serialize(
                new
                {
                    was = wasName,
                    now = nowName,
                    same,
                    options.Seconds,
                    frames,
                    options.Width,
                    options.Height,
                    sound,
                    picture,
                },
                Writing.Json));
        }
        else
        {
            Write(wasName, nowName, options, frames, sound, picture, output);
        }

        return same ? Exit.Ok : Exit.Problems;
    }

    private static void Write(
        string was,
        string now,
        CompareOptions options,
        int frames,
        Parting? sound,
        Parting? picture,
        TextWriter output)
    {
        var played = string.Create(
            CultureInfo.InvariantCulture,
            $"{options.Seconds:0.###} s of sound and {Writing.Count(frames, "frame")} at {options.Width}x{options.Height}");

        if (sound is null && picture is null)
        {
            output.WriteLine($"{was} and {now} are the same instrument: {played}, bit for bit.");
            return;
        }

        output.WriteLine($"{was} and {now} are not the same instrument, over {played}.");

        if (sound is not null)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  sound    parts at {sound.Seconds:0.000} s in the {sound.Channel}, by at most {sound.Most:G4} "
                + $"({Decibels(sound.Most)} dB), in {Writing.Count((int)Math.Min(sound.Count, int.MaxValue), "sample")}"));
        }

        if (picture is not null)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  picture  parts at {picture.Seconds:0.000} s in frame {picture.Frame}, by at most "
                + $"{picture.Most:0} of 255, in {Writing.Count((int)picture.Count, "frame")}"));
        }

        static string Decibels(double value) =>
            (20d * Math.Log10(value)).ToString("0.0", CultureInfo.InvariantCulture);
    }

    /// <summary>The sound's parting so far, with one frame's samples added to it.</summary>
    /// <remarks>
    /// Compared as bits, so two NaNs agree and a negative zero differs from a
    /// positive one, as <c>print --check</c> compares constants.
    /// </remarks>
    private static Parting? Differ(
        ReadOnlySpan<float> a, ReadOnlySpan<float> b, long offset, int rate, Parting? parting)
    {
        for (var i = 0; i < a.Length; i++)
        {
            if (BitConverter.SingleToInt32Bits(a[i]) == BitConverter.SingleToInt32Bits(b[i])) continue;

            var most = Math.Abs((double)a[i] - b[i]);
            if (double.IsNaN(most)) most = double.PositiveInfinity;

            var sample = offset + i;

            parting = parting is null
                ? new Parting((double)(sample / NodeCatalog.AudioChannels) / rate, most, 1, Channel: Channel(sample))
                : parting with { Most = Math.Max(parting.Most, most), Count = parting.Count + 1 };
        }

        return parting;

        static string Channel(long sample) =>
            (sample % NodeCatalog.AudioChannels, NodeCatalog.AudioChannels) switch
            {
                (0, 2) => "left",
                (1, 2) => "right",
                var (channel, _) => $"channel {channel + 1}",
            };
    }

    /// <summary>The picture's parting so far, with one frame added to it.</summary>
    private static Parting? Differ(
        ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, int frame, double time, Parting? parting)
    {
        if (a.SequenceEqual(b)) return parting;

        var most = 0;

        for (var i = 0; i < a.Length; i++) most = Math.Max(most, Math.Abs(a[i] - b[i]));

        return parting is null
            ? new Parting(time, most, 1, Frame: frame)
            : parting with { Most = Math.Max(parting.Most, most), Count = parting.Count + 1 };
    }

    /// <summary>One of the two patches, compiled for both sinks and played a frame at a time.</summary>
    private sealed class Playing
    {
        private readonly CompiledPatch video;
        private readonly CompiledPatch audio;
        private readonly SynthRenderer screen = new();
        private readonly AudioRenderer speakers;
        private readonly LiveValues heard;
        private readonly byte[] pixels;
        private readonly int width;
        private readonly int height;
        private float[] samples = [];

        private Playing(CompiledPatch video, CompiledPatch audio, int width, int height)
        {
            this.video = video;
            this.audio = audio;
            this.width = width;
            this.height = height;

            speakers = new AudioRenderer { Aspect = SynthRenderer.AspectOf(width, height) };
            heard = new LiveValues(video.LiveInputs);
            pixels = new byte[width * 4 * height];
        }

        public int SampleRate => speakers.SampleRate;

        /// <summary>
        /// The patch ready to play, or null with its errors said: a stand-in module
        /// plays, so two broken patches could agree about nothing real.
        /// </summary>
        public static Playing? Start(Opened opened, string name, CompareOptions options, TextWriter error)
        {
            var video = opened.Patch.CompileForVideo(samples: opened.Samples, pictures: opened.Pictures);
            var audio = opened.Patch.CompileForAudio(samples: opened.Samples, pictures: opened.Pictures);

            var errors = video.Issues
                .Concat(audio.Issues)
                .Where(i => i.Severity == IssueSeverity.Error)
                .DistinctBy(i => (i.NodeId, i.Message))
                .ToArray();

            foreach (var issue in errors)
                error.WriteLine($"{GlobalConstants.ApplicationName}: {name}: error: {issue.Message}");

            if (errors.Length > 0)
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: refusing to compare a patch with errors in it.");
                return null;
            }

            // Speed only: a program the IL compiler turns away plays the same bytes interpreted.
            IlCompiler.CompileOnce(video.Program, IlParts.Staged);
            IlCompiler.CompileOnce(audio.Program, IlParts.Whole);

            return new Playing(video.Program, audio.Program, options.Width, options.Height);
        }

        /// <summary>Plays the next <paramref name="count"/> samples and lights the meters with them.</summary>
        public ReadOnlySpan<float> Hear(int count)
        {
            if (samples.Length < count) samples = new float[count];

            var span = samples.AsSpan(0, count);

            if (count > 0) speakers.Render(audio, span);

            Meters.Refresh(audio, speakers.Memory, heard);
            Traces.Refresh(video, audio, speakers.Memory);

            return span;
        }

        /// <summary>Draws the frame at <paramref name="time"/>.</summary>
        public ReadOnlySpan<byte> See(double time)
        {
            screen.Render(video, time, width, height, pixels, width * 4, heard);
            return pixels;
        }
    }
}
