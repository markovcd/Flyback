using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// Four lines of text that say what Text does, paged by the clock and typed out
/// as each one arrives.
/// </summary>
/// <remarks>
/// Both of the module's own sockets are driven by the one count. Its whole
/// number is the line, since Text floors what it is given; what is left over is
/// how far through that line's two seconds the picture is, stretched so the
/// typing finishes early and the line is held long enough to read.
/// <para>
/// Filled rather than outlined, and in one color, so the letters are the only
/// thing on the screen that moves.
/// </para>
/// <para>
/// There is no sound in it. What it teaches is where the words come from and
/// what chooses among them, and a sound would be a second thing to follow.
/// </para>
/// </remarks>
internal static class CaptionsPreset
{
    public const string Name = "Captions";

    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // Half a line a second, so each line has two.
        var clock = b.Add("time");
        var pace = b.Add("math.mul", (1, 0.5f));

        // Typed in the first five eighths of a line's time, then held.
        var through = b.Add("math.fract");
        var typing = b.Add("math.mul", (1, 1.6f));

        var text = TextModule.WithLines(
            b.Add(TextModule.TypeId, (2, 0.18f)),
            "This is Text.",
            "'line' picks one,",
            "'reveal' types it,",
            "and Fill draws it.");

        var ink = b.Add(FillModule.TypeId, (1, 0.006f));

        // A terminal's green, which is what a pixel font being typed looks like.
        var tint = b.Add("color.hsv", (0, 0.36f), (1, 0.55f));

        var output = b.Add(NodeCatalog.OutputTypeId);

        b.Wire(clock, 0, pace, 0)
         .Wire(pace, 0, text, TextModule.LinePort)
         .Wire(pace, 0, through, 0)
         .Wire(through, 0, typing, 0)
         .Wire(typing, 0, text, TextModule.RevealPort)
         .Wire(text, 0, ink, 0)
         .Wire(ink, 0, tint, 2)
         .Wire(tint, 0, output, NodeCatalog.OutputColorPort);

        return b.Build();
    }
}
