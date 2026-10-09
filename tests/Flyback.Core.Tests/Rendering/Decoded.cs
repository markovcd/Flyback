using System.Diagnostics;
using System.Globalization;

namespace Flyback.Core.Tests.Rendering;

/// <summary>What ffmpeg decodes out of a written file: how many pictures, and how much sound.</summary>
/// <remarks>Decoded to one byte a picture and one a frame of sound, so the count is the length of what ffmpeg printed.</remarks>
internal static class Decoded
{
    /// <summary>How long one decode may take; a test's file decodes in well under a second.</summary>
    private static readonly TimeSpan Cap = TimeSpan.FromMinutes(1);

    /// <summary>How many pictures the file's first picture stream holds.</summary>
    public static int Pictures(string ffmpeg, string path) =>
        Count(ffmpeg, path, "-map", "0:v:0", "-fps_mode", "passthrough", "-vf", "scale=1:1", "-pix_fmt", "gray", "-f", "rawvideo");

    /// <summary>How many frames of sound, at <paramref name="rate"/>, the file's first sound stream holds.</summary>
    public static int Sound(string ffmpeg, string path, int rate) =>
        Count(ffmpeg, path, "-map", "0:a:0", "-ac", "1", "-ar", rate.ToString(CultureInfo.InvariantCulture), "-f", "u8");

    private static int Count(string ffmpeg, string path, params string[] output)
    {
        var start = new ProcessStartInfo(ffmpeg, ["-v", "error", "-nostdin", "-i", path, .. output, "-"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(start)!;
        var said = process.StandardError.ReadToEndAsync();
        var counted = Drain(process.StandardOutput.BaseStream);

        if (!process.WaitForExit(Cap))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"ffmpeg decoded {path} for longer than {Cap.TotalSeconds:0} s: {said.Result}");
        }

        if (process.ExitCode != 0) throw new InvalidOperationException($"ffmpeg could not decode {path}: {said.Result}");

        return counted.Result;
    }

    private static async Task<int> Drain(Stream output)
    {
        var buffer = new byte[64 * 1024];
        var total = 0;
        int read;

        while ((read = await output.ReadAsync(buffer)) > 0) total += read;

        return total;
    }
}
