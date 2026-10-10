using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flyback.Engine.Graph;
using Flyback.Specs.Support;
using Reqnroll;
using Reqnroll.UnitTestProvider;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>
/// The web viewer's and the web editor's pages in a headless Chromium, served by
/// <see cref="PageServer"/>, driven through <c>window.flyback</c> and a finger.
/// </summary>
[Binding]
public sealed class BrowserSteps(PatchContext context, IUnitTestRuntimeProvider runtime) : IDisposable
{
    /// <summary>How long a page may take to open a patch: it loads the runtime, interpreted, and a slow machine takes several times what a fast one does.</summary>
    private static readonly TimeSpan Opening = TimeSpan.FromMinutes(1);

    /// <summary>How long the sound may take to be heard or the microphone to open once asked; each takes well under a second.</summary>
    private static readonly TimeSpan Answering = TimeSpan.FromSeconds(15);

    /// <summary>How long a gesture is given to have paused the page before it is taken not to have; a tap pauses at once.</summary>
    private static readonly TimeSpan Unanswered = TimeSpan.FromMilliseconds(500);

    /// <summary>Chrome on a phone, as Android's own browser names itself.</summary>
    private const string Android = "Mozilla/5.0 (Linux; Android 14; Pixel 9 Pro) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/155.0.0.0 Mobile Safari/537.36";

    private PageServer? server;
    private Chromium? browser;
    private string? answer;

    private BrowserPage Page => browser?.Page ?? throw new InvalidOperationException("No page is open in the browser.");

    [Given("the web viewer is open in a browser on the preset {string}")]
    public void GivenTheViewerOnAPreset(string preset) => OpenViewer($"preset={Uri.EscapeDataString(preset)}");

    [Given("the web viewer is open in Android's browser on the preset {string}")]
    public void GivenTheViewerOnAndroid(string preset) => OpenViewer($"preset={Uri.EscapeDataString(preset)}", Android);

    [Given("the web viewer is open in a browser on the patch")]
    public void GivenTheViewerOnThePatch() => OpenViewer(OfferPatch());

    [Given("the web editor is open in a browser on the preset {string}")]
    public void GivenTheEditorOnAPreset(string preset) => OpenEditor($"preset={Uri.EscapeDataString(preset)}", preset);

    [Given("the web editor is open in a browser on the patch")]
    public void GivenTheEditorOnThePatch() => OpenEditor(OfferPatch(), "patch");

    private void OpenViewer(string query, string? userAgent = null)
    {
        Open($"viewer/?{query}", userAgent);

        // The Play button is enabled once the patch is open, sound and all.
        Page.Until("!document.getElementById('play').disabled || flyback.status().error", "the web viewer did not open its patch", Opening);
        ((string?)Status()["error"]).ShouldBeNull();
    }

    /// <summary>
    /// Opens the editor on <paramref name="query"/>, and waits for its tab to be titled
    /// <paramref name="title"/>: it starts on a preset of its own first.
    /// </summary>
    private void OpenEditor(string query, string title)
    {
        Open($"editor/?{query}");

        var titled = $"document.title.startsWith({JsonSerializer.Serialize(title + " ")})";
        Page.Until($"{titled} || /not open/i.test(flyback.state().said)", $"the web editor did not open {title}", Opening);
        ((bool?)Page.Evaluate(titled)).ShouldBe(true, (string?)Page.Evaluate("flyback.state().said"));
    }

    private void Open(string address, string? userAgent = null)
    {
        server ??= new PageServer();
        browser = Chromium.Start(runtime);

        if (userAgent is not null) Page.Pretend(userAgent);
        Page.Go(new Uri(server.Root, address));
    }

    /// <summary>The scenario's patch served as a file, and the query that opens it.</summary>
    private string OfferPatch()
    {
        server ??= new PageServer();
        var file = server.Offer("patch.fbk", Encoding.UTF8.GetBytes(PatchIO.ToJson(context.Patch)));

        return $"file={Uri.EscapeDataString(file.AbsolutePath)}&name=patch.fbk";
    }

    [When("the web viewer's picture is tapped")]
    public void WhenThePictureIsTapped()
    {
        var (x, y) = Middle("screen");
        Page.Touch(x, y);
    }

    [When("a finger drags across the web viewer's picture")]
    public void WhenAFingerDrags()
    {
        var (x, y) = Middle("screen");
        Page.Touch(x - 60, y, 120, 0);
    }

    [When("a mouse drags across the web viewer's picture")]
    public void WhenAMouseDrags()
    {
        var (x, y) = Middle("screen");
        Page.Drag(x - 60, y, 120, 0);
    }

    [When("a script plays the web viewer")]
    public void WhenAScriptPlays() => Page.Evaluate("flyback.play()");

    [Then("the web viewer plays, its sound heard")]
    public void ThenItPlaysHeard()
    {
        Page.Until("flyback.status().sound && flyback.status().time > 0.25", "the web viewer's speakers did not become its clock", Answering);

        var status = Status();
        ((bool?)status["playing"]).ShouldBe(true);
        ((string?)status["held"]).ShouldBeNull();
        ((double?)status["rendered"]).ShouldNotBeNull().ShouldBeGreaterThan(0);
    }

    [Then("the web viewer plays its picture alone, saying the browser holds the sound back")]
    public void ThenThePictureAlone()
    {
        var status = Status();

        ((bool?)status["playing"]).ShouldBe(true);
        ((bool?)status["sound"]).ShouldBe(false);
        ((string?)status["held"]).ShouldNotBeNull().ShouldContain("holds the sound back");
    }

