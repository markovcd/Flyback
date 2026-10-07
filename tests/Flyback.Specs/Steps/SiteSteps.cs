using System.Net;
using System.Text;
using System.Text.Json;
using Flyback.Cli.Commands;
using Flyback.Cli.Models;
using Flyback.Cli.Rendering;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Reqnroll;
using Shouldly;
using Flyback.Site.Checking;
using Flyback.Site.Reading;

namespace Flyback.Specs.Steps;

/// <summary>The preset site's tooling: flyback-site reading a submission, and render-presets talking to the site.</summary>
[Binding]
public sealed class SiteSteps
{
    private const string Waiting = "0199a000000070008000000000000009";
    private const string Unfinished = "0199a00000007000800000000000000a";

    private DirectoryInfo? folder;

    private byte[] submitted = [];
    private string fileName = "submitted.fbk";
    private string verdict = "{}";

    private string patch = string.Empty;
    private readonly List<(string Path, byte[] Body)> sent = [];

    [Given("a submitted patch by {string}, described as {string} and tagged {string}")]
    public void GivenAPatch(string author, string description, string tag)
    {
        var made = new Patch();
        made.EnsureOutput(NodeCatalog.Current);
        made.Credit(author);
        made.Describe(description);
        made.Tag([tag]);

        submitted = Encoding.UTF8.GetBytes(PatchIO.ToJson(made));
    }

    [Given("a submitted patch built on the {string} plugin")]
    public void GivenAPatchOnAPlugin(string plugin) => submitted = Encoding.UTF8.GetBytes($$"""
        {
          "Requires": [ { "Id": "example.{{plugin.ToLowerInvariant()}}", "Name": "{{plugin}}" } ],
          "Nodes": [ { "Id": "8f9d1d3e-0000-4000-8000-000000000012", "TypeId": "example.{{plugin.ToLowerInvariant()}}.glow" } ],
          "Connections": []
        }
        """);

    [Given("a submitted file that is not a patch")]
    public void GivenNotAPatch() => submitted = "{\"hello\": 1}"u8.ToArray();

    [When("flyback-site checks the submission")]
    public void WhenChecked() =>
        verdict = Checks.ToJson(Checks.Of(plugin: false, fileName, submitted, null, BrowserPlugins.Linked(), TextWriter.Null));

    [Then("it is taken, by {string}, described as {string} and tagged {string}")]
    public void ThenTaken(string author, string description, string tag)
    {
        using var read = JsonDocument.Parse(verdict);
        var root = read.RootElement;

        root.GetProperty("accepted").GetBoolean().ShouldBeTrue(verdict);
        root.GetProperty("author").GetString().ShouldBe(author);
        root.GetProperty("description").GetString().ShouldBe(description);
        root.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ShouldBe([tag]);
    }

    [Then("it is taken, saying {string}")]
    public void ThenTakenSaying(string lacks)
    {
        using var read = JsonDocument.Parse(verdict);

        read.RootElement.GetProperty("accepted").GetBoolean().ShouldBeTrue(verdict);
        read.RootElement.GetProperty("lacks").GetProperty("said").GetString().ShouldBe(lacks);
    }

    [Then("it is refused because {string}")]
    public void ThenRefused(string reason)
    {
        using var read = JsonDocument.Parse(verdict);

        read.RootElement.GetProperty("accepted").GetBoolean().ShouldBeFalse(verdict);
        read.RootElement.GetProperty("reason").GetString().ShouldBe(reason);
    }

    [Given("the preset site is waiting on a preset that makes a picture and a sound")]
    public void GivenAPictureAndASound() =>
        patch = "rings(freq: 3, offset: t) |> color.hsv(saturation: _, hue: 0.3) |> out.color\nsine(freq: 110) |> out.left\n";

    [Given("the preset site is waiting on a preset that names a module nothing here has")]
    public void GivenAModuleNothingHas() =>
        patch = """{"Nodes":[{"Id":"8f9d1d3e-0000-4000-8000-000000000031","TypeId":"example.nowhere.module"}],"Connections":[]}""";

    [When("flyback-cli render-presets makes a pass with no media folder")]
    public Task WhenAPassIsMade() => Pass(stillOnly: false);

    [When("flyback-cli render-presets makes a pass with no media folder, the still alone")]
    public Task WhenAStillIsMade() => Pass(stillOnly: true);

    [Then("the site is sent the preset's still and done, and nothing else")]
    public void ThenOnlyTheStill() => Names().ShouldBe(["webp", "done"]);

