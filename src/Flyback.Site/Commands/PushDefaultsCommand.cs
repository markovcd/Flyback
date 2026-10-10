using System.CommandLine;
using System.Net.Http.Json;
using Flyback.Site.Admin;
using Flyback.Site.Checking;
using Flyback.Site.Client;
using Flyback.Site.Reading;

namespace Flyback.Site.Commands;

/// <summary>
/// Sends the presets and plugin packages the site starts with (ADR-0138, ADR-0141), each
/// with its check. The site keeps a default to its file: the same one again changes
/// nothing, a changed one replaces it, one the admin deleted stays deleted.
/// </summary>
internal static class PushDefaultsCommand
{
    public static Command Build()
    {
        var server = new Option<string>("--server")
        {
            Description = "The preset site, e.g. https://presets.example.org/.",
            Required = true,
        };

        var files = new Argument<FileInfo[]>("files")
        {
            Description = "The .fbk, .fbkb and signed .fbkp files the site starts with.",
            Arity = ArgumentArity.OneOrMore,
        };

        var command = new Command(
            "push-defaults",
            $"Give the preset site the presets and plugins it starts with. Needs {SiteAdmin.IdVariable} and {SiteAdmin.SecretVariable}.")
        {
            server, files,
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

            return await Run(site, result.GetRequiredValue(files), BrowserPlugins.Linked(), result.InvocationConfiguration.Output, error, cancellation);
        });

        return command;
    }

    /// <remarks>A default that is refused fails the run, as a bad setting does: it is a broken build, and a shelf quietly short of it would not say so.</remarks>
    internal static async Task<int> Run(HttpClient site, IEnumerable<FileInfo> files, BrowserPlugins browser, TextWriter output, TextWriter error, CancellationToken cancellation)
    {
        var failed = false;

        foreach (var file in files)
        {
            if (!file.Exists)
            {
                error.WriteLine($"flyback-site: {file.FullName}: there is no such file.");
                failed = true;
                continue;
            }

            var bytes = await File.ReadAllBytesAsync(file.FullName, cancellation);
            var check = Checks.Of(Checks.IsPlugin(file.Name), file.Name, bytes, name: null, browser, error);

            if (!Checks.Accepted(check))
            {
                error.WriteLine($"flyback-site: the default {file.Name} cannot be shared: {Checks.Reason(check)}");
                failed = true;
                continue;
            }

            // Quoted, as a browser sends them: the Worker's form parser takes no bare field name.
            using var form = new MultipartFormDataContent
            {
                { new ByteArrayContent(bytes), "\"file\"", "\"" + file.Name.Replace("\"", "", StringComparison.Ordinal) + "\"" },
                { new StringContent(Checks.ToJson(check)), "\"check\"" },
            };

            try
            {
                using var sent = await site.PutAsync($"api/v1/admin/defaults/{Uri.EscapeDataString(file.Name)}", form, cancellation);
                await SiteAnswer.EnsureTaken(sent, cancellation);

                var said = await sent.Content.ReadFromJsonAsync<Seeded>(Checks.Json, cancellation);
                output.WriteLine($"{file.Name}: {said?.State} ({said?.Id})");
            }
            catch (HttpRequestException e)
            {
                error.WriteLine($"flyback-site: {file.Name}: {e.Message}");
                failed = true;
            }
        }

        return failed ? Exit.Failed : Exit.Ok;
    }
}
