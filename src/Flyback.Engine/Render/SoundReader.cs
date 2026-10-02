using System.Diagnostics.CodeAnalysis;
using Flyback.Core.Compile;

namespace Flyback.Engine.Render;

/// <summary>
/// A sound file, whichever kind it is: a WAV read here, or an MP3 decoded by ffmpeg.
/// Told apart by their first bytes, not their names.
/// </summary>
public static class SoundReader
{
    /// <param name="path">The file.</param>
    /// <param name="ffmpeg">The ffmpeg to decode an MP3 with, or null where there is none.</param>
    /// <param name="fault">Why nothing came back, or <see cref="SoundFault.None"/>.</param>
    public static LoadedSample? Read(string path, string? ffmpeg, out SoundFault fault)
    {
        if (!File.Exists(path))
        {
            fault = SoundFault.Missing;
            return null;
        }

        try
        {
            byte[] head;

            using (var file = File.OpenRead(path))
            {
                head = Head(file);

                if (WavReader.Looks(head)) return WavReader.Read(file, out fault);
            }

            return Mp3(head, ffmpeg, out fault) ? Mp3Reader.Read(path, ffmpeg, out fault) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Locked, or on a share that went away between the check and the open.
            fault = SoundFault.Missing;
            return null;
        }
    }

    /// <inheritdoc cref="Read(string, string?, out SoundFault)"/>
    /// <param name="input">The file's bytes, from the start.</param>
    public static LoadedSample? Read(Stream input, string? ffmpeg, out SoundFault fault)
    {
        if (!input.CanSeek)
        {
            var copy = new MemoryStream();
            input.CopyTo(copy);
            copy.Position = 0;
            input = copy;
        }

        var head = Head(input);

        if (WavReader.Looks(head)) return WavReader.Read(input, out fault);

        return Mp3(head, ffmpeg, out fault) ? Mp3Reader.Read(input, ffmpeg, out fault) : null;
    }

    /// <summary>The first bytes, with the stream put back where it was.</summary>
    private static byte[] Head(Stream input)
    {
        var start = input.Position;
        var head = new byte[12];
        var read = input.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);

        input.Position = start;

        return head[..read];
    }

    /// <summary>Whether this is an MP3 there is an ffmpeg to decode, and why not where it is not.</summary>
    private static bool Mp3(byte[] head, [NotNullWhen(true)] string? ffmpeg, out SoundFault fault)
    {
        fault = !Mp3Reader.Looks(head) ? SoundFault.NotSound
            : ffmpeg is null ? SoundFault.NoFfmpeg
            : SoundFault.None;

        return fault == SoundFault.None;
    }
}
