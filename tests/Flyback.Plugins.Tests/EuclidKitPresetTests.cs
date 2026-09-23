using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The Euclid kit preset, which exists to put Random, Slew, Decay, Euclid, Layer and Line
/// in one patch. Building, compiling and layout are covered for every preset in
/// <see cref="ShippedPresetTests"/>; this checks it holds what it is for.
/// </summary>
public class EuclidKitPresetTests
{
    private const string Name = "Euclid kit";

    [Fact]
    public void It_uses_every_new_module()
    {
        var patch = Build(out _);
        var types = patch.Nodes.Select(n => n.TypeId).ToHashSet();

        foreach (var typeId in new[]
                 {
                     NodeCatalog.RandomTypeId, NodeCatalog.SlewTypeId, "flyback.voice.decay",
                     "flyback.voice.euclid", "flyback.picture.layer", "flyback.picture.line",
                 })
            types.ShouldContain(typeId);
    }

    [Fact]
    public void Its_layers_blend_three_different_ways()
    {
        var patch = Build(out _);

        patch.Nodes
            .Where(n => n.TypeId == "flyback.picture.layer")
            .Select(n => n.StateOf("layer")!["mode"]!.GetValue<string>())
            .ShouldBe(["add", "screen", "difference"], ignoreOrder: true);
    }

    [Fact]
    public void Its_picture_survives_to_the_shader()
    {
        var patch = Build(out var modules);
        var video = patch.CompileForVideo(modules);

        video.Issues.ShouldBeEmpty();

        foreach (var dialect in Enum.GetValues<GlslDialect>())
            GlslEmitter.Emit(video.Program, dialect).PatchFragment.ShouldNotBeNullOrEmpty();
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
