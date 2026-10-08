using System.Text.Json.Nodes;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;

namespace Flyback.Plugins.Drawings;

/// <summary>
/// A model played as oscilloscope music: a Path goes round a cube's edges
/// fifty-five times a second, a Rotate 3D turns it about the vertical and tips it
/// toward the viewer, and a Perspective spreads the near edges wider. What is heard
/// is what the Beam draws.
/// </summary>
internal static class WireframePreset
{
    /// <summary>The preset's name, and the folder of its resources.</summary>
    public const string Name = "Wireframe";

    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var clock = b.Add(NodeCatalog.TimeTypeId);
        var model = b.Add(PathModule.TypeId, (PathModule.FreqPort, 55f), (PathModule.AmpPort, 0.6f));
        var turned = b.Add(RotateModule.TypeId, (RotateModule.PitchPort, 0.4f));
        var seen = b.Add(PerspectiveModule.TypeId);

        new DrawingExtra().Point(model, "cube.obj");

        // Turned about the vertical at 0.7 radians a second.
        var turn = b.Add(NodeCatalog.ExpressionTypeId);
        turn.SetState(FormulaExtra.StateKey, new JsonObject { [FormulaExtra.FormulaField] = "a * 0.7" });

        b.Wire(clock, 0, turn, 0)
         .Wire(model, PathModule.XPort, turned, Space3d.XPort)
         .Wire(model, PathModule.YPort, turned, Space3d.YPort)
         .Wire(model, PathModule.ZPort, turned, Space3d.ZPort)
         .Wire(turn, 0, turned, RotateModule.YawPort)
         .Wire(turned, Space3d.XPort, seen, Space3d.XPort)
         .Wire(turned, Space3d.YPort, seen, Space3d.YPort)
         .Wire(turned, Space3d.ZPort, seen, Space3d.ZPort);

        // Thirty milliseconds of phosphor: about two trips round the cube.
        var screen = b.Add(NodeCatalog.BeamTypeId, (2, -1.5229f));

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.4f));

        b.Wire(seen, Space3d.XPort, screen, 0)
         .Wire(seen, Space3d.YPort, screen, 1)
         .Wire(seen, Space3d.XPort, output, NodeCatalog.OutputLeftPort)
         .Wire(seen, Space3d.YPort, output, NodeCatalog.OutputRightPort)
         .Wire(screen, 0, output, NodeCatalog.OutputColorPort);

        return b.Build();
    }
}
