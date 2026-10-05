using System.CommandLine;
using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace Flyback.Site.Commands;

/// <summary>
/// The Validate workflow's work: checks every submission the site holds unchecked and
/// sends each verdict back, and with --lacks, says again what the web pages lack to
/// open every checked preset.
/// </summary>
internal static class ValidateSubmissionsCommand
{
    /// <summary>A pass takes what the site lists, fifty of each kind; this many passes is plenty for a burst.</summary>
    private const int Passes = 20;

    public static Command Build()
    {
        var server = new Option<string>("--server")
        {
            Description = "The preset site, e.g. https://presets.example.org/.",
            Required = true,
        };

        var lacks = new Option<bool>("--lacks") { Description = "Also say again what the web pages lack for every checked preset." };

        var command = new Command(
            "validate-submissions",
            $"Check what the preset site holds unchecked and send back each verdict. Needs {SiteAdmin.IdVariable} and {SiteAdmin.SecretVariable}.")
        {
            server, lacks,
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

            return await Run(site, BrowserPlugins.Linked(), result.GetValue(lacks), result.InvocationConfiguration.Output, error, cancellation);
        });

        return command;
    }

    internal static async Task<int> Run(HttpClient site, BrowserPlugins browser, bool lacks, TextWriter output, TextWriter error, CancellationToken cancellation)
    {
        var failed = false;

        for (var pass = 0; pass < Passes; pass++)
        {
            Unchecked? waiting;

            try
            {
                waiting = await site.GetFromJsonAsync<Unchecked>("api/v1/admin/unchecked", Checks.Json, cancellation);
            }
            catch (HttpRequestException e)
            {
                error.WriteLine($"flyback-site: the site did not list what waits: {e.Message}");
                return Exit.Failed;
            }

            var listed = (waiting?.Presets ?? []).Select(f => ("presets", f)).Concat((waiting?.Plugins ?? []).Select(f => ("plugins", f))).ToList();

            if (listed.Count == 0) break;

            var progress = false;

            foreach (var (kind, file) in listed)
            {
                try
                {
                    var bytes = await site.GetByteArrayAsync($"api/v1/admin/{kind}/{file.Id}/file", cancellation);
                    var check = Checks.Of(kind == "plugins", file.FileName, bytes, file.Name, browser, error);

                    using var sent = await site.PutAsync(
                        $"api/v1/admin/{kind}/{file.Id}/check",
                        new StringContent(Checks.ToJson(check), Encoding.UTF8, "application/json"),
                        cancellation);

                    // Another run got there first.
                    if (sent.StatusCode == HttpStatusCode.Conflict) continue;

                    await SiteAnswer.EnsureTaken(sent, cancellation);
                    progress = true;

                    output.WriteLine(Checks.Accepted(check)
                        ? $"{file.Id} {file.FileName}: accepted"
                        : $"{file.Id} {file.FileName}: refused: {Checks.Reason(check)}");
                }
                catch (HttpRequestException e)
                {
                    error.WriteLine($"flyback-site: {file.Id} {file.FileName}: {e.Message}");
                    failed = true;
                }
            }

            if (!progress) break;
        }

        if (lacks && !await Lacks(site, browser, output, error, cancellation)) failed = true;

        return failed ? Exit.Failed : Exit.Ok;
    }

    /// <summary>What the web pages of this build lack for each checked preset, which changes with the pages and not the preset.</summary>
    private static async Task<bool> Lacks(HttpClient site, BrowserPlugins browser, TextWriter output, TextWriter error, CancellationToken cancellation)
    {
        CheckedPresets? presets;

        try
        {
            presets = await site.GetFromJsonAsync<CheckedPresets>("api/v1/admin/presets", Checks.Json, cancellation);
        }
        catch (HttpRequestException e)
        {
            error.WriteLine($"flyback-site: the site did not list its presets: {e.Message}");
            return false;
        }

        var whole = true;

        foreach (var preset in presets?.Items ?? [])
        {
            try
            {
                var bytes = await site.GetByteArrayAsync($"api/v1/admin/presets/{preset.Id}/file", cancellation);
                var lack = browser.Lacking(preset.FileName, bytes);

                using var sent = await site.PutAsync(
                    $"api/v1/admin/presets/{preset.Id}/lacks",
                    new StringContent(lack is null ? "null" : Checks.ToJson(lack), Encoding.UTF8, "application/json"),
                    cancellation);

                await SiteAnswer.EnsureTaken(sent, cancellation);
                output.WriteLine($"{preset.Id} {preset.FileName}: {lack?.Said ?? "opens in a browser"}");
            }
            catch (HttpRequestException e)
            {
                error.WriteLine($"flyback-site: {preset.Id} {preset.FileName}: {e.Message}");
                whole = false;
            }
        }

        return whole;
    }
}
