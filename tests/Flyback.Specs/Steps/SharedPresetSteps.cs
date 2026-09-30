using System.Net;
using System.Text;
using System.Text.Json;
using Flyback.App;
using Flyback.App.Site;
using Flyback.Core.Graph;
using Flyback.Specs.Support;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>Presets shared on the preset site, as the editor's gallery finds and opens them.</summary>
[Binding]
public sealed class SharedPresetSteps(Editor editor) : IDisposable
{
    private static readonly Uri Root = new("http://site.test/");

    private readonly string kept = Directory.CreateTempSubdirectory("flyback-shared-specs").FullName;
    private readonly Site site = new();
    private bool serving;

    public void Dispose()
    {
        site.Dispose();

        if (Directory.Exists(kept)) Directory.Delete(kept, recursive: true);
    }

    [Given("the preset site shares {string}, rated {double} by {int} people")]
    public void GivenShared(string name, double average, int count)
    {
        site.Shared.Add((name, average, count));
        Serve();
    }

    [Given("the preset site shares {string}, which needs the {string} plugin")]
    public void GivenSharedNeeding(string name, string plugin)
    {
        site.Needs[name] = plugin;
        GivenShared(name, 0, 0);
    }

    [Given("the editor reaches the preset site")]
    public void Serve()
    {
        if (serving) return;

        serving = true;
        editor.Setup = editor.Setup with
        {
            Host = editor.Setup.Host with { PresetSite = Root },
            Folders = editor.Setup.Folders with { SharedPresetFolder = kept },
        };
        editor.Services += services => services.AddHttpClient(SiteAccess.Client)
            .ConfigurePrimaryHttpMessageHandler(() => site)
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
    }

    [Then("the gallery lists {string} as needing the {string} plugin, and it cannot be picked")]
    public void ThenListedLacking(string name, string plugin)
    {
        var (pickable, lacks) = editor.SharedTile(name);

        pickable.ShouldBeFalse();
        lacks.ShouldNotBeNull().ShouldContain($"Needs the {plugin} plugin");
    }

    [Then("picking {string} from the gallery opens it")]
    public void ThenPickedOpens(string name)
    {
        editor.SharedTile(name).Pickable.ShouldBeTrue();
        editor.PickShared(name);

        editor.Title.ShouldStartWith(name + " — ");
    }

    [When("a letter saying {string} is sent from the status bar")]
    public void WhenLetterSent(string message) => editor.SendLetter("good", message);

    [Then("the preset site has a letter saying {string}")]
    public void ThenSiteHasLetter(string message)
    {
        site.Letters.ShouldHaveSingleItem().ShouldContain(message);
        editor.Reported[^1].ShouldBe("Your letter is on its way. Thank you.");
    }

    [Given("{string} was opened from the gallery")]
    public void GivenOpened(string name)
    {
        editor.SharedInGallery().ShouldContain(tile => tile.Name == name);
        editor.PickShared(name);
    }

    [When("{string} is opened from the gallery again")]
    public void WhenOpenedAgain(string name) => GivenOpened(name);

    [Then("{string} was downloaded once")]
    public void ThenDownloadedOnce(string name) => site.Downloads.GetValueOrDefault(name).ShouldBe(1);

    [When("the preset site stops answering")]
    public void WhenDown() => site.Down = true;

    [Then("the gallery lists {string} under the preset site, rated {string}")]
    public void ThenListed(string name, string rating) =>
        editor.SharedInGallery().ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            tile => tile.Name.ShouldBe(name),
            tile => tile.Stars.ShouldEndWith(rating));

    [Then("picking {string} there opens it")]
    public void ThenOpens(string name)
    {
        editor.PickShared(name);

        editor.Title.ShouldStartWith(name + " — ");
        editor.Reported[^1].ShouldBe($"Opened “{name}” as it was kept, the preset site not answering.");
    }

    /// <summary>The preset site's API as far as the gallery asks it, until it is down.</summary>
    private sealed class Site : HttpMessageHandler
    {
        public List<(string Name, double Average, int Count)> Shared { get; } = [];

        public bool Down { get; set; }

        /// <summary>The plugin each preset needs that a browser page lacks, by the preset's name.</summary>
        public Dictionary<string, string> Needs { get; } = [];

        /// <summary>Each letter posted, as its JSON.</summary>
        public List<string> Letters { get; } = [];

        /// <summary>How many times each preset's file has been sent.</summary>
        public Dictionary<string, int> Downloads { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Down) throw new HttpRequestException("No connection could be made.");

            var path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Post && path == "/api/v1/letters")
            {
                lock (Letters) Letters.Add(request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult());

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            if (path == "/api/v1/presets")
            {
                return Json(new { items = Shared.Select(shared => Item(shared, Needs.GetValueOrDefault(shared.Name))), total = Shared.Count, page = 1, pageSize = 24 });
            }

            if (Shared.FirstOrDefault(s => path == $"/api/v1/presets/{Id(s.Name)}/file") is { Name: not null } file)
            {
                Downloads[file.Name] = Downloads.GetValueOrDefault(file.Name) + 1;

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(File) });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static readonly byte[] File = Encoding.UTF8.GetBytes(Empty());

        private static string Empty()
        {
            var patch = new Patch();
            patch.EnsureOutput();

            return PatchIO.ToJson(patch);
        }

        private static string Id(string name) => name.ToLowerInvariant();

        /// <param name="needs">The plugin a browser page lacks to open it, which the site lists as <c>lacks</c>.</param>
        private static object Item((string Name, double Average, int Count) shared, string? needs) => new
        {
            id = Id(shared.Name),
            name = shared.Name,
            author = "Ann",
            fileName = shared.Name + ".fbk",
            file = $"/api/v1/presets/{Id(shared.Name)}/file",
            rating = new { average = shared.Average, count = shared.Count },
            lacks = needs is null ? null : new { plugins = new[] { new { id = needs.ToLowerInvariant(), name = needs } }, modules = 1, said = $"Needs the {needs} plugin" },
        };

        private static Task<HttpResponseMessage> Json(object body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        });
    }
}
