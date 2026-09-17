using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Flyback.Core.Render;

/// <summary>
/// A clip encoded by ffmpeg: frames go down its standard input as raw pixels and
/// it writes whatever <see cref="ClipFormat"/> asked for.
/// </summary>
/// <remarks>
/// Raw pixels rather than the JPEGs <see cref="AviClipWriter"/> makes, because
/// handing a real encoder a compressed frame to decompress would pay for this
/// program's compression twice and lose a generation to it.
/// <para>
/// A process reads one standard input, and a clip with sound in it has two
/// streams. So the sound goes to a WAV beside the file while the picture is
/// encoded, and the two are muxed in a second pass that copies the video through
/// untouched. That costs one rewrite of the file at the end and nothing in the
/// loop, which is the half that has to keep up with a performance. A clip with no
/// sound skips all of it and is written straight through by one process.
/// </para>
/// </remarks>
public sealed class FfmpegClipWriter : IClipWriter
{
    /// <summary>
    /// How long the encoder is given to drain and close after its input ends.
    /// Generous because it is draining a queue this filled as fast as the disk
    /// would take it, and a frame still being encoded is not a hang.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How much of what ffmpeg said is kept to say back. Its complaints are one
    /// line; a wall of them is a build that will not run at all, and the first
    /// few thousand characters of that say as much as all of it.
    /// </summary>
    private const int TroubleKept = 4000;

    private readonly ClipTarget target;
    private readonly string ffmpeg;
    /// <summary>The one process frames or samples are fed to, whichever this clip is.</summary>
    private readonly Process encoder;
    private readonly StringBuilder trouble = new();

    /// <summary>Where the picture goes first, or null where it goes straight to the file.</summary>
    private readonly string? partial;

    /// <summary>The sound, waiting to be muxed, or null for a clip with none.</summary>
    private readonly Stream? soundFile;
    private readonly WavStreamWriter? sound;
    private readonly string? soundPath;

    private readonly Stream input;
    private long frames;
    private bool closed;

    /// <param name="target">What the clip is. Its format has to be one that needs ffmpeg.</param>
    /// <param name="ffmpeg">The executable, as <see cref="Render.Ffmpeg.Resolve"/> found it.</param>
    public FfmpegClipWriter(ClipTarget target, string ffmpeg)
    {
        if (!target.Format.NeedsFfmpeg)
            throw new ArgumentException($"{target.Format.Label} is written here, not by ffmpeg.", nameof(target));

        if (target.Format.HasPicture && (target.Width <= 0 || target.Height <= 0))
            throw new ArgumentOutOfRangeException(nameof(target), "A frame needs both dimensions.");

        if (!target.Format.HasPicture && !target.HasSound)
            throw new ArgumentException("A sound-only clip needs a rate and channels.", nameof(target));

        this.target = target;
        this.ffmpeg = ffmpeg;

        // A picture and a sound need the second pass; either on its own is
        // written where it is asked for by the one process below.
        if (target.Format.HasPicture && target.HasSound)
        {
            partial = $"{target.Path}.part{target.Format.Extension}";
            soundPath = $"{target.Path}.part.wav";

            soundFile = File.Create(soundPath);
            sound = new WavStreamWriter(soundFile, target.SampleRate, target.Channels);
        }

        // An ffmpeg that will not start must not leave the WAV it never got to
        // use open and on the disk.
        try
        {
            encoder = Start(target.Format.HasPicture ? PictureArguments() : SoundArguments());
        }
        catch
        {
            sound?.Dispose();
            soundFile?.Dispose();
            Discard(soundPath);

            throw;
        }

        input = encoder.StandardInput.BaseStream;
    }

    public long FrameCount => frames;

    public void WriteFrame(ReadOnlySpan<byte> bgra, int stride, int repeat = 1)
    {
        if (!target.Format.HasPicture) throw new InvalidOperationException("This clip has no picture in it.");
        if (repeat <= 0) return;

        var tight = target.Width * 4;

        for (var i = 0; i < repeat; i++)
        {
            // A repeated frame is the same bytes again rather than a cheaper
            // signal to hold the last one: rawvideo has no such signal, and what
            // this costs is a pipe write, which the encoder collapses into
            // nothing anyway once it sees two identical frames.
            if (stride == tight)
            {
                Feed(bgra[..(tight * target.Height)]);
            }
            else
            {
                for (var y = 0; y < target.Height; y++) Feed(bgra.Slice(y * stride, tight));
            }

            frames++;
        }
    }

    public void WriteAudio(ReadOnlySpan<float> interleaved)
    {
        if (!target.HasSound) throw new InvalidOperationException("This clip has no sound in it.");
        if (interleaved.Length == 0) return;

        // Into the WAV beside the file while there is a picture being encoded,
        // and down the pipe when the sound is the whole clip.
        if (sound is not null) sound.WriteAudio(interleaved);
        else Feed(MemoryMarshal.AsBytes(interleaved));
    }

    /// <summary>
    /// Ends the encode and, where there is sound, muxes it in. Throws if ffmpeg
    /// failed, having first moved whatever it did manage to write into place —
    /// a take that lost its sound is still a take, and is worth more than a
    /// tidy folder.
    /// </summary>
    public void Dispose()
    {
        if (closed) return;
        closed = true;

        try
        {
            Finish();
        }
        finally
        {
            sound?.Dispose();
            soundFile?.Dispose();
            encoder.Dispose();

            Discard(soundPath);
        }
    }

