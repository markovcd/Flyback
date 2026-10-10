using System.CommandLine;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Flyback.Cli.Models;
using Flyback.Cli.Common;
using Flyback.Cli.Rendering;
using Flyback.Core;
using Flyback.Engine.Render;
using PluginRegistry = Flyback.Cli.Plugins;
using Flyback.Site.Client;

namespace Flyback.Cli.Commands;

/// <summary>
/// Renders the preset site's shared presets, for the machine that has the power to
/// spare (ADR-0131), and uploads what it makes through the site's admin API, or
/// writes it into the media folder where one is given.
/// </summary>
/// <remarks>
/// The site says which presets are waiting and hands out their files. A pass takes
/// every preset still waiting, then another pass follows after the poll interval,
/// unless <c>--once</c>.
/// </remarks>
internal static class RenderPresetsCommand
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>The shortest and longest wait, in minutes: nought would fail every render, or ask the site without pause.</summary>
    private const double LeastMinutes = 1d, MostMinutes = 24d * 60d;

    public static Command Build(PluginRegistry plugins)
    {
        var server = new Option<string>("--server")
        {
            Description = "The preset site, e.g. https://presets.example.org/.",
            Required = true,
        };

        var media = new Option<DirectoryInfo?>("--media")
        {
            Description = $"The site's media folder, as this machine sees the share. Left out, the render is uploaded with the Access service token in {SiteAdmin.IdVariable} and {SiteAdmin.SecretVariable}.",
        };

        var ffmpeg = new Option<string>("--ffmpeg")
        {
            Description = "The ffmpeg to encode with. Left out, the first on PATH is used.",
        };

        var once = new Option<bool>("--once") { Description = "Make one pass and stop." };

        var poll = new Option<double>("--poll-minutes")
        {
            Description = "How long to wait between passes.",
            DefaultValueFactory = _ => 5d,
        };

        var timeout = new Option<double>("--timeout-minutes")
        {
            Description = "The longest one render or one ffmpeg run may take.",
            DefaultValueFactory = _ => 10d,
        };

        var limit = new Option<int?>("--limit")
        {
            Description = "The most presets one pass renders; the rest wait for the next.",
        };

        var stillOnly = new Option<bool>("--still-only") { Description = "Make only the still: no loop and no track." };

        var command = new Command("render-presets", "Give the preset site's waiting presets a still, a loop and a track.")
        {
            server, media, ffmpeg, once, poll, timeout, limit, stillOnly,
        };

        command.SetAction(async (result, cancellation) =>
        {
            var error = result.InvocationConfiguration.Error;
            var folder = result.GetValue(media);

            if (folder is { Exists: false })
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: {folder.FullName}: the media folder is not there. Mount the site's share.");
                return Exit.Failed;
            }

            if (result.GetValue(limit) is < 1)
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: --limit {result.GetValue(limit)}: a pass renders at least one preset.");
                return Exit.Failed;
            }

            foreach (var wait in (Option<double>[])[poll, timeout])
            {
                if (result.GetValue(wait) is not (>= LeastMinutes and <= MostMinutes))
                {
                    var typed = result.GetResult(wait)?.Tokens is [{ } token, ..] ? token.Value : null;

                    error.WriteLine($"{GlobalConstants.ApplicationName}: {wait.Name} {typed}: a wait is {LeastMinutes:0} to {MostMinutes:0} minutes.");
                    return Exit.Failed;
                }
            }

            if (Ffmpeg.Resolve(result.GetValue(ffmpeg)) is not { } found)
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: there is no ffmpeg on PATH. Install it or point --ffmpeg at it.");
                return Exit.Failed;
            }

            string? problem;

            using var site = folder is null
                ? SiteAdmin.Client(result.GetRequiredValue(server), Environment.GetEnvironmentVariable, out problem)
                : SiteAddress.Parse(result.GetRequiredValue(server), out problem) is { } address ? new HttpClient { BaseAddress = address } : null;

            if (site is null)
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: {problem}");
                return Exit.Failed;
            }

            plugins.Ready();

            var render = new PresetRender(
                new PresetTools(found, TimeSpan.FromMinutes(result.GetValue(timeout))),
                folder is null ? new MediaUpload(site) : new MediaWriter(folder.FullName),
                result.GetValue(stillOnly));

            try
            {
                while (true)
                {
                    await Pass(site, render, result.InvocationConfiguration.Output, error, cancellation, result.GetValue(limit));

                    if (result.GetValue(once)) return Exit.Ok;

                    await Task.Delay(TimeSpan.FromMinutes(result.GetValue(poll)), cancellation);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return Exit.Ok;
            }
        });

        return command;
    }

    /// <summary>Renders the presets the site says are waiting, oldest first, at most <paramref name="limit"/> of them where one is given.</summary>
    internal static async Task Pass(HttpClient site, PresetRender render, TextWriter output, TextWriter error, CancellationToken cancellation, int? limit = null)
    {
        var rendered = 0;

        List<Waiting> waiting;

        try
        {
            var page = await site.GetFromJsonAsync<WaitingPage>("api/v1/presets?pending=true", Web, cancellation);
            waiting = page?.Items ?? [];
        }
        catch (HttpRequestException e)
        {
            error.WriteLine($"{Now()} the site did not answer: {e.Message}");
            return;
        }

        foreach (var preset in waiting)
        {
            if (!render.Pending(preset.Id)) continue;
            if (rendered++ == limit) return;

            var folder = Directory.CreateTempSubdirectory("flyback-preset-");

            try
            {
                var file = new FileInfo(Path.Combine(folder.FullName, "preset" + Path.GetExtension(preset.FileName)));

                // count=false: fetching to render is not a download.
                var bytes = await site.GetByteArrayAsync($"api/v1/presets/{preset.Id}/file?count=false", cancellation);
                await File.WriteAllBytesAsync(file.FullName, bytes, cancellation);

                output.WriteLine($"{Now()} rendering {preset.Name} ({preset.Id})");

                var why = await render.Render(preset.Id, file, cancellation);

                output.WriteLine(why is null ? $"{Now()} done" : $"{Now()} failed: {why}");
            }
            catch (HttpRequestException e)
            {
                error.WriteLine($"{Now()} could not fetch {preset.Name} or send its render: {e.Message}");
            }
            finally
            {
                folder.Delete(recursive: true);
            }
        }
    }

    private static string Now() => DateTime.Now.ToString("t", CultureInfo.InvariantCulture);

    private sealed record WaitingPage(List<Waiting> Items);
}
