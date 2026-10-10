using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Graph;
using Flyback.Engine.Render;
using Shouldly;
using Xunit;
using Flyback.Cli.Commands;
using Flyback.Cli.Common;
using Flyback.Cli.Models;
using Flyback.Cli.Rendering;

namespace Flyback.Cli.Tests;

/// <summary>
/// Stands in for the renderer and ffmpeg: writes a file wherever one is asked
/// for, and answers the loudness pass as told.
/// </summary>
internal sealed class FakeTools : IPresetTools
{
    public string? FailOn { get; init; }

    public string Loudness { get; init; } = "-18.5";

    public List<string> Ran { get; } = [];

    public List<Opened> Rendered { get; } = [];

    public int Render(Opened patch, RenderOptions options, TextWriter error, CancellationToken cancellation)
    {
        Ran.Add("render " + options.Out.Name);
        Rendered.Add(patch);

        if (FailOn is not null && options.Out.Name.Contains(FailOn, StringComparison.Ordinal))
        {
            error.WriteLine("refusing to render a patch with errors in it.");
            return Exit.Problems;
        }

        File.WriteAllText(options.Out.FullName, options.Out.Name);

        return Exit.Ok;
    }

    public Task<Ran> Ffmpeg(IReadOnlyList<string> arguments, CancellationToken cancellation)
    {
        var line = "ffmpeg " + string.Join(' ', arguments);
        Ran.Add(line);

        if (FailOn is not null && line.Contains(FailOn, StringComparison.Ordinal))
            return Task.FromResult(new Ran(1, [], "Unknown encoder"));

        if (line.Contains("print_format=json", StringComparison.Ordinal))
            return Task.FromResult(new Ran(0, [], $$"""
                [Parsed_loudnorm_0 @ 0x1]
                {
                    "input_i" : "{{Loudness}}",
                    "input_tp" : "-3.20",
                    "input_lra" : "5.10",
                    "input_thresh" : "-28.90",
                    "target_offset" : "0.40"
                }
                """));

        if (arguments[^1] == "-")
        {
            var samples = new float[8000];
            for (var i = 0; i < samples.Length; i++) samples[i] = MathF.Sin(i * 0.1f) * (i < 4000 ? 0.2f : 0.8f);

            var raw = new byte[samples.Length * sizeof(float)];
            Buffer.BlockCopy(samples, 0, raw, 0, raw.Length);

            return Task.FromResult(new Ran(0, raw, ""));
        }

        File.WriteAllText(arguments[^1], line);

        return Task.FromResult(new Ran(0, [], ""));
    }
}
