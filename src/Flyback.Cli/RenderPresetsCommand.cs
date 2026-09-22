using System.CommandLine;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Flyback.Core;
using Flyback.Core.Render;

namespace Flyback.Cli;

/// <summary>A preset the site is waiting on media for.</summary>
internal sealed record Waiting(string Id, string Name, string FileName);

/// <summary>
/// Renders the preset site's shared presets into its media folder, for the
/// machine that has the power to spare (ADR-0131).
/// </summary>
/// <remarks>
/// The site says which presets are waiting and hands out their files; the only
/// thing written is the share. A pass takes every preset still waiting, then
/// another pass follows after the poll interval, unless <c>--once</c>.
/// </remarks>
internal static class RenderPresetsCommand
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static Command Build()
    {
        var server = new Option<string>("--server")
        {
            Description = "The preset site, e.g. https://presets.example.org/.",
            Required = true,
        };

        var media = new Option<DirectoryInfo>("--media")
        {
            Description = "The site's media folder, as this machine sees the share.",
            Required = true,
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

        var command = new Command("render-presets", "Give the preset site's waiting presets a still, a loop and a track.")
        {
            server, media, ffmpeg, once, poll, timeout,
        };

        command.SetAction(async (result, cancellation) =>
        {
            var folder = result.GetRequiredValue(media);

            if (!folder.Exists)
            {
                Console.Error.WriteLine($"{GlobalConstants.ApplicationName}: {folder.FullName}: the media folder is not there. Mount the site's share.");
                return Exit.Failed;
            }

            if (Ffmpeg.Resolve(result.GetValue(ffmpeg)) is not { } found)
            {
                Console.Error.WriteLine($"{GlobalConstants.ApplicationName}: there is no ffmpeg on PATH. Install it or point --ffmpeg at it.");
                return Exit.Failed;
            }

            if (!Uri.TryCreate(result.GetRequiredValue(server), UriKind.Absolute, out var address))
            {
                Console.Error.WriteLine($"{GlobalConstants.ApplicationName}: --server {result.GetValue(server)}: give the site's whole address, e.g. https://presets.example.org/.");
                return Exit.Failed;
            }

            using var site = new HttpClient { BaseAddress = new Uri(address.AbsoluteUri.TrimEnd('/') + "/") };

            var render = new PresetRender(
                new PresetTools(found, TimeSpan.FromMinutes(result.GetValue(timeout))),
                new MediaWriter(folder.FullName));

            try
            {
                while (true)
                {
                    await Pass(site, render, Console.Out, Console.Error, cancellation);

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

    /// <summary>Renders every preset the site says is waiting, oldest first.</summary>
    internal static async Task Pass(HttpClient site, PresetRender render, TextWriter output, TextWriter error, CancellationToken cancellation)
    {
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
                error.WriteLine($"{Now()} could not fetch {preset.Name}: {e.Message}");
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
