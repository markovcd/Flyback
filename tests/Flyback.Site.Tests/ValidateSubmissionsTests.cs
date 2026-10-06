using System.Net;
using System.Text.Json;
using Flyback.Site.Commands;
using Shouldly;
using Xunit;
using Flyback.Site.Reading;

namespace Flyback.Site.Tests;

public sealed class ValidateSubmissionsTests
{
    private static readonly BrowserPlugins Browser = BrowserPlugins.Linked();

    /// <summary>A site with two presets and a package waiting, which takes each verdict once.</summary>
    private static FakeSite Waiting(HttpStatusCode secondAnswer = HttpStatusCode.OK)
    {
        var listed = false;

        return new FakeSite((request, _) => (request.Method.Method, request.RequestUri!.AbsolutePath) switch
        {
            ("GET", "/api/v1/admin/unchecked") when !listed && (listed = true) => FakeSite.Json("""
                {
                  "presets": [ { "id": "p1", "name": "Night bus", "fileName": "Drone.fbk" }, { "id": "p2", "name": null, "fileName": "notes.fbk" } ],
                  "plugins": [ { "id": "k1", "fileName": "upload.fbkp" } ]
                }
                """),
            ("GET", "/api/v1/admin/unchecked") => FakeSite.Json("""{ "presets": [], "plugins": [] }"""),
            ("GET", "/api/v1/admin/presets/p1/file") => FakeSite.Bytes(Files.Patch()),
            ("GET", "/api/v1/admin/presets/p2/file") => FakeSite.Bytes("{\"hello\": 1}"u8.ToArray()),
            ("GET", "/api/v1/admin/plugins/k1/file") => FakeSite.Bytes(Files.Package("win")),
            ("PUT", "/api/v1/admin/presets/p2/check") => FakeSite.Status(secondAnswer),
            ("PUT", _) => FakeSite.Status(HttpStatusCode.OK),
            _ => FakeSite.Status(HttpStatusCode.NotFound),
        });
    }

    [Fact]
    public async Task Every_waiting_submission_is_checked_and_its_verdict_sent_back()
    {
        var site = Waiting();
        using var client = site.Client();
        var output = new StringWriter();

        (await ValidateSubmissionsCommand.Run(client, Browser, lacks: false, output, TextWriter.Null, TestContext.Current.CancellationToken))
            .ShouldBe(Exit.Ok);

        var sent = site.Asked.Where(a => a.Method == HttpMethod.Put).ToDictionary(a => a.Path, a => JsonDocument.Parse(a.Body!).RootElement);

        sent["/api/v1/admin/presets/p1/check"].GetProperty("name").GetString().ShouldBe("Night bus");
        sent["/api/v1/admin/presets/p2/check"].GetProperty("accepted").GetBoolean().ShouldBeFalse();
        sent["/api/v1/admin/plugins/k1/check"].GetProperty("assembly").GetString().ShouldBe("Flyback.Plugins.Picture");

        output.ToString().ShouldContain("p2 notes.fbk: refused: That is not a Flyback patch.");
    }

    [Fact]
    public async Task A_verdict_another_run_sent_first_is_no_failure()
    {
        using var client = Waiting(HttpStatusCode.Conflict).Client();

        (await ValidateSubmissionsCommand.Run(client, Browser, lacks: false, TextWriter.Null, TextWriter.Null, TestContext.Current.CancellationToken))
            .ShouldBe(Exit.Ok);
    }

    [Fact]
    public async Task A_verdict_the_site_would_not_take_fails_the_run_and_the_rest_are_still_sent()
    {
        var site = Waiting(HttpStatusCode.BadRequest);
        using var client = site.Client();
        var error = new StringWriter();

        (await ValidateSubmissionsCommand.Run(client, Browser, lacks: false, TextWriter.Null, error, TestContext.Current.CancellationToken))
            .ShouldBe(Exit.Failed);

        site.Asked.ShouldContain(a => a.Path == "/api/v1/admin/plugins/k1/check");
        error.ToString().ShouldContain("p2 notes.fbk");
    }

    [Fact]
    public async Task What_the_pages_lack_is_said_again_for_every_checked_preset()
    {
        var site = new FakeSite((request, _) => (request.Method.Method, request.RequestUri!.AbsolutePath) switch
        {
            ("GET", "/api/v1/admin/unchecked") => FakeSite.Json("""{ "presets": [], "plugins": [] }"""),
            ("GET", "/api/v1/admin/presets") => FakeSite.Json("""{ "items": [ { "id": "a", "fileName": "Lantern.fbk" }, { "id": "b", "fileName": "Drone.fbk" } ] }"""),
            ("GET", "/api/v1/admin/presets/a/file") => FakeSite.Bytes(Files.Using("example.lantern", "Lantern", "example.lantern.glow")),
            ("GET", "/api/v1/admin/presets/b/file") => FakeSite.Bytes(Files.Patch()),
            ("PUT", _) => FakeSite.Status(HttpStatusCode.NoContent),
            _ => FakeSite.Status(HttpStatusCode.NotFound),
        });
        using var client = site.Client();

        (await ValidateSubmissionsCommand.Run(client, Browser, lacks: true, TextWriter.Null, TextWriter.Null, TestContext.Current.CancellationToken))
            .ShouldBe(Exit.Ok);

        var sent = site.Asked.Where(a => a.Method == HttpMethod.Put).ToDictionary(a => a.Path, a => a.Body);

        sent["/api/v1/admin/presets/a/lacks"]!.ShouldContain("Needs the Lantern plugin");
        sent["/api/v1/admin/presets/b/lacks"].ShouldBe("null");
    }

    [Fact]
    public async Task A_site_that_does_not_answer_fails_the_run()
    {
        using var client = new FakeSite((_, _) => FakeSite.Status(HttpStatusCode.Unauthorized)).Client();
        var error = new StringWriter();

        (await ValidateSubmissionsCommand.Run(client, Browser, lacks: false, TextWriter.Null, error, TestContext.Current.CancellationToken))
            .ShouldBe(Exit.Failed);
        error.ToString().ShouldContain("did not list");
    }
}
