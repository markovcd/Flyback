using Flyback.Core.Graph;

namespace Flyback.Plugins.Easy;

/// <summary>
/// A beat from four Easy Drums and no timing wires: each is left on Auto, so each
/// plays the rhythm its sound is usually heard in, and all four share the one tempo.
/// </summary>
/// <remarks>
/// The hats are panned apart and lightly swung. The picture is rings pushed outward
/// by the kick, colored round the wheel by where they sit, and lit by the kick and
/// the snare.
/// </remarks>
internal static class FirstBeatPreset
{
    public const string Name = "First beat";

    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var kick = Drum(Kit.Kick, (DrumModule.DrivePort, 0.3f));
        var snare = Drum(Kit.Snare, (DrumModule.TonePort, 0.6f));
        var closed = Drum(Kit.ClosedHat, (DrumModule.SwingPort, 0.3f), (DrumModule.PanPort, -0.4f), (DrumModule.VelocityPort, 0.6f));
        var open = Drum(Kit.OpenHat, (DrumModule.SwingPort, 0.3f), (DrumModule.PanPort, 0.4f), (DrumModule.VelocityPort, 0.5f));

        var desk = b.Add(NodeCatalog.DeskTypeId, (14, 0.7f));

        var rings = b.Add("pattern.rings", (2, 3f));
        var color = b.Add("color.hsv", (1, 0.8f));
        var lit = b.Add("math.add");

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.5f));

        var channel = 0;
        foreach (var drum in (NodeInstance[])[kick, snare, closed, open])
        {
            b.Wire(drum, DrumModule.LeftPort, desk, channel * 3)
             .Wire(drum, DrumModule.RightPort, desk, channel * 3 + 1);
            channel++;
        }

        b.Wire(desk, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(desk, 1, output, NodeCatalog.OutputRightPort)

         .Wire(kick, DrumModule.EnvPort, rings, 3)
         .Wire(rings, 0, color, 0)
         .Wire(kick, DrumModule.EnvPort, lit, 0)
         .Wire(snare, DrumModule.EnvPort, lit, 1)
         .Wire(lit, 0, color, 2)
         .Wire(color, 0, output, NodeCatalog.OutputColorPort);

        return b.Build();

        NodeInstance Drum(string sound, params (int Port, float Value)[] knobs) =>
            DrumModule.Configure(b.Add(DrumModule.TypeId, knobs), (DrumModule.SoundKey, sound));
    }
}
