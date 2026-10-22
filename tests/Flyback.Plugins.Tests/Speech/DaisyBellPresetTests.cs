using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Speech;

/// <summary>
/// The Daisy Bell preset, a sung Speak with a Mouth. Building and layout are covered
/// for every preset in <see cref="ShippedPresetTests"/>; this checks it holds what it is for.
/// </summary>
public class DaisyBellPresetTests
{
    private const string Name = "Daisy Bell";

    [Fact]
    public void Its_caption_is_the_words_it_sings()
    {
        var patch = Build(out _);

        var singer = patch.Nodes.Single(n => n.TypeId == "flyback.speech.speak").StateOf("speak")!;
        var caption = patch.Nodes.Single(n => n.TypeId == "flyback.picture.text").StateOf("text")!;

        singer["mode"]!.GetValue<string>().ShouldBe("sing");
        caption["lines"]!.GetValue<string>().ShouldBe(singer["words"]!.GetValue<string>());
    }

    [Fact]
    public void Its_picture_compiles_clean()
    {
        var patch = Build(out var modules);

        patch.CompileForVideo(modules).Issues.ShouldBeEmpty();
    }

    [Fact]
    public void Its_sound_compiles_clean()
    {
        var patch = Build(out var modules);

        patch.CompileForAudio(modules).Issues.ShouldBeEmpty();
    }

    private static Patch Build(out ModuleCatalog modules)
    {
        var loaded = ShippedPlugins.Loaded;
        modules = loaded.Modules;

        return loaded.Presets.Single(p => p.Name == Name).Build(loaded.Modules);
    }
}
