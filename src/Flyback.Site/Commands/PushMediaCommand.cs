using System.CommandLine;
using System.Text.RegularExpressions;
using Flyback.Site.Client;

namespace Flyback.Site.Commands;

/// <summary>
/// Sends what flyback-cli render-presets --media wrote into a folder to the site, each
/// preset's files first and its done or failed last, so the rendering step can run
/// without the site's token and the step holding it reads no stranger's patch.
/// </summary>
internal static partial class PushMediaCommand
{
    /// <summary>What render-presets writes beside the marker, by the name the site takes it under.</summary>
    private static readonly (string Name, string Suffix)[] Files = [("webp", ".webp"), ("webm", ".webm"), ("mp3", ".mp3"), ("peaks.json", ".peaks.json")];

    public static Command Build()
    {
        var server = new Option<string>("--server")
        {
            Description = "The preset site, e.g. https://presets.example.org/.",
            Required = true,
        };

        var folder = new Argument<DirectoryInfo>("folder") { Description = "The folder render-presets --media wrote into." };

        var command = new Command(
            "push-media",
            $"Send the renders render-presets --media made to the preset site. Needs {SiteAdmin.IdVariable} and {SiteAdmin.SecretVariable}.")
        {
            server, folder,
        };

        command.SetAction(async (result, cancellation) =>
        {
            var error = result.InvocationConfiguration.Error;

            using var site = SiteAdmin.Client(result.GetRequiredValue(server), Environment.GetEnvironmentVariable, out var problem);

            if (site is null)
            {
                error.WriteLine($"flyback-site: {problem}");
                return Exit.Failed;
            }

            return await Run(site, result.GetRequiredValue(folder), result.InvocationConfiguration.Output, error, cancellation);
        });

        return command;
    }

    /// <remarks>A preset with no marker beside its files is a render that did not finish, and is left for the next.</remarks>
    internal static async Task<int> Run(HttpClient site, DirectoryInfo folder, TextWriter output, TextWriter error, CancellationToken cancellation)
    {
        if (!folder.Exists)
        {
            error.WriteLine($"flyback-site: {folder.FullName}: there is no such folder.");
            return Exit.Failed;
        }

        var failed = false;

        foreach (var marker in folder.EnumerateFiles().Where(f => Marker().IsMatch(f.Name)).OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            var id = Path.GetFileNameWithoutExtension(marker.Name);
            var sent = new List<string>();

            try
            {
                foreach (var (name, suffix) in Files)
                {
                    var file = new FileInfo(Path.Combine(folder.FullName, id + suffix));
                    if (!file.Exists) continue;

                    await using var bytes = file.OpenRead();
                    await SiteMedia.Put(site, id, name, new StreamContent(bytes), cancellation);
                    sent.Add(name);
                }

                var done = marker.Extension == ".done";
                await SiteMedia.Put(site, id, done ? "done" : "failed", new StringContent(done ? "" : await File.ReadAllTextAsync(marker.FullName, cancellation)), cancellation);

                output.WriteLine(done ? $"{id}: done ({string.Join(", ", sent)})" : $"{id}: failed");
            }
            catch (HttpRequestException e)
            {
                error.WriteLine($"flyback-site: {id}: {e.Message}");
                failed = true;
            }
        }

        return failed ? Exit.Failed : Exit.Ok;
    }

    /// <summary>A finished render's marker: a preset's id, then done or failed.</summary>
    [GeneratedRegex("^[0-9a-f]{32}\\.(done|failed)$")]
    private static partial Regex Marker();
}
