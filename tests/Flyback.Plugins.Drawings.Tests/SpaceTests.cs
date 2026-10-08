using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Drawings.Tests;

/// <summary>Rotate 3D, Translate 3D, Scale 3D and Perspective, each checked against the sum it stands for.</summary>
public class SpaceTests
{
    private static readonly ModuleCatalog Modules = PluginHost.LoadTypes(typeof(DrawingsPlugin)).Modules;

    private const float Quarter = MathF.PI / 2;

    /// <summary>One output of a module set by its knobs alone.</summary>
    private static double Out(string typeId, int output, params (int Port, float Value)[] knobs)
    {
        var b = new PatchBuilder(Modules);
        var screen = b.Add(NodeCatalog.OutputTypeId);
        var module = b.Add(typeId, knobs);

        b.Wire(module, output, screen, NodeCatalog.OutputColorPort);

        var program = b.Patch.CompileForVideo(Modules).Program;
        var registers = program.AllocateRegisters();
        program.Evaluate(0d, 0d, 0d, registers, default);

        return registers[program.OutputBase];
    }

    private static (double X, double Y, double Z) Point(string typeId, params (int Port, float Value)[] knobs) =>
        (Out(typeId, 0, knobs), Out(typeId, 1, knobs), Out(typeId, 2, knobs));

    private static void ShouldBeAt((double X, double Y, double Z) actual, double x, double y, double z)
    {
        actual.X.ShouldBe(x, 1e-5);
        actual.Y.ShouldBe(y, 1e-5);
        actual.Z.ShouldBe(z, 1e-5);
    }

    [Fact]
    public void A_quarter_turn_of_yaw_carries_the_right_side_to_the_back()
    {
        ShouldBeAt(Point(RotateModule.TypeId, (0, 1f), (RotateModule.YawPort, Quarter)), 0, 0, -1);
    }

    [Fact]
    public void A_quarter_turn_of_pitch_tips_the_top_toward_the_viewer()
    {
        ShouldBeAt(Point(RotateModule.TypeId, (1, 1f), (RotateModule.PitchPort, Quarter)), 0, 0, 1);
    }

    [Fact]
    public void A_quarter_turn_of_roll_turns_right_into_up_as_Rotate_does()
    {
        ShouldBeAt(Point(RotateModule.TypeId, (0, 1f), (RotateModule.RollPort, Quarter)), 0, 1, 0);
    }

    [Fact]
    public void Turning_keeps_a_point_as_far_from_the_center()
    {
        var (x, y, z) = Point(RotateModule.TypeId,
            (0, 0.3f), (1, -0.5f), (2, 0.7f),
            (RotateModule.YawPort, 0.9f), (RotateModule.PitchPort, -2.1f), (RotateModule.RollPort, 4f));

        Math.Sqrt(x * x + y * y + z * z).ShouldBe(Math.Sqrt(0.3 * 0.3 + 0.5 * 0.5 + 0.7 * 0.7), 1e-5);
    }

    [Fact]
    public void Translate_adds_its_offsets()
    {
        ShouldBeAt(Point(TranslateModule.TypeId,
            (0, 0.1f), (1, 0.2f), (2, 0.3f),
            (TranslateModule.DxPort, 0.5f), (TranslateModule.DyPort, -0.5f), (TranslateModule.DzPort, 1f)),
            0.6, -0.3, 1.3);
    }

    [Fact]
    public void Scale_multiplies_by_the_whole_and_by_each_axis()
    {
        ShouldBeAt(Point(ScaleModule.TypeId,
            (0, 0.5f), (1, 0.5f), (2, 0.5f),
            (ScaleModule.ScalePort, 2f), (ScaleModule.SxPort, -1f), (ScaleModule.SzPort, 0.5f)),
            -1, 1, 0.5);
    }

    [Fact]
    public void Perspective_leaves_depth_nought_alone_and_enlarges_what_is_nearer()
    {
        ShouldBeAt(Point(PerspectiveModule.TypeId, (0, 0.5f), (1, -0.5f)), 0.5, -0.5, 0);

        // A third of the way to a camera three away: one and a half times the size.
        ShouldBeAt(Point(PerspectiveModule.TypeId, (0, 0.5f), (1, -0.5f), (2, 1f)), 0.75, -0.75, 1);
    }

    [Fact]
    public void A_point_behind_the_camera_is_held_in_front_of_it()
    {
        var (x, _, _) = Point(PerspectiveModule.TypeId, (0, 0.5f), (2, 5f));

        double.IsFinite(x).ShouldBeTrue();
        x.ShouldBe(0.5 / 0.05, 1e-3);
    }
}
