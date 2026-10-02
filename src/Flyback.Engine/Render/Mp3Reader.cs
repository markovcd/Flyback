using System.Diagnostics;
using System.Globalization;
using Flyback.Core.Compile;
using Flyback.Core;

namespace Flyback.Engine.Render;

/// <summary>
/// An MP3, decoded by ffmpeg into a temporary float WAV and read back by
/// <see cref="WavReader"/>, which mixes it down and holds it to the same length.
/// </summary>
/// <remarks>
/// A program rather than a decoder of its own, as <see cref="Ffmpeg"/> is allowed
/// under ADR-0019. ffmpeg is handed a file rather than a pipe, because only from a
/// file does it trim the silence an encoder pads the end with.
/// </remarks>
public static class Mp3Reader
{
    /// <summary>How long a decode is given before it is treated as a failure.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Whether bytes begin as an MP3 does: an ID3 tag, or an MPEG audio frame
    /// header with a layer, a bitrate and a rate that are not reserved.
    /// </summary>
    public static bool Looks(ReadOnlySpan<byte> head) =>
        head.StartsWith("ID3"u8)
        || (head.Length >= 3
            && head[0] == 0xFF
            && (head[1] & 0xE0) == 0xE0
            && (head[1] & 0x18) != 0x08
            && (head[1] & 0x06) != 0
            && (head[2] & 0xF0) != 0xF0
            && (head[2] & 0x0C) != 0x0C);

    public static LoadedSample? Read(string path, string ffmpeg, out SoundFault fault)
    {
        var wav = Path.Combine(Path.GetTempPath(), $"flyback-{Guid.NewGuid():N}.wav");

        try
        {
            if (!Decode(ffmpeg, path, wav))
            {
                fault = SoundFault.Undecoded;
                return null;
            }

            var clip = WavReader.Read(wav, out fault);

            // Written by ffmpeg, so anything wrong with it is ffmpeg's to have said.
            if (fault is SoundFault.Missing or SoundFault.NotSound or SoundFault.Unsupported) fault = SoundFault.Undecoded;

            return clip;
        }
        finally
        {
            Discard(wav);
        }
    }

    /// <summary>Bytes rather than a file, as a bundle holds them: put on the disk for ffmpeg first.</summary>
    public static LoadedSample? Read(Stream input, string ffmpeg, out SoundFault fault)
    {
        var mp3 = Path.Combine(Path.GetTempPath(), $"flyback-{Guid.NewGuid():N}.mp3");

        try
        {
            using (var file = File.Create(mp3)) input.CopyTo(file);

            return Read(mp3, ffmpeg, out fault);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            fault = SoundFault.Undecoded;
            return null;
        }
        finally
        {
            Discard(mp3);
        }
    }

    /// <summary>Runs ffmpeg from one file to the other, and answers whether it finished cleanly.</summary>
    private static bool Decode(string ffmpeg, string from, string to)
    {
        var start = new ProcessStartInfo(ffmpeg)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // file: so a path is never read as a protocol, and -t so a long file
        // stops at the length WavReader would cut it to anyway.
        var seconds = WavReader.MostSamples / GlobalConstants.SampleRate;

        foreach (var argument in (string[])
                 [
                     "-v", "error", "-nostdin", "-y",
                     "-f", "mp3", "-i", "file:" + Path.GetFullPath(from),
                     "-vn", "-t", seconds.ToString(CultureInfo.InvariantCulture),
                     "-c:a", "pcm_f32le", "-f", "wav", "file:" + to,
                 ])
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start);

            if (process is null) return false;

            process.StandardInput.Close();

            // Drained, so a chatty ffmpeg cannot fill a pipe and stall.
            var said = process.StandardError.ReadToEndAsync();
            var printed = process.StandardOutput.ReadToEndAsync();

            if (!process.WaitForExit(Patience))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            Task.WaitAll(said, printed);

            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            // A path that is not a program, or one that will not start.
            return false;
        }
    }

    private static void Discard(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temporary file left behind is the temp folder's to clear.
        }
    }
}