    private async Task Pass(bool stillOnly)
    {
        var text = patch.StartsWith('{');
        using var site = new HttpClient(new Site(Waiting, text ? "preset.fbk" : "preset.fbks", patch, sent)) { BaseAddress = new Uri("https://presets.example.org/") };

        await RenderPresetsCommand.Pass(site, new PresetRender(new StandIn(), new MediaUpload(site), stillOnly), TextWriter.Null, TextWriter.Null, CancellationToken.None);
    }

    [Then("the site is sent the preset's still, loop, track and bars")]
    public void ThenSentEverything() =>
        Names().ShouldBe(["webp", "webm", "mp3", "peaks.json", "done"], ignoreOrder: true);

    [Then("it is told the render is done after the rest")]
    public void ThenDoneLast() => Names()[^1].ShouldBe("done");

    [Then("the site is told the render failed because the patch did not open whole")]
    public void ThenFailed()
    {
        Names().ShouldBe(["failed"]);
        Encoding.UTF8.GetString(sent.Single().Body).ShouldStartWith("The patch did not open whole.");
    }

    [Given("a folder where render-presets left a finished render and an unfinished one")]
    public void GivenAFolder()
    {
        folder = Directory.CreateTempSubdirectory("flyback-renders-");
        File.WriteAllText(Path.Combine(folder.FullName, Waiting + ".webp"), "still");
        File.WriteAllText(Path.Combine(folder.FullName, Waiting + ".mp3"), "track");
        File.WriteAllText(Path.Combine(folder.FullName, Waiting + ".done"), "");
        File.WriteAllText(Path.Combine(folder.FullName, Unfinished + ".webp"), "half");
    }

    [When("flyback-site sends the folder to the preset site")]
    public async Task WhenTheFolderIsSent()
    {
        using var site = new HttpClient(new Site(Waiting, "preset.fbk", "{}", sent)) { BaseAddress = new Uri("https://presets.example.org/") };

        try
        {
            (await Flyback.Site.Commands.PushMediaCommand.Run(site, folder!, TextWriter.Null, TextWriter.Null, CancellationToken.None)).ShouldBe(0);
        }
        finally
        {
            folder!.Delete(recursive: true);
        }
    }

    [Then("the site is sent the finished render's files, and done after them")]
    public void ThenTheFinishedRenderIsSent() => Names().ShouldBe(["webp", "mp3", "done"]);

    [Then("nothing of the unfinished render is sent")]
    public void ThenNothingUnfinished() => sent.ShouldNotContain(s => s.Path.Contains(Unfinished, StringComparison.Ordinal));

    private string[] Names() =>
        [.. sent.Select(s => s.Path).Where(p => p.StartsWith($"/api/v1/admin/presets/{Waiting}/media/", StringComparison.Ordinal)).Select(p => p[(p.LastIndexOf('/') + 1)..])];

    /// <summary>The preset site as render-presets sees it: one preset waiting, and every upload kept.</summary>
    private sealed class Site(string id, string fileName, string patch, List<(string Path, byte[] Body)> sent) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Put)
            {
                sent.Add((path, await request.Content!.ReadAsByteArrayAsync(cancellationToken)));
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return path switch
            {
                "/api/v1/presets" => Json($$"""{"items":[{"id":"{{id}}","name":"Waiting","fileName":"{{fileName}}"}]}"""),
                _ when path == $"/api/v1/presets/{id}/file" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(patch) },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    /// <summary>The renderer and ffmpeg, standing in: each writes the file it was asked for, and the track has a loudness to measure.</summary>
    private sealed class StandIn : IPresetTools
    {
        public int Render(Opened patch, RenderOptions options, TextWriter error, CancellationToken cancellation)
        {
            File.WriteAllText(options.Out.FullName, options.Out.Name);
            return Cli.Common.Exit.Ok;
        }

        public Task<Ran> Ffmpeg(IReadOnlyList<string> arguments, CancellationToken cancellation)
        {
            var line = string.Join(' ', arguments);

            if (line.Contains("print_format=json", StringComparison.Ordinal))
                return Task.FromResult(new Ran(0, [], """{ "input_i" : "-18", "input_tp" : "-3", "input_lra" : "5", "input_thresh" : "-28", "target_offset" : "0" }"""));

            if (arguments[^1] == "-")
            {
                var samples = Enumerable.Range(0, 8000).Select(i => MathF.Sin(i * 0.1f) * 0.5f).ToArray();
                var raw = new byte[samples.Length * sizeof(float)];
                Buffer.BlockCopy(samples, 0, raw, 0, raw.Length);
                return Task.FromResult(new Ran(0, raw, ""));
            }

            File.WriteAllText(arguments[^1], line);
            return Task.FromResult(new Ran(0, [], ""));
        }
    }
}