    [Then("its sound goes through a media element, which takes the phone's audio focus")]
    public void ThenThroughAMediaElement()
    {
        var deadline = DateTime.UtcNow + Answering;
        while (Page.MediaPlayers == 0 && DateTime.UtcNow < deadline) Thread.Sleep(50);

        Page.MediaPlayers.ShouldBe(1, "the browser made no media player, so the sound went straight to the speakers and the phone keeps playing what it was");
    }

    [Then("its sound goes straight to the speakers, through no media element")]
    public void ThenStraightToTheSpeakers() =>
        Page.MediaPlayers.ShouldBe(0, "the sound went through a media element, which only Android needs");

    [Then("the web viewer is still playing")]
    public void ThenStillPlaying()
    {
        Thread.Sleep(Unanswered);
        ((bool?)Status()["playing"]).ShouldBe(true);
    }

    [Then("the web viewer is paused")]
    public void ThenPaused() => Page.Until("!flyback.status().playing", "the web viewer did not pause", Answering);

    /// <summary>Pixels lit on the WebGL canvas; the canvas is black with nothing drawn on it.</summary>
    [Then("the web viewer's picture is drawn")]
    public void ThenThePictureIsDrawn()
    {
        var lit = Page.Evaluate("""
            (() => {
              const canvas = document.getElementById('screen');
              const gl = canvas.getContext('webgl2');
              const pixels = new Uint8Array(canvas.width * canvas.height * 4);
              gl.readPixels(0, 0, canvas.width, canvas.height, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
              let lit = 0;
              for (let i = 0; i < pixels.length; i += 4) if (pixels[i] + pixels[i + 1] + pixels[i + 2] > 30) lit++;
              return lit / (canvas.width * canvas.height);
            })()
            """);

        ((double?)lit).ShouldNotBeNull().ShouldBeGreaterThan(0.01, "the picture is all but black");
    }

    [Then("the web viewer has the browser's microphone open")]
    public void ThenTheMicrophoneIsOpen()
    {
        Page.Until("flyback.status().microphone.listening || flyback.status().microphone.trouble", "the web viewer did not ask for the microphone", Answering);

        var microphone = Status()["microphone"]!;
        ((string?)microphone["trouble"]).ShouldBeNull();
        ((bool?)Status()["lineIn"]).ShouldBe(true);
    }

    [Then("the web viewer has let the browser's microphone go")]
    public void ThenTheMicrophoneIsLetGo()
    {
        ThenPaused();
        var status = Status();

        ((bool?)status["microphone"]!["wanted"]).ShouldBe(false);
        ((bool?)status["microphone"]!["listening"]).ShouldBe(false);
    }

    [Then("the web editor says it started on {string}, drawn with WebGL")]
    public void ThenTheEditorStarted(string preset)
    {
        var state = Page.Evaluate("flyback.state()")!;

        ((bool?)state["started"]).ShouldBe(true);
        ((string?)state["preset"]).ShouldBe(preset);
        ((string?)state["renderer"]).ShouldBe("WebGL");
        ((int?)state["modules"]).ShouldNotBeNull().ShouldBeGreaterThan(0);
    }

    [When("a script applies the web editor's text with {string} changed to {string}")]
    public void WhenAScriptApplies(string from, string to)
    {
        var text = (string)Page.Evaluate("flyback.text()")!;
        text.ShouldContain(from);

        answer = (string?)Page.Evaluate($"flyback.apply({JsonSerializer.Serialize(text.Replace(from, to))})");
    }

    [Then("the web editor tells the script nothing is wrong")]
    public void ThenNothingIsWrong() => answer.ShouldBeNull();

    [Then("the web editor's text has {string}")]
    public void ThenTheTextHas(string text) => ((string)Page.Evaluate("flyback.text()")!).ShouldContain(text);

    [Then("the web editor says the patch is edited")]
    public void ThenEdited() => ((bool?)Page.Evaluate("flyback.state()")!["unsaved"]).ShouldBe(true);

    /// <summary>Chrome on Android resizes only what it is told to; with this, the page shrinks and a box low on the screen stays in view.</summary>
    [Then("the page lets the phone's keyboard shrink it")]
    public void ThenTheKeyboardShrinksThePage() =>
        ((string)Page.Evaluate("document.querySelector('meta[name=viewport]').content")!).ShouldContain("interactive-widget=resizes-content");

    /// <summary>Safari zooms in on a focused field smaller than 16px, and the page refuses the pinch that would zoom back out.</summary>
    [Then("the field the editor types through is big enough that an iPhone does not zoom in on it")]
    public void ThenTheInputIsBigEnough() =>
        ((string)Page.Evaluate("getComputedStyle(document.querySelector('.avalonia-input-element')).fontSize")!).ShouldBe("16px");

    private JsonNode Status() => Page.Evaluate("flyback.status()")!;

    /// <summary>The middle of the element <paramref name="id"/> names, in the page's pixels.</summary>
    private (double X, double Y) Middle(string id)
    {
        var box = Page.Evaluate($"(({{ x, y, width, height }}) => ({{ x, y, width, height }}))(document.getElementById('{id}').getBoundingClientRect())")!;
        return ((double)box["x"]! + (double)box["width"]! / 2, (double)box["y"]! + (double)box["height"]! / 2);
    }

    public void Dispose()
    {
        browser?.Dispose();
        server?.Dispose();
    }
}
