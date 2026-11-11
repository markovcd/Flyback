using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flyback.Specs.Support;
using Flyback.Tests;
using Reqnroll;
using Reqnroll.UnitTestProvider;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>
/// A page embedding a patch through <c>embed.js</c>, served beside the viewer by
/// <see cref="PageServer"/> and clicked in a headless Chromium.
/// </summary>
[Binding]
public sealed class EmbedSteps(IUnitTestRuntimeProvider runtime) : IDisposable
{
    /// <summary>How long the player may take to open and be heard: it loads the runtime, interpreted.</summary>
    private static readonly TimeSpan Opening = TimeSpan.FromMinutes(1);

    /// <summary>How long a button may take to follow the player; the frame tells it within a quarter second.</summary>
    private static readonly TimeSpan Answering = TimeSpan.FromSeconds(15);

    /// <summary>The player's frame, whose page is the viewer's, on this server's origin.</summary>
    private const string Viewer = "document.querySelector('.flyback-player iframe').contentWindow.flyback";

    private PageServer? server;
    private Chromium? browser;
    private string text = string.Empty;

    private BrowserPage Page => browser?.Page ?? throw new InvalidOperationException("No page is open in the browser.");

    [Given("a page embeds the preset {string} to open {string}")]
    public void GivenAnEmbed(string preset, string opens) => Embed(preset, opens, phone: false);

    [Given("a page on a phone embeds the preset {string} to open {string}")]
    public void GivenAnEmbedOnAPhone(string preset, string opens) => Embed(preset, opens, phone: true);

    [Given("a page embeds the preset {string} to open in its element {string}")]
    public void GivenAnEmbedInAnElement(string preset, string element) => Embed(preset, element, phone: false);

    private void Embed(string preset, string opens, bool phone)
    {
        BuiltSite.Need(runtime);

        text = File.ReadAllText(Path.Combine(Repository.Root, "src", "Flyback.Engine", "Graph", "Presets", preset + ".fbks")).Trim().ReplaceLineEndings("\n");

        server = new PageServer();
        var page = server.Offer("host.html", Encoding.UTF8.GetBytes($"""
            <!doctype html>
            <html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Host</title></head>
            <body><p>Someone's post.</p>
            {(opens.StartsWith('#') ? $"""<div id="{WebUtility.HtmlEncode(opens[1..])}"><p id="held">What the element held.</p></div>""" : "")}
            <pre data-flyback="{WebUtility.HtmlEncode(preset)}" data-opens="{WebUtility.HtmlEncode(opens)}">
            {WebUtility.HtmlEncode(text)}
            </pre>
            <script src="/embed.js" async></script></body></html>
            """));

        browser = Chromium.Start(runtime);
        if (phone)
            Page.Send("Emulation.setDeviceMetricsOverride", new JsonObject { ["width"] = 390, ["height"] = 844, ["deviceScaleFactor"] = 2, ["mobile"] = true });

        Page.Go(page);
        Page.Until("document.querySelector('.flyback-embed .fe-play')", "embed.js did not turn the pre into a code block", Answering);
    }

    [Then("the embedded code is colored and numbered, with Copy and Play")]
    public void ThenTheCodeIsShown()
    {
        var block = Page.Evaluate("""
            (() => {
              const block = document.querySelector('.flyback-embed');
              return {
                code: block.querySelector('.fe-code').textContent,
                colored: block.querySelectorAll('.fe-code .t-key, .fe-code .t-mod, .fe-code .t-sink').length,
                lines: block.querySelector('.fe-lines').textContent.split('\n').length,
                copy: !!block.querySelector('button.fe-copy'),
                play: block.querySelector('button.fe-play').textContent.trim(),
              };
            })()
            """)!;

        ((string?)block["code"]).ShouldBe(text);
        ((int?)block["colored"]).ShouldNotBeNull().ShouldBeGreaterThan(5);
        ((int?)block["lines"]).ShouldBe(text.Split('\n').Length);
        ((bool?)block["copy"]).ShouldBe(true);
        ((string?)block["play"]).ShouldBe("Play");
    }

    [When("the embedded patch's Play button is clicked")]
    public void WhenPlayIsClicked() => Click(".flyback-embed .fe-play");

    [When("the embedded player's close button is clicked")]
    public void WhenCloseIsClicked() => Click(".flyback-player .fp-close");

    [Then("the web viewer opens over the page, playing the embedded patch with its sound heard")]
    public void ThenOverThePage() => ThenOpens("overlay");

    [Then("the web viewer opens docked beside the page, playing the embedded patch with its sound heard")]
    public void ThenBesideThePage() => ThenOpens("side");

    [Then("the web viewer plays in that element, replacing what it held, with its sound heard")]
    public void ThenInTheElement()
    {
        Heard();

        ((bool?)Page.Evaluate("document.querySelector('#picture .flyback-player.inline iframe') !== null")).ShouldBe(true);
        ((bool?)Page.Evaluate("getComputedStyle(document.querySelector('#held')).display === 'none'")).ShouldBe(true);
        ((bool?)Page.Evaluate("document.querySelector('.flyback-player.overlay, .flyback-player.side') === null")).ShouldBe(true);
        ((bool?)Page.Evaluate("document.querySelector('.fp-close') === null")).ShouldBe(true);
    }

    [When("the embedded patch's Pause button is clicked")]
    public void WhenPauseIsClicked() => Click(".flyback-embed .fe-play");

    private void Heard()
    {
        Page.Until($"{Viewer}.status().error || ({Viewer}.status().sound && {Viewer}.status().time > 0.25)", "the embedded player was not heard", Opening);

        var status = Page.Evaluate($"{Viewer}.status()")!;
        ((string?)status["error"]).ShouldBeNull();
        ((bool?)status["playing"]).ShouldBe(true);
        ((string?)status["held"]).ShouldBeNull();
    }

    private void ThenOpens(string where)
    {
        Heard();

        ((bool?)Page.Evaluate($"document.querySelector('.flyback-player').classList.contains({JsonSerializer.Serialize(where)})")).ShouldBe(true);
        ((bool?)Page.Evaluate("document.documentElement.classList.contains('flyback-side-open')")).ShouldBe(where == "side");
    }

    [Then("the embedded patch's button says {string}")]
    public void ThenTheButtonSays(string label) =>
        Page.Until($"document.querySelector('.flyback-embed .fe-play').textContent.trim() === {JsonSerializer.Serialize(label)}", $"the embedded patch's button did not say {label}", Answering);

    [Then("the player is gone and the embedded patch's button says {string}")]
    public void ThenThePlayerIsGone(string label)
    {
        ((bool?)Page.Evaluate("document.querySelector('.flyback-player') === null")).ShouldBe(true);
        ((bool?)Page.Evaluate("document.documentElement.classList.contains('flyback-side-open')")).ShouldBe(false);
        ThenTheButtonSays(label);
    }

    /// <summary>A mouse click in the middle of what <paramref name="selector"/> finds, which is what lets a page start its sound.</summary>
    private void Click(string selector)
    {
        var box = Page.Evaluate($"(({{ x, y, width, height }}) => ({{ x, y, width, height }}))(document.querySelector({JsonSerializer.Serialize(selector)}).getBoundingClientRect())")!;
        Page.Drag((double)box["x"]! + (double)box["width"]! / 2, (double)box["y"]! + (double)box["height"]! / 2, 0, 0);
    }

    public void Dispose()
    {
        browser?.Dispose();
        server?.Dispose();
    }
}
