using System.Text.Json.Nodes;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The Layer module: two RGB swatches blended in each mode and read off the screen.
/// </summary>
public class LayerTests
{
    private const string LayerType = "flyback.picture.layer";
    private const int Amount = 2;

    private static readonly (float R, float G, float B) Base = (0.2f, 0.5f, 0.8f);
    private static readonly (float R, float G, float B) Top = (0.6f, 0.4f, 0.1f);

    private static readonly ModuleCatalog Catalog = ShippedPlugins.Loaded.Modules;

    [Fact]
    public void The_picture_plugin_offers_it_beside_the_colors()
    {
        var def = Catalog.Get(LayerType).ShouldNotBeNull();

        def.Name.ShouldBe("Layer");
        def.Category.ShouldBe(ModuleCategories.Color);
        Catalog.ProviderOf(LayerType)!.Id.ShouldBe("flyback.picture");
        Catalog.All.Count(d => d.TypeId.Split('.')[^1] == "layer").ShouldBe(1);
    }

    [Fact]
    public void A_fresh_layer_is_in_normal_mode()
    {
        var node = NodeInstance.Create(Catalog.Require(LayerType), 0, 0);

        node.StateOf("layer").ShouldBeOfType<JsonObject>()["mode"]!.GetValue<string>().ShouldBe("normal");
    }

    [Theory]
    [InlineData("normal")]
    [InlineData("add")]
    [InlineData("multiply")]
    [InlineData("screen")]
    [InlineData("overlay")]
    [InlineData("difference")]
    [InlineData("lighten")]
    [InlineData("darken")]
    public void Each_mode_blends_channel_by_channel(string mode)
    {
        var seen = Blend(mode, 1f);

        seen.R.ShouldBe(Expected(mode, Base.R, Top.R), 1e-5f);
        seen.G.ShouldBe(Expected(mode, Base.G, Top.G), 1e-5f);
        seen.B.ShouldBe(Expected(mode, Base.B, Top.B), 1e-5f);
    }

    [Fact]
    public void Amount_fades_between_the_base_and_the_blend()
    {
        Blend("multiply", 0f).ShouldBe(Base);

        var half = Blend("multiply", 0.5f);
        half.R.ShouldBe((Base.R + Base.R * Top.R) / 2f, 1e-5f);
    }

    [Fact]
    public void A_mode_this_build_does_not_know_is_normal()
    {
        Blend("hard-mix", 1f).ShouldBe(Top);
    }

    [Fact]
    public void Only_the_chosen_mode_is_compiled()
    {
        Ops("normal").ShouldBeLessThan(Ops("overlay"));
    }

    [Theory]
    [InlineData("normal")]
    [InlineData("overlay")]
    [InlineData("difference")]
    public void Every_mode_survives_to_the_shader(string mode)
    {
        foreach (var dialect in Enum.GetValues<GlslDialect>())
            GlslEmitter.Emit(Compiled(mode, 0.5f), dialect).PatchFragment.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void The_mode_is_written_and_read_back_by_the_language()
    {
        var built = PatchLanguage.Build("""rgb(0.2, 0.5, 0.8) |> layer(rgb(0.6, 0.4, 0.1), mode: "screen") |> out.color""", Catalog);
        built.Issues.ShouldBeEmpty();

        var layer = built.Patch.Nodes.Single(n => n.TypeId == LayerType);
        layer.StateOf("layer")!["mode"]!.GetValue<string>().ShouldBe("screen");

        var printed = PatchPrinter.Print(built.Patch, Catalog);
        printed.ShouldContain("mode: \"screen\"");

        var again = PatchLanguage.Build(printed, Catalog);
        again.Issues.ShouldBeEmpty();
        again.Patch.Nodes.Single(n => n.TypeId == LayerType).StateOf("layer")!["mode"]!.GetValue<string>().ShouldBe("screen");
    }

    // --- harness -----------------------------------------------------------------

    private static float Expected(string mode, float a, float b) => mode switch
    {
        "add" => a + b,
        "multiply" => a * b,
        "screen" => 1f - (1f - a) * (1f - b),
        "overlay" => a < 0.5f ? 2f * a * b : 1f - 2f * (1f - a) * (1f - b),
        "difference" => MathF.Abs(a - b),
        "lighten" => MathF.Max(a, b),
        "darken" => MathF.Min(a, b),
        _ => b,
    };

    private static (float R, float G, float B) Blend(string mode, float amount)
    {
        var program = Compiled(mode, amount);
        var bank = program.AllocateRegisters();

        program.Evaluate(0d, 0d, 0d, bank, default);

        return ((float)bank[program.OutputBase], (float)bank[program.OutputBase + 1], (float)bank[program.OutputBase + 2]);
    }

    private static int Ops(string mode) => Compiled(mode, 1f).Ops.Length;

    private static CompiledPatch Compiled(string mode, float amount)
    {
        var patch = new Patch();

        var under = Swatch(patch, Base);
        var over = Swatch(patch, Top);

        var layer = NodeInstance.Create(Catalog.Require(LayerType), 0, 0);
        layer.InputValues[Amount] = amount;
        layer.SetState("layer", new JsonObject { ["mode"] = mode });

        var screen = NodeInstance.Create(Catalog.Require(NodeCatalog.OutputTypeId), 0, 0);

        patch.Nodes.Add(layer);
        patch.Nodes.Add(screen);
        patch.Connect(under.Id, 0, layer.Id, 0);
        patch.Connect(over.Id, 0, layer.Id, 1);
        patch.Connect(layer.Id, 0, screen.Id, NodeCatalog.OutputColorPort);

        return patch.CompileForVideo(Catalog).Program;
    }

    private static NodeInstance Swatch(Patch patch, (float R, float G, float B) color)
    {
        var rgb = NodeInstance.Create(Catalog.Require("color.rgb"), 0, 0);
        rgb.InputValues[0] = color.R;
        rgb.InputValues[1] = color.G;
        rgb.InputValues[2] = color.B;

        patch.Nodes.Add(rgb);
        return rgb;
    }
}
