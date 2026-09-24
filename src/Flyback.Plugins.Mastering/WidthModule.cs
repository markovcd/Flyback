using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins;

namespace Flyback.Plugins.Mastering;

/// <summary>
/// Stereo width, by way of mid and side: what the two sides share, and how they
/// differ.
/// </summary>
/// <remarks>
/// Width scales the side, so nought is mono and two is twice as wide. 'mono
/// below' then takes a one-pole lowpass of the side back out of it, so the bass
/// is in the middle however wide the rest is. At nought hertz the lowpass never
/// moves and the side is untouched. On the picture it does nothing, since a
/// picture has no low notes.
/// </remarks>
internal static class WidthModule
{
    public const string TypeId = "flyback.mastering.width";

    private const int Left = 0;
    private const int Right = 1;
    private const int Width = 2;
    private const int MonoBelow = 3;

    public static NodeDef Definition { get; } = new(
        TypeId, "Width", ModuleCategories.Shaping,
        [
            new PortSpec("left", PatchOnly: true) { Help = Dsp.LeftIn },
            new PortSpec("right", NormalledFrom: Left, PatchOnly: true) { Help = SocketHelp.Right },
            new PortSpec("width", PortKind.Scalar, 1f, 0f, 2f) { Help = "Scales the side: 0 is mono, 2 twice as wide." },
            new PortSpec("mono below", PortKind.Scalar, 0f, 0f, 500f)
            {
                Knee = 20f,
                Help = "In hertz, and 0 is off. Centers the bass under it.",
            },
        ],
        [
            new PortSpec("left") { Help = "The left side, at the new width." },
            new PortSpec("right") { Help = "The right side, at the new width." },
            new PortSpec("mid") { Help = "The halved sum of the two sides." },
            new PortSpec("side") { Help = "Their halved difference, after 'width' and 'mono below'." },
        ],
        Emit,
        "Stereo width, by way of mid and side: what the two sides share, and how they differ.")
    {
        Skin = new ModuleSkin.Palette(CategoryAccents.Of(ModuleCategories.Shaping))
        {
            Glyph = "M6,12 A6,4 0 1 1 18,12 A6,4 0 1 1 6,12 "
                + "M6,12 L1,12 M1,12 L3.5,9.8 M1,12 L3.5,14.2 M18,12 L23,12 M23,12 L20.5,9.8 M23,12 L20.5,14.2",
        },
    };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var live = em.HasMemory();

        var mid = em.Mul(em.Add(node[Left], node[Right]), 0.5f);
        var side = em.Mul(em.Mul(em.Sub(node[Left], node[Right]), 0.5f), node[Width]);

        // A one-pole in the trapezoidal form the other filters here take, so the
        // top is left exactly as it was rather than shaved by a naive lag.
        var g = Dsp.Warp(em, em.Binary(OpCode.Max, node[MonoBelow], em.Constant(0f)));
        var share = em.Binary(OpCode.Div, g, em.Add(g, 1f));

        var cell = em.AllocateUnitSlot();
        var held = em.UnitRead(cell);
        var v = em.Mul(em.Sub(side, held), share);
        var lowpass = em.Add(v, held);

        em.UnitWrite(cell, em.Add(lowpass, v));

        var narrowed = em.Sub(side, em.Mul(lowpass, live));

        return [em.Add(mid, narrowed), em.Sub(mid, narrowed), mid, narrowed];
    }
}
