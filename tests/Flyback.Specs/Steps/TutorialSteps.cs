using System.Net;
using System.Text.RegularExpressions;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Reqnroll;
using Shouldly;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Language;
using Flyback.Engine.Render;
using Flyback.Plugins.Hosting;

namespace Flyback.Specs.Steps;

/// <summary>The website's tutorials against the presets they bake, read from the pages the build copies beside the tests.</summary>
[Binding]
public sealed partial class TutorialSteps
{
    private static readonly Lazy<PluginCatalog> Installed = new(() => PluginHost.Load(PluginHost.DefaultDirectory, PluginTrust.Shipped(PluginHost.DefaultDirectory)));

    /// <summary>Frames drawn one after another, so a trail has a past to show: two seconds at thirty a second.</summary>
    private const int Frames = 60;

    private const int Width = 64, Height = 36;

    private Patch? built;

    /// <summary>The text block the page marks as the finished patch, built with every shipped plugin.</summary>
    [Given("the patch the tutorial {string} ends with")]
    public void GivenTheTutorialsPatch(string page)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "wwwroot", page);
        var finished = Finished().Match(File.ReadAllText(path));

        finished.Success.ShouldBeTrue($"{page} marks no block as the finished patch");

        var load = PatchLanguage.Build(WebUtility.HtmlDecode(finished.Groups["text"].Value), Installed.Value.Modules);

        load.Ok.ShouldBeTrue(load.Report);
        built = load.Patch;
    }

    /// <summary>
    /// The same instrument is the same sound and the same pictures, to the bit. The two
    /// list their modules in another order, so their programs are compared by what they
    /// make rather than op by op.
    /// </summary>
    [Then("it is the same instrument as the shipped preset {string}")]
    public void ThenItIsThePreset(string name)
    {
        var modules = Installed.Value.Modules;
        var preset = Installed.Value.Presets.Single(p => p.Name == name).Build(modules);

        built.ShouldNotBeNull();

        Heard(built, modules).ShouldBe(Heard(preset, modules), "the sound differs");
        Seen(built, modules).ShouldBe(Seen(preset, modules), "the picture differs");

        (built.Controls ?? []).Select(knob => (knob.Name, knob.Value))
            .ShouldBe((preset.Controls ?? []).Select(knob => (knob.Name, knob.Value)), "the panel differs");
    }

    /// <summary>Two seconds of the sound, through the real renderer.</summary>
    private static float[] Heard(Patch patch, ModuleCatalog modules)
    {
        var buffer = new float[2 * Flyback.Core.GlobalConstants.SampleRate * 2];

        new AudioRenderer().Render(Compiled(patch.CompileForAudio(modules)), buffer);
        return buffer;
    }

    /// <summary>Two seconds of frames, every one of them, from a cold renderer.</summary>
    private static byte[] Seen(Patch patch, ModuleCatalog modules)
    {
        var program = Compiled(patch.CompileForVideo(modules));
        var renderer = new SynthRenderer();
        var stride = Width * 4;
        var frames = new byte[stride * Height * Frames];
        var buffer = new byte[stride * Height];

        for (var frame = 0; frame < Frames; frame++)
        {
            renderer.Render(program, frame / 30.0, Width, Height, buffer, stride);
            buffer.CopyTo(frames, frame * buffer.Length);
        }

        return frames;
    }

    private static CompiledPatch Compiled(CompileResult result)
    {
        result.Issues.Where(i => i.Severity == IssueSeverity.Error).ShouldBeEmpty();
        return result.Program;
    }

    [GeneratedRegex("""<code class="fbks" data-finished>(?<text>.*?)</code>""", RegexOptions.Singleline)]
    private static partial Regex Finished();
}
