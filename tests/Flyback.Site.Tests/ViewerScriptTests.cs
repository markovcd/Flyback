using Flyback.Tests;
using Shouldly;
using Xunit;

namespace Flyback.Site.Tests;

/// <summary>
/// The web viewer's <c>main.js</c>, read as text. It runs only in a browser, on the wasm build,
/// a worker and WebGL, so these hold the lines that keep a phone's behavior rather than running them.
/// </summary>
public sealed class ViewerScriptTests
{
    private static readonly string Script = File.ReadAllText(Repository.Path("src", "Flyback.Viewer.Web", "wwwroot", "main.js"));

    [Fact]
    public void The_screen_is_kept_on_while_the_picture_has_the_whole_screen_and_plays()
    {
        Script.ShouldContain("const wanted = playing() && document.fullscreenElement != null && !document.hidden;");
        Script.ShouldContain("navigator.wakeLock.request('screen')");
        Script.ShouldContain("document.addEventListener('fullscreenchange', keepAwake);");
        Script.ShouldMatch("""speakers\.start\([^)]*\);(?:[^\n]*\n){1,6}\s*keepAwake\(\);""");
        Script.ShouldMatch("""speakers\.stop\([^)]*\);(?:[^\n]*\n){1,6}\s*keepAwake\(\);""");
    }

    [Fact]
    public void Sizes_run_up_to_1080p_and_a_picture_turned_off_draws_nothing()
    {
        Script.ShouldContain("[1920, 1080]");
        Script.ShouldContain("ui.size.add(new Option('Picture off', OFF));");
        Script.ShouldContain("if (noPicture !== null || !pictureOn) return;");
        Script.ShouldContain("ui.size.value === OFF ? setPicture(false)");
    }

    [Fact]
    public void Landscape_is_asked_for_once_full_screen_is_granted()
    {
        Script.ShouldContain("requestFullscreen?.().then(turnSideways");
        Script.ShouldContain("screen.orientation?.lock?.('landscape')");
    }

    [Fact]
    public void Back_goes_to_the_presets_page_unless_the_address_says_otherwise() =>
        Script.ShouldContain("params.get('back') ?? 'presets.html'");
}
