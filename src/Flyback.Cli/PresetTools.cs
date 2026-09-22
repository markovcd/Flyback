using System.Diagnostics;
using Flyback.Core.Graph;

namespace Flyback.Cli;

/// <summary>What ffmpeg said and how it ended.</summary>
internal sealed record Ran(int Exit, byte[] Output, string Error, bool TimedOut = false)
{
    public bool Ok => Exit == 0 && !TimedOut;
}

/// <summary>What a preset's media is made with: the renderer here, and ffmpeg.</summary>
internal interface IPresetTools
{
    /// <summary>What <see cref="RenderCommand.Run"/> answers for the patch.</summary>
    int Render(Opened patch, RenderOptions options, TextWriter error, CancellationToken cancellation);

    Task<Ran> Ffmpeg(IReadOnlyList<string> arguments, CancellationToken cancellation);
}

internal sealed class PresetTools(string ffmpeg, TimeSpan timeout) : IPresetTools
{
    public int Render(Opened patch, RenderOptions options, TextWriter error, CancellationToken cancellation)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(timeout);

        return RenderCommand.Run(
            patch.Patch,
            options with { Ffmpeg = ffmpeg },
            error,
            samples: patch.Samples,
            pictures: patch.Pictures,
            output: TextWriter.Null,
            cancellation: limit.Token);
    }

    public async Task<Ran> Ffmpeg(IReadOnlyList<string> arguments, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo(ffmpeg)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("ffmpeg did not start.");
        using var output = new MemoryStream();

        var reading = process.StandardOutput.BaseStream.CopyToAsync(output, cancellation);
        var error = process.StandardError.ReadToEndAsync(cancellation);

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);

            return new Ran(-1, [], $"ffmpeg took longer than {timeout.TotalSeconds:0} seconds.", TimedOut: true);
        }

        await reading;

        return new Ran(process.ExitCode, output.ToArray(), await error);
    }
}
