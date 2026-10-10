using System.Text;
using Flyback.Engine.Graph;
using Flyback.Editor.Site;
using Flyback.Core.Graph;
using Flyback.Specs.Support;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using Shouldly;
using Flyback.Tests;

namespace Flyback.Specs.Steps;

/// <summary>Presets shared on the preset site, as the editor's gallery finds and opens them.</summary>
[Binding]
public sealed class SharedPresetSteps(EditorDriver editor) : IDisposable
{
    private readonly string kept = Directory.CreateTempSubdirectory("flyback-shared-specs").FullName;
    private readonly FakePresetSite site = new();
    private bool serving;

    public void Dispose()
    {
        site.Dispose();

        if (Directory.Exists(kept)) Directory.Delete(kept, recursive: true);
    }

    [Given("the preset site shares {string}, rated {double} by {int} people")]
    public void GivenShared(string name, double average, int count) => Share(name, average, count, needs: null);

    [Given("the preset site shares {string}, which needs the {string} plugin")]
    public void GivenSharedNeeding(string name, string plugin) => Share(name, 0, 0, needs: plugin);

    private void Share(string name, double average, int count, string? needs)
    {
        site.Presets.Add(new Posted(Id(name), name, "Ann", Average: average, Ratings: count, File: EmptyPatch, Needs: needs));
        Serve();
    }

    /// <summary>An empty patch, which is all a scenario's shared preset has to open as.</summary>
    private static readonly byte[] EmptyPatch = Encoding.UTF8.GetBytes(Empty());

    private static string Empty()
    {
        var patch = new Patch();
        patch.EnsureOutput();

        return PatchIO.ToJson(patch);
    }

    private static string Id(string name) => name.ToLowerInvariant();

    [Given("the editor reaches the preset site")]
    public void Serve()
    {
        if (serving) return;

        serving = true;
        editor.Setup = editor.Setup with
        {
            Host = editor.Setup.Host with { PresetSite = FakePresetSite.Root },
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
    public void ThenDownloadedOnce(string name) => site.Downloaded.GetValueOrDefault(Id(name)).ShouldBe(1);

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

        // Among the last few lines said, not only the last: a headless preview with no OpenGL
        // says so whenever its patience runs out, which a slow machine reaches mid-scenario.
        editor.Reported.ShouldContain($"Opened “{name}” as it was kept, the preset site not answering.");
    }
}
