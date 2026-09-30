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

    public void Dispose()
    {
        site.Dispose();

        if (Directory.Exists(kept)) Directory.Delete(kept, recursive: true);
    }

    [Given("the preset site shares {string}, rated {double} by {int} people")]
    public void GivenShared(string name, double average, int count)
    {
        site.Shared.Add((name, average, count));

        editor.Setup = editor.Setup with { PresetSite = Root, SharedPresetFolder = kept };
        editor.Services = services => services.AddHttpClient(SiteAccess.Client)
            .ConfigurePrimaryHttpMessageHandler(() => site)
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
    }

    [Given("{string} was opened from the gallery")]
    public void GivenOpened(string name)
    {
        editor.SharedInGallery().ShouldContain(tile => tile.Name == name);
        editor.PickShared(name);
    }

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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Down) throw new HttpRequestException("No connection could be made.");

            var path = request.RequestUri!.AbsolutePath;

            if (path == "/api/v1/presets")
                return Json(new { items = Shared.Select(Item), total = Shared.Count, page = 1, pageSize = 24 });

            if (Shared.FirstOrDefault(s => path == $"/api/v1/presets/{Id(s.Name)}/file") is { Name: not null })
            {
                var patch = new Patch();
                patch.EnsureOutput();

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(PatchIO.ToJson(patch)) });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static string Id(string name) => name.ToLowerInvariant();

        private static object Item((string Name, double Average, int Count) shared) => new
        {
            id = Id(shared.Name),
            name = shared.Name,
            author = "Ann",
            fileName = shared.Name + ".fbk",
            file = $"/api/v1/presets/{Id(shared.Name)}/file",
            rating = new { average = shared.Average, count = shared.Count },
        };

        private static Task<HttpResponseMessage> Json(object body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        });
    }
}
