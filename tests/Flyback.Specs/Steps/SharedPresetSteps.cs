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

    [Given("the gallery lists {string} under the preset site")]
    public void GivenListed(string name) => editor.SharedInGallery().ShouldContain(tile => tile.Name == name);

    [Given("the preset site has since replaced the file of {string}")]
    public void GivenReplaced(string name) => site.Replaced.Add(name);

    [When("{string} is taken off the preset site")]
    public void WhenTakenDown(string name) => site.TakenDown.Add(name);

    [When("{string} is picked there")]
    public void WhenPicked(string name) => editor.PickShared(name);

    [Then("the editor says {string}")]
    public void ThenSays(string said) => editor.Reported[^1].ShouldBe(said);

    [Then("while the preset site does not answer, the gallery lists nothing kept from it")]
    public void ThenNothingKept()
    {
        site.Down = true;

        editor.SharedInGallery().ShouldBeEmpty();
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

        /// <summary>The names whose file the site has replaced since it was first shared.</summary>
        public HashSet<string> Replaced { get; } = [];

        /// <summary>How many times each preset's file has been sent.</summary>
        public Dictionary<string, int> Downloads { get; } = [];

        /// <summary>The names taken off the site, which it answers it does not have.</summary>
        public HashSet<string> TakenDown { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Down) throw new HttpRequestException("No connection could be made.");

            var path = request.RequestUri!.AbsolutePath;

            if (path == "/api/v1/presets")
            {
                var listed = Shared.Where(s => !TakenDown.Contains(s.Name)).ToList();

                return Json(new { items = listed.Select(Item), total = listed.Count, page = 1, pageSize = 24 });
            }

            if (Shared.FirstOrDefault(s => path == $"/api/v1/presets/{Id(s.Name)}/file") is { Name: not null } file && !TakenDown.Contains(file.Name))
            {
                Downloads[file.Name] = Downloads.GetValueOrDefault(file.Name) + 1;

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(FileOf(file.Name)) });
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

        /// <summary>The file as shared, or a byte longer where the site has replaced it.</summary>
        private byte[] FileOf(string name) => Replaced.Contains(name) ? [.. File, (byte)' '] : File;

        private static string Id(string name) => name.ToLowerInvariant();

        private object Item((string Name, double Average, int Count) shared) => new
        {
            id = Id(shared.Name),
            name = shared.Name,
            author = "Ann",
            fileName = shared.Name + ".fbk",
            size = FileOf(shared.Name).LongLength,
            file = $"/api/v1/presets/{Id(shared.Name)}/file",
            rating = new { average = shared.Average, count = shared.Count },
        };

        private static Task<HttpResponseMessage> Json(object body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        });
    }
}
