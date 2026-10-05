using System.Net;
using Flyback.Site.Commands;
using Shouldly;
using Xunit;

namespace Flyback.Site.Tests;

public sealed class PushDefaultsTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-site-defaults-");

    public void Dispose() => folder.Delete(recursive: true);

    private FileInfo Write(string name, byte[] bytes)
    {
        var file = new FileInfo(Path.Combine(folder.FullName, name));
        File.WriteAllBytes(file.FullName, bytes);
        return file;
    }

    [Fact]
    public async Task Each_default_is_sent_with_its_check()
    {
        var site = new FakeSite((_, _) => FakeSite.Json("""{ "id": "d1", "state": "added" }"""));
        using var client = site.Client();
        var output = new StringWriter();

        var code = await PushDefaultsCommand.Run(
            client, [Write("Machine Room.fbk", Files.Patch("Techno.", "Flyback", "techno"))], BrowserPlugins.Linked(), output, TextWriter.Null,
            TestContext.Current.CancellationToken);

        code.ShouldBe(Exit.Ok);

        var (method, path, body) = site.Asked.Single();
        method.ShouldBe(HttpMethod.Put);
        path.ShouldBe("/api/v1/admin/defaults/Machine%20Room.fbk");
        body!.ShouldContain("name=\"check\"", customMessage: "the Worker's form parser takes only a quoted field name");
        body!.ShouldContain("name=\"file\"; filename=\"Machine Room.fbk\"");
        body!.ShouldContain("\"name\":\"Machine Room\"");
        output.ToString().ShouldBe($"Machine Room.fbk: added (d1){Environment.NewLine}");
    }

    [Fact]
    public async Task A_default_the_site_will_not_take_fails_the_run_saying_why()
    {
        using var client = new FakeSite((_, _) => FakeSite.Json("""{ "error": "The check is not JSON." }""", HttpStatusCode.BadRequest)).Client();
        var error = new StringWriter();

        var code = await PushDefaultsCommand.Run(
            client, [Write("Drone.fbk", Files.Patch())], BrowserPlugins.Linked(), TextWriter.Null, error, TestContext.Current.CancellationToken);

        code.ShouldBe(Exit.Failed);
        error.ToString().ShouldContain("Drone.fbk: the site answered 400 (Bad Request): The check is not JSON.");
    }

    [Fact]
    public async Task A_default_that_is_refused_fails_the_run_and_is_not_sent()
    {
        var site = new FakeSite((_, _) => FakeSite.Status(HttpStatusCode.OK));
        using var client = site.Client();
        var error = new StringWriter();

        var code = await PushDefaultsCommand.Run(
            client, [Write("Broken.fbk", "{}"u8.ToArray())], BrowserPlugins.Linked(), TextWriter.Null, error, TestContext.Current.CancellationToken);

        code.ShouldBe(Exit.Failed);
        site.Asked.ShouldBeEmpty();
        error.ToString().ShouldContain("Broken.fbk cannot be shared");
    }
}
