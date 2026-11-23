using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using Flyback.Cli.Common;
using Flyback.Cli.Models;
using Flyback.Cli.Rendering;
using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Flyback.Engine.Render;
using Flyback.Host;
using Flyback.Plugins.Settings;
using Flyback.Plugins.Video;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Cli.Commands;

/// <summary>
/// Plays a patch's picture into a plugin's picture output for a while, with no window,
/// and says how many frames went: how a script or an agent checks an output works.
/// </summary>
/// <remarks>Hidden from the help until a shipped plugin offers an output.</remarks>
internal static class SendCommand
{
    public static Command Build(PluginRegistry plugins, OutputSettings defaults, Option<bool> json)
    {
        var given = new PatchInput("send");

        var to = new Option<string>("--to") { Description = "The picture output to send to, by id. --outputs lists them." };

        var outputs = new Option<bool>("--outputs") { Description = "List the picture outputs installed, and stop." };

        var size = new Option<(int Width, int Height)>("--size")
        {
            Description = "Frame size, as WIDTHxHEIGHT, or " + string.Join(", ", Resolutions.Names.Keys) + ".",
            DefaultValueFactory = _ => (defaults.Width, defaults.Height),
            CustomParser = SizeArgument.Parse,
        };

        var seconds = new Option<double>("--seconds")
        {
            Description = $"How long to send for, above 0 and up to {RenderOptions.MostSeconds:0}.",
            DefaultValueFactory = _ => 5d,
        };

        var fps = new Option<double>("--fps")
        {
            Description = "Frames a second.",
            DefaultValueFactory = _ => defaults.FrameRate,
        };

        var fast = new Option<bool>("--fast")
        {
            Description = "Send each frame as soon as it is drawn, rather than when its moment comes round.",
        };

        var command = new Command("send", "Send a patch's picture to a picture output a plugin offers, such as another program.")
        {
            given.Patch, given.Preset, given.Presets, to, outputs, size, seconds, fps, fast, json,
        };

        command.Hidden = true;

        command.SetAction((result, cancellation) =>
        {
            var error = result.InvocationConfiguration.Error;
            var output = result.InvocationConfiguration.Output;

            plugins.Ready();

            if (result.GetValue(outputs))
            {
                List(plugins.Catalog.VideoOutputs, plugins.Catalog.UsableVideoOutputs, output, result.GetValue(json));
                return Task.FromResult(Exit.Ok);
            }

            if (given.Listed(result, plugins)) return Task.FromResult(Exit.Ok);

            if (!given.Chosen(result, out var file, out var named)) return Task.FromResult(Exit.Failed);

            if (result.GetValue(to) is not { } id)
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: --to says which picture output to send to; --outputs lists them.");
                return Task.FromResult(Exit.Failed);
            }

            if (plugins.Catalog.UsableVideoOutputs.FirstOrDefault(o => o.Id == id) is not { } target)
            {
                var usable = plugins.Catalog.UsableVideoOutputs.Select(o => o.Id).ToList();

                error.WriteLine($"{GlobalConstants.ApplicationName}: --to {id}: no picture output of that id can send here. "
                    + (usable.Count == 0 ? "None is installed." : "Choose one of " + string.Join(", ", usable) + "."));

                return Task.FromResult(Exit.Failed);
            }

            Opened opened;

            if (file is not null)
            {
                if (Patches.Open(file, error) is not { } fromFile) return Task.FromResult(Exit.Failed);

                opened = fromFile;
            }
            else
            {
                if (ShippedPresets.Open(plugins.Catalog, named!, error) is not { } shipped) return Task.FromResult(Exit.Failed);

                opened = shipped.Opened;
            }

            var (width, height) = result.GetValue(size);

            return Task.FromResult(Run(
                target,
                opened,
                new VideoFormat(width, height, result.GetValue(fps)),
                result.GetValue(seconds),
                paced: !result.GetValue(fast),
                result.GetValue(json),
                output,
                error,
                cancellation: cancellation));
        });

        return command;
    }

    public static int Run(
        IVideoOutput target,
        Opened opened,
        VideoFormat format,
        double seconds,
        bool paced,
        bool json,
        TextWriter output,
        TextWriter error,
        PictureBackend backend = PictureBackend.Any,
        CancellationToken cancellation = default)
    {
        if (!(seconds > 0d && seconds <= RenderOptions.MostSeconds))
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: --seconds runs above 0 and up to {RenderOptions.MostSeconds:0}.");
            return Exit.Failed;
        }

        if (!(format.FramesPerSecond >= RenderOptions.LeastFps && format.FramesPerSecond <= RenderOptions.MostFps))
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: --fps runs from {RenderOptions.LeastFps:0} up to {RenderOptions.MostFps:0}.");
            return Exit.Failed;
        }

        var (patch, samples, pictures) = opened;

        var video = patch.CompileForVideo(samples: samples, pictures: pictures);
        var audio = RenderCommand.Listens(video.Program) ? patch.CompileForAudio(samples: samples) : null;

        var issues = video.Issues.Concat(audio?.Issues ?? []).DistinctBy(i => (i.NodeId, i.Message)).ToArray();

        foreach (var issue in issues)
            error.WriteLine($"{GlobalConstants.ApplicationName}: {(issue.Severity == IssueSeverity.Error ? "error" : "warning")}: {issue.Message}");

        if (issues.Any(i => i.Severity == IssueSeverity.Error))
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: refusing to send a patch with errors in it.");
            return Exit.Problems;
        }

        using var gpu = RenderCommand.Gpu(video.Program, backend, error, out _);

        var frames = RenderCommand.Frames(gpu, Smoothing.Fast);
        var settings = new MovieSettings(format.Width, format.Height, seconds, format.FramesPerSecond);

        int sent;

        try
        {
            using var feed = new FeedWriter(target.Create(format, SettingValues.None), format.FramesPerSecond, TimeSpan.Zero, paced);

            sent = MovieRenderer.Render(feed, video.Program, audio?.Program, settings, frames, cancellation);
        }
        catch (Exception ex)
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: {target.Id}: {ex.Message}");
            return Exit.Failed;
        }

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(
                new { output = target.Id, frames = sent, width = format.Width, height = format.Height, fps = format.FramesPerSecond },
                Writing.Json));
        }
        else
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"sent {sent} frames of {format.Width}x{format.Height} at {format.FramesPerSecond:0.##} fps to {target.Id}"));
        }

        return sent >= settings.FrameCount ? Exit.Ok : Exit.Failed;
    }

    private static void List(IReadOnlyList<IVideoOutput> installed, IReadOnlyList<IVideoOutput> usable, TextWriter output, bool json)
    {
        var rows = installed.Select(o => (o.Id, o.Name, Usable: usable.Contains(o))).OrderBy(o => o.Id, StringComparer.Ordinal).ToList();

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(rows.Select(r => new { id = r.Id, name = r.Name, usable = r.Usable }), Writing.Json));
            return;
        }

        if (rows.Count == 0) output.WriteLine("No picture output is installed.");

        foreach (var (id, name, canSend) in rows)
            output.WriteLine($"{id}  {name}{(canSend ? "" : "  (cannot send here)")}");
    }
}
