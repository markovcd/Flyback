using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Language;
using Flyback.Engine.Packs;
using Flyback.Tests;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Effects;

/// <summary>The Effects pack, held to the Effects plugin it is written from: the same sockets, and the same sound and picture, sample for sample.</summary>
/// <remarks>
/// Not the same ops: the plugin computes every output whether wired or not, and a pack's Sine
/// multiplies by its 'amp' and adds its 'bias', so the registers and a few identities differ.
/// </remarks>
public class EffectsPackTests
{
    private const int Rate = GlobalConstants.SampleRate;

    private static readonly ModuleCatalog Plugin = ShippedPlugins.Loaded.Modules;

    private static readonly ModuleCatalog Pack = Packed();

    private static ModuleCatalog Packed()
    {
        var (catalog, issues) = PackModules.Into(NodeCatalog.BuiltIn, File.ReadAllText(Repository.Path("src", "packs", "flyback.effects.fbks")));

        issues.ShouldBeEmpty(string.Join("; ", issues.Select(i => $"line {i.Line}: {i.Message}")));

        return catalog;
    }

    [Theory]
    [InlineData("flyback.effects.chorus")]
    [InlineData("flyback.effects.flanger")]
    [InlineData("flyback.effects.phaser")]
    [InlineData("flyback.effects.echo")]
    public void It_offers_the_plugins_module_by_the_same_id_with_the_same_sockets(string typeId)
    {
        var pack = Pack.Get(typeId).ShouldNotBeNull();
        var plugin = Plugin.Get(typeId).ShouldNotBeNull();

        pack.Name.ShouldBe(plugin.Name);
        pack.Category.ShouldBe(plugin.Category);
        pack.Inputs.Select(p => (p.Name, p.Default)).ShouldBe(plugin.Inputs.Select(p => (p.Name, p.Default)));
        pack.Outputs.Select(p => (p.Name, p.Help)).ShouldBe(plugin.Outputs.Select(p => (p.Name, p.Help)));
    }

    public static TheoryData<string> Patches =>
    [
        "flyback.effects.chorus(in: x, rate: 1.3, depth: 0.9, mix: 0.7) |> out.left",
        "flyback.effects.chorus(in: x).wide |> out.left",
        "flyback.effects.chorus(in: x, rate: 3).lfo |> out.left",
        "flyback.effects.flanger(in: x, rate: 2, depth: 0.6, feedback: -0.7, mix: 0.8) |> out.left",
        "flyback.effects.phaser(in: x, rate: 2, depth: 0.9, feedback: 0.8, mix: 0.9) |> out.left",
        "flyback.effects.echo(in: x, tempo: 3, left: 2, right: 1.5, feedback: 0.6, mix: 0.5).right |> out.left",
        "flyback.effects.echo(in: x, tempo: 3, taps: \"side\", division: 3).right |> out.left",
    ];

    [Theory]
    [MemberData(nameof(Patches))]
    public void It_plays_what_the_plugin_plays(string text)
    {
        var heard = Heard(text, Pack);
        var expected = Heard(text, Plugin);

        for (var i = 0; i < heard.Length; i++) heard[i].ShouldBe(expected[i], $"sample {i}");
    }

    /// <summary>On the picture the effects are a wire and only 'lfo' moves, in the pack as in the plugin.</summary>
    [Theory]
    [MemberData(nameof(Patches))]
    public void It_draws_what_the_plugin_draws(string text)
    {
        var drawn = Drawn(text.Replace("out.left", "out.color"), Pack);
        var expected = Drawn(text.Replace("out.left", "out.color"), Plugin);

        drawn.ShouldBe(expected);
    }

    private static float[] Drawn(string text, ModuleCatalog catalog)
    {
        var program = Built(text, catalog).CompileForVideo(catalog).Program;
        var registers = program.AllocateRegisters();

        return [.. Enumerable.Range(0, 64).Select(i =>
        {
            program.Evaluate(i / 32f - 1f, 0f, i * 0.37, registers, default);
            return (float)registers[program.OutputBase];
        })];
    }

    private static Patch Built(string text, ModuleCatalog catalog)
    {
        var load = PatchLanguage.Build(text + "\nout.volume = 1\n", catalog);

        load.Issues.Where(i => i.IsError).ShouldBeEmpty(load.Report);

        return load.Patch;
    }

    private static float[] Heard(string text, ModuleCatalog catalog)
    {
        var program = Built(text, catalog).CompileForAudio(catalog).Program;
        var state = new DelayState(program, Rate);
        var registers = program.AllocateRegisters();
        var random = new Random(7);
        var output = new float[Rate / 2];

        for (var i = 0; i < output.Length; i++)
        {
            program.Evaluate(random.NextSingle() * 2 - 1, 0f, i / (double)Rate, registers, default, state);
            output[i] = (float)registers[program.OutputBase];
        }

        return output;
    }
}