    private void Finish()
    {
        var failed = Close(encoder);

        // The picture is already the file when there was no sound to add, so
        // whatever ffmpeg said about it is all there is to say.
        if (partial is null)
        {
            if (failed is not null) throw Failed(failed);
            return;
        }

        sound?.Dispose();
        soundFile?.Dispose();

        // Even a failed pass leaves a picture worth keeping, and putting it in
        // place is what makes "the sound is missing" the whole of the damage.
        if (failed is not null)
        {
            Salvage();
            throw Failed(failed);
        }

        var mux = Start(MuxArguments());
        var refused = Close(mux);

        mux.Dispose();

        if (refused is not null)
        {
            Salvage();
            throw Failed(refused);
        }

        Discard(partial);
    }

    /// <summary>The picture, on its own, in place of the file that should have held both.</summary>
    private void Salvage()
    {
        if (partial is null || !File.Exists(partial)) return;

        try
        {
            File.Move(partial, target.Path, overwrite: true);
        }
        catch (IOException)
        {
            // Then it stays where it is, under a name that still says what it
            // holds. Nothing here is worth a second failure over.
        }
    }

    private static void Discard(string? path)
    {
        if (path is null) return;

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A temporary file left behind is untidy and nothing more.
        }
    }

    /// <summary>
    /// One frame or one row into the pipe. An encoder that has died takes the
    /// write with it, and what it said on the way out is the only useful thing
    /// left to report.
    /// </summary>
    private void Feed(ReadOnlySpan<byte> bytes)
    {
        try
        {
            input.Write(bytes);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            if (encoder.WaitForExit(Patience)) encoder.WaitForExit();

            throw Failed($"ffmpeg stopped after {frames} frames");
        }
    }

    /// <summary>
    /// Ends one process's input and waits for it, giving back what went wrong or
    /// null where nothing did.
    /// </summary>
    private string? Close(Process process)
    {
        try
        {
            process.StandardInput.BaseStream.Flush();
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // Already gone. Its exit code below says so.
        }

        if (!process.WaitForExit(Patience))
        {
            process.Kill(entireProcessTree: true);

            return "ffmpeg did not finish";
        }

        // The timed wait returns at the exit and not at the end of what was
        // said, and what was said is the reason; only the untimed one waits for
        // both pipes to run dry.
        process.WaitForExit();

        return process.ExitCode == 0 ? null : $"ffmpeg exited with {process.ExitCode}";
    }

    private InvalidOperationException Failed(string what)
    {
        var said = trouble.ToString().Trim();

        return new InvalidOperationException(said.Length > 0 ? $"{what}: {said}" : what);
    }

    private Process Start(IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(ffmpeg)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        var started = Process.Start(info)
            ?? throw new InvalidOperationException($"Could not run {ffmpeg}.");

        // Read as it comes rather than at the end, because a pipe nobody empties
        // is what stops a process that is complaining loudly enough.
        started.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;

            lock (trouble)
                if (trouble.Length < TroubleKept) trouble.AppendLine(e.Data);
        };

        // Nothing is expected on the output, and a pipe nobody empties is what
        // would stop the process if anything ever were.
        started.OutputDataReceived += (_, _) => { };

        started.BeginErrorReadLine();
        started.BeginOutputReadLine();

        return started;
    }

    /// <summary>
    /// Raw frames in, the format's own video arguments out. <c>-an</c> because
    /// the sound is not here yet, whether it is coming in a second pass or not
    /// coming at all.
    /// </summary>
    private IEnumerable<string> PictureArguments() =>
    [
        .. Common,
        "-f", "rawvideo",
        "-pixel_format", "bgra",
        "-video_size", $"{target.Width}x{target.Height}",
        "-framerate", target.FramesPerSecond.ToString("0.######", CultureInfo.InvariantCulture),
        "-i", "pipe:0",
        "-an",
        .. Split(target.Format.PictureArguments(target.Quality)),
        .. Split(partial is null ? target.Format.Container : string.Empty),
        partial ?? target.Path,
    ];

    /// <summary>
    /// The samples as they already are in memory — float, native order, which is
    /// little-endian everywhere this runs — so nothing is converted on the way to
    /// a codec that will convert them anyway.
    /// </summary>
    private IEnumerable<string> SoundArguments() =>
    [
        .. Common,
        "-f", "f32le",
        "-ar", target.SampleRate.ToString(CultureInfo.InvariantCulture),
        "-ac", target.Channels.ToString(CultureInfo.InvariantCulture),
        "-i", "pipe:0",
        .. Split(target.Format.Sound),
        .. Split(target.Format.Container),
        target.Path,
    ];

    /// <summary>
    /// The second pass: the encoded picture and the WAV into one file, the video
    /// copied rather than encoded again. <c>-shortest</c> because the two are
    /// within a frame of each other by construction and the last fraction of
    /// whichever ran over says nothing.
    /// </summary>
    private IEnumerable<string> MuxArguments() =>
    [
        .. Common,
        "-i", partial!,
        "-i", soundPath!,
        "-map", "0:v:0",
        "-map", "1:a:0",
        "-c:v", "copy",
        .. Split(target.Format.Sound),
        .. Split(target.Format.Container),
        "-shortest",
        target.Path,
    ];

    /// <summary>
    /// What every pass wants: no banner, nothing on the output but trouble, and
    /// an existing file overwritten — the caller has already asked somebody
    /// about that, and a prompt on a pipe nobody is watching would hang.
    /// </summary>
    private static string[] Common => ["-hide_banner", "-loglevel", "error", "-y"];

    /// <summary>
    /// A format's arguments as ffmpeg wants them: one per entry, since they are
    /// passed as a list rather than a command line and nothing here has to be
    /// quoted.
    /// </summary>
    private static string[] Split(string arguments) =>
        arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
