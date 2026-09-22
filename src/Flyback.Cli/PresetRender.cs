using System.Globalization;
using System.Text.Json;
using Flyback.Core.Graph;

namespace Flyback.Cli;

/// <summary>
/// Makes one preset's still, loop and track, and puts them in the share.
/// </summary>
/// <remarks>
/// The still is 1280x720 at four seconds in, as webp at quality 88. The loop is six
/// silent seconds at 640x360 in VP9. The track is the first thirty seconds brought
/// to -16 LUFS with a four-second fade, as the Pages site's tracks are. A patch that
/// wires only a picture or only a sound gets only that half, and a silent one no
/// track at all.
/// </remarks>
internal sealed class PresetRender(IPresetTools tools, MediaWriter media)
{
    public const double StillAt = 4;
    public const double LoopSeconds = 6;
    public const double TrackSeconds = 30;
    public const double FadeSeconds = 4;

    private const string Loudnorm = "loudnorm=I=-16:TP=-1.5:LRA=11";

    /// <summary>Whether the preset has neither its media nor a failed render yet.</summary>
    public bool Pending(string id) => media.Pending(id);

    /// <summary>Renders the patch in <paramref name="file"/> as the preset <paramref name="id"/>.</summary>
    /// <returns>Null where it rendered, or why it did not.</returns>
    public async Task<string?> Render(string id, FileInfo file, CancellationToken cancellation)
    {
        var work = Directory.CreateTempSubdirectory("flyback-render-").FullName;

        try
        {
            foreach (var (suffix, made) in await Make(file, work, cancellation)) media.Put(id, suffix, made);

            media.Done(id);

            return null;
        }
        catch (Failure failure)
        {
            media.Failed(id, failure.Message);

            return failure.Message;
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); }
            catch (IOException) { }
        }
    }

    /// <summary>The preset's files in <paramref name="work"/>, each with the suffix it is served under.</summary>
    private async Task<List<(string Suffix, string File)>> Make(FileInfo file, string work, CancellationToken cancellation)
    {
        // A patch short of a plugin still opens, and a render of it would be short of
        // the same modules.
        var open = PatchFile.Open(file);

        if (open.Problems.Count > 0 || open.Patch is not { } patch)
            throw new Failure("The patch did not open whole.\n" + string.Join('\n', open.Problems));

        var reaches = patch.Patch.Reaches();
        var made = new List<(string Suffix, string File)>();

        if (reaches.Picture)
        {
            var png = Path.Combine(work, "still.png");
            var webp = Path.Combine(work, "still.webp");
            var raw = Path.Combine(work, "loop-raw.webm");
            var loop = Path.Combine(work, "loop.webm");

            Render(patch, "the still", new RenderOptions(new FileInfo(png), 1280, 720, At: StillAt), cancellation);
            await Ffmpeg("the still's webp", cancellation, "-i", png, "-c:v", "libwebp", "-quality", "88", webp);

            Render(patch, "the loop", new RenderOptions(new FileInfo(raw), 640, 360, Seconds: LoopSeconds), cancellation);
            await Ffmpeg("the silent loop", cancellation, "-i", raw, "-an", "-c:v", "copy", loop);

            made.Add((".webp", webp));
            made.Add((".webm", loop));
        }

        if (reaches.Sound)
        {
            var wav = Path.Combine(work, "track.wav");
            var mp3 = Path.Combine(work, "track.mp3");
            var bars = Path.Combine(work, "peaks.json");

            Render(patch, "the track", new RenderOptions(new FileInfo(wav), Seconds: TrackSeconds), cancellation);

            // At ffmpeg's default log level, since the measurement is logged.
            var measuring = await Checked("measuring the track", cancellation,
                "-hide_banner", "-nostats", "-i", wav, "-af", Loudnorm + ":print_format=json", "-f", "null", "-");

            if (Measured(measuring.Error) is { } measured)
            {
                var filter = string.Create(CultureInfo.InvariantCulture,
                    $"{Loudnorm}:measured_I={measured.I}:measured_TP={measured.Tp}:measured_LRA={measured.Lra}:measured_thresh={measured.Thresh}:offset={measured.Offset}:linear=true,afade=t=out:st={TrackSeconds - FadeSeconds}:d={FadeSeconds}");

                await Ffmpeg("the track's mp3", cancellation, "-i", wav, "-af", filter, "-ar", "44100", "-c:a", "libmp3lame", "-q:a", "2", mp3);

                var decoded = await Checked("reading the track back", cancellation,
                    "-hide_banner", "-loglevel", "error", "-i", mp3, "-ac", "1", "-ar", "8000", "-f", "f32le", "-");

                if (Peaks.Of(Peaks.Decoded(decoded.Output)) is { } peaks)
                {
                    await File.WriteAllBytesAsync(bars, JsonSerializer.SerializeToUtf8Bytes(peaks), cancellation);

                    made.Add((".mp3", mp3));
                    made.Add((".peaks.json", bars));
                }
            }
        }

        return made;
    }

    private void Render(Opened patch, string step, RenderOptions options, CancellationToken cancellation)
    {
        using var error = new StringWriter(CultureInfo.InvariantCulture);

        if (tools.Render(patch, options, error, cancellation) != Exit.Ok || !options.Out.Exists)
            throw new Failure($"{step} failed.\n{error.ToString().Trim()}");
    }

    private Task<Ran> Ffmpeg(string step, CancellationToken cancellation, params string[] arguments) =>
        Checked(step, cancellation, ["-y", "-loglevel", "error", .. arguments]);

    private async Task<Ran> Checked(string step, CancellationToken cancellation, params string[] arguments)
    {
        var ran = await tools.Ffmpeg(arguments, cancellation);

        if (ran.Ok) return ran;

        var how = ran.TimedOut ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" with exit code {ran.Exit}");

        throw new Failure($"{step} failed{how}.\n{ran.Error.Trim()}");
    }

    internal sealed record Loudness(string I, string Tp, string Lra, string Thresh, string Offset);

    /// <summary>
    /// The first pass of loudnorm, from the JSON it prints last, or null where the
    /// track is too quiet to be anything.
    /// </summary>
    internal static Loudness? Measured(string log)
    {
        var from = log.LastIndexOf('{');
        var to = log.LastIndexOf('}');

        if (from < 0 || to < from) return null;

        using var document = JsonDocument.Parse(log[from..(to + 1)]);
        var root = document.RootElement;

        string Read(string name) => root.GetProperty(name).GetString() ?? string.Empty;

        var loudness = Read("input_i");

        if (!double.TryParse(loudness, NumberStyles.Float, CultureInfo.InvariantCulture, out var lufs) || lufs < -70)
            return null;

        return new Loudness(loudness, Read("input_tp"), Read("input_lra"), Read("input_thresh"), Read("target_offset"));
    }

    /// <summary>A step that could not be done, and why.</summary>
    private sealed class Failure(string why) : Exception(why);
}
