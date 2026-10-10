using System.Text.RegularExpressions;
using Flyback.Engine.Graph;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Hosting;

/// <summary>
/// Every module a user patches, the engine's and every shipped plugin's, is named by a feature in
/// <c>tests/Flyback.Specs</c>, where its requirement is stated. A module an Expression stands for is not patched.
/// </summary>
public sealed class ModuleScenarioTests
{
    /// <summary>Every feature's text, as one.</summary>
    private static readonly string Features = string.Join(
        '\n', Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Features"), "*.feature").Select(File.ReadAllText));

    /// <summary>
    /// The modules no feature named when this rule was made. One leaves the list in the commit that gives it a
    /// scenario, and none joins it: a module added since is named by a feature before it lands.
    /// </summary>
    private static readonly HashSet<string> Unwritten =
    [
        "audio.note", "audio.quantiser", "audio.tune",
        "color.gain", "color.hsv", "color.ink", "color.mix", "color.rgb", "color.split", "color.vignette",
        "coord",
        "env.adsr",
        "feedback.blur", "feedback.trails",
        "flyback.drawings.perspective", "flyback.drawings.rotate3d", "flyback.drawings.scale3d", "flyback.drawings.translate3d",
        "flyback.effects.chorus", "flyback.effects.echo", "flyback.effects.flanger", "flyback.effects.phaser",
        "flyback.figures.harmonograph", "flyback.figures.overtones", "flyback.figures.plate",
        "flyback.mastering.compressor", "flyback.mastering.crossover", "flyback.mastering.eq", "flyback.mastering.limiter", "flyback.mastering.loudness", "flyback.mastering.maximizer", "flyback.mastering.width",
        "flyback.picture.arc", "flyback.picture.box", "flyback.picture.cells", "flyback.picture.circle", "flyback.picture.combine", "flyback.picture.fill", "flyback.picture.fractal", "flyback.picture.grade", "flyback.picture.hsv", "flyback.picture.layer", "flyback.picture.palette", "flyback.picture.polygon", "flyback.picture.posterise", "flyback.picture.star",
        "flyback.voice.bell", "flyback.voice.crush", "flyback.voice.decay", "flyback.voice.euclid", "flyback.voice.fade", "flyback.voice.fm", "flyback.voice.fold", "flyback.voice.hiss", "flyback.voice.osc", "flyback.voice.stroke", "flyback.voice.wander",
        "math.clamp", "math.desk", "math.mix", "math.mixer", "math.remap", "math.smoothstep",
        "meter",
        "pattern.checker", "pattern.rings",
        "poly.spread",
        "scan",
        "seq.hold", "seq.notes", "seq.tempo", "seq.values",
        "space.mirror", "space.polar", "space.scale", "space.tile", "space.transform", "space.translate", "space.warp",
    ];

    public static TheoryData<string> Patched => [.. PatchedIds()];

    [Theory]
    [MemberData(nameof(Patched))]
    public void Every_module_is_named_by_a_feature(string typeId)
    {
        var module = ShippedPlugins.Loaded.Modules.All.Single(d => d.TypeId == typeId);
        var named = Features.Contains(typeId, StringComparison.Ordinal)
            || Regex.IsMatch(Features, $@"(?<![\w-]){Regex.Escape(module.Name)}(?![\w-])");

        if (Unwritten.Contains(typeId))
            named.ShouldBeFalse($"A feature names the {module.Name} now: take {typeId} off {nameof(Unwritten)}.");
        else
            named.ShouldBeTrue($"No feature names the {module.Name} ({typeId}): state what it does in a scenario in tests/Flyback.Specs.");
    }

    [Fact]
    public void Every_module_left_unwritten_is_still_shipped() =>
        Unwritten.Except(PatchedIds()).ShouldBeEmpty();

    /// <summary>Every module but the ones an Expression stands for, and the Sample plugin's, which only the tests load.</summary>
    private static IEnumerable<string> PatchedIds() =>
        ShippedPlugins.Loaded.Modules.All
            .Where(d => !ExpressionFusion.Retired(d) && ShippedPlugins.Loaded.Modules.ProviderOf(d.TypeId)?.Id != "flyback.sample")
            .Select(d => d.TypeId);
}
