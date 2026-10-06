using System.Net;
using Flyback.Site.Commands;
using Shouldly;
using Xunit;

namespace Flyback.Site.Tests;

public sealed class PushMediaTests : IDisposable
{
    private const string Done = "0199a000000070008000000000000001";
    private const string Failed = "0199a000000070008000000000000002";
    private const string Unfinished = "0199a000000070008000000000000003";

    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-site-media-");

    public void Dispose() => folder.Delete(recursive: true);

    private void Write(string name, string text) => File.WriteAllText(Path.Combine(folder.FullName, name), text);

    [Fact]
    public async Task Each_finished_render_is_sent_with_its_marker_last_and_an_unfinished_one_is_left()
    {
        Write(Done + ".webp", "still");
        Write(Done + ".mp3", "track");
        Write(Done + ".peaks.json", "[0.5, 1]");
        Write(Done + ".done", "");
        Write(Failed + ".failed", "The patch did not open whole.");
        Write(Unfinished + ".webp", "half");

        var site = new FakeSite((_, _) => FakeSite.Status(HttpStatusCode.NoContent));
        using var client = site.Client();
        var output = new StringWriter();

        (await PushMediaCommand.Run(client, folder, output, TextWriter.Null, TestContext.Current.CancellationToken)).ShouldBe(Exit.Ok);

        site.Asked.Select(a => a.Path).ShouldBe([
            $"/api/v1/admin/presets/{Done}/media/webp",
            $"/api/v1/admin/presets/{Done}/media/mp3",
            $"/api/v1/admin/presets/{Done}/media/peaks.json",
            $"/api/v1/admin/presets/{Done}/media/done",
            $"/api/v1/admin/presets/{Failed}/media/failed",
        ]);
        site.Asked.ShouldAllBe(a => a.Method == HttpMethod.Put);
        site.Asked[^1].Body.ShouldBe("The patch did not open whole.");
        output.ToString().ShouldContain($"{Done}: done (webp, mp3, peaks.json)");
    }

    [Fact]
    public async Task A_render_the_site_will_not_take_fails_the_run_and_the_rest_are_still_sent()
    {
        Write(Done + ".done", "");
        Write(Failed + ".failed", "no plugin");

        var site = new FakeSite((request, _) => request.RequestUri!.AbsolutePath.Contains(Done, StringComparison.Ordinal)
            ? FakeSite.Json("""{ "error": "Not found." }""", HttpStatusCode.NotFound)
            : FakeSite.Status(HttpStatusCode.NoContent));
        using var client = site.Client();
        var error = new StringWriter();

        (await PushMediaCommand.Run(client, folder, TextWriter.Null, error, TestContext.Current.CancellationToken)).ShouldBe(Exit.Failed);

        site.Asked.ShouldContain(a => a.Path == $"/api/v1/admin/presets/{Failed}/media/failed");
        error.ToString().ShouldContain($"{Done}: the site answered 404");
    }
}
