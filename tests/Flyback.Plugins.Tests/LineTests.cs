using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The Line form, read point by point as the renderer reads it.
/// </summary>
public class LineTests
{
    private const string LineType = "flyback.picture.line";

    private const int X1 = 2;
    private const int Y1 = 3;
    private const int X2 = 4;
    private const int Y2 = 5;
    private const int Width = 6;

    private const int Distance = 0;
    private const int Along = 1;

    private static readonly ModuleCatalog Catalog = PluginHost.Load().Modules;

    /// <summary>From (-0.5, 0) to (0.5, 0), with no width, unless a test says otherwise.</summary>
    private static readonly (int Port, float Value)[] Level =
        [(X1, -0.5f), (Y1, 0f), (X2, 0.5f), (Y2, 0f), (Width, 0f)];

    [Fact]
    public void The_picture_plugin_offers_it_beside_the_forms()
    {
        var def = Catalog.Get(LineType).ShouldNotBeNull();

        def.Name.ShouldBe("Line");
        def.Category.ShouldBe(ModuleCategories.Forms);
        def.Outputs.Select(p => p.Name).ShouldBe(["distance", "along"]);
        Catalog.ProviderOf(LineType)!.Id.ShouldBe("flyback.picture");
        Catalog.All.Count(d => d.TypeId.Split('.')[^1] == "line").ShouldBe(1);

        Catalog.Normalled(def.Inputs[0]).ShouldBe("Coordinates x");
        Catalog.Normalled(def.Inputs[1]).ShouldBe("Coordinates y");
    }

    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(0.3f, 0f, 0f)]
    [InlineData(0.2f, 0.25f, 0.25f)]
    [InlineData(-0.1f, -0.4f, 0.4f)]
    [InlineData(0.8f, 0f, 0.3f)]
    [InlineData(-0.5f, 0.3f, 0.3f)]
    [InlineData(0.8f, 0.4f, 0.5f)]
    public void The_distance_is_to_the_nearest_point_of_the_segment(float x, float y, float expected)
    {
        Read(Distance, x, y, Level).ShouldBe(expected, 1e-5f);
    }

    [Fact]
    public void Width_grows_the_stroke_either_side()
    {
        var wide = Level.Append((Width, 0.1f)).ToArray();

        Read(Distance, 0f, 0f, wide).ShouldBe(-0.1f, 1e-5f);
        Read(Distance, 0f, 0.1f, wide).ShouldBe(0f, 1e-5f);
        Read(Distance, 0.6f, 0f, wide).ShouldBe(0f, 1e-5f);
    }

    [Theory]
    [InlineData(-0.5f, 0f)]
    [InlineData(0f, 0.5f)]
    [InlineData(0.25f, 0.75f)]
    [InlineData(-2f, 0f)]
    [InlineData(2f, 1f)]
    public void Along_runs_from_the_first_end_to_the_second(float x, float expected)
    {
        Read(Along, x, 0.3f, Level).ShouldBe(expected, 1e-5f);
    }

    [Fact]
    public void A_diagonal_measures_square_to_itself()
    {
        (int, float)[] diagonal = [(X1, 0f), (Y1, 0f), (X2, 1f), (Y2, 1f), (Width, 0f)];

        Read(Distance, 0f, 1f, diagonal).ShouldBe(MathF.Sqrt(0.5f), 1e-5f);
        Read(Along, 0f, 1f, diagonal).ShouldBe(0.5f, 1e-5f);
    }

    [Fact]
    public void A_line_of_no_length_is_a_dot_at_its_end()
    {
        (int, float)[] dot = [(X1, 0.2f), (Y1, 0.1f), (X2, 0.2f), (Y2, 0.1f), (Width, 0.05f)];

        Read(Distance, 0.5f, 0.5f, dot).ShouldBe(0.5f - 0.05f, 1e-5f);
        Read(Along, 0.5f, 0.5f, dot).ShouldBe(0f);
    }

    [Fact]
    public void It_survives_to_the_shader()
    {
        foreach (var port in new[] { Distance, Along })
        foreach (var dialect in Enum.GetValues<GlslDialect>())
            GlslEmitter.Emit(Compiled(port, Level), dialect).PatchFragment.ShouldNotBeNullOrEmpty();
    }

    // --- harness -----------------------------------------------------------------

    private static float Read(int port, float x, float y, (int Port, float Value)[] knobs)
    {
        var program = Compiled(port, knobs);
        var bank = program.AllocateRegisters();

        program.Evaluate(x, y, 0d, bank, default);
        return (float)bank[program.OutputBase];
    }

    private static CompiledPatch Compiled(int port, (int Port, float Value)[] knobs)
    {
        var patch = new Patch();

        var line = NodeInstance.Create(Catalog.Require(LineType), 0, 0);
        foreach (var (at, value) in knobs) line.InputValues[at] = value;

        var screen = NodeInstance.Create(Catalog.Require(NodeCatalog.OutputTypeId), 0, 0);

        patch.Nodes.Add(line);
        patch.Nodes.Add(screen);
        patch.Connect(line.Id, port, screen.Id, NodeCatalog.OutputColorPort);

        return patch.CompileForVideo(Catalog).Program;
    }
}
