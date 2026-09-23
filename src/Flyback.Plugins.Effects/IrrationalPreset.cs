using Flyback.Core.Graph;

namespace Flyback.Plugins.Effects;

/// <summary>
/// Seven concentric orbits at seven speeds, drawing together over about four minutes
/// into an alignment that is never quite exact and then coming apart again. Each dot
/// strikes one degree of A minor as it comes round, into a long delay and a large room.
/// </summary>
/// <remarks>
/// Each orbit is one turn per four minutes faster than the one outside it, so every dot
/// would meet every other one on that four minutes exactly — but each speed is nudged by
/// a root of a prime, a fiftieth of a turn per cycle, so the meeting is only ever nearly.
/// The piece is the drawing-together: all six pairs close at once, the picture blooms,
/// and it spreads again a little differently from last time. In between it does what a
/// pendulum wave does, because it is one — the dots fall into a travelling wave, then
/// two clusters facing each other, then three, then four, and back.
/// <para>
/// A turn takes between eleven and sixteen seconds, and a bell rings for the whole of
/// one, so seven voices is a slow chord rather than a clatter.
/// </para>
/// </remarks>
internal sealed class IrrationalPreset : PresetBench
{
    public const string Name = "Irrational";

    private const string Voice = "flyback.voice";

    private const string Picture = "flyback.picture";

    private const string CircleType = "flyback.picture.circle";

    private const string FillType = "flyback.picture.fill";

    /// <summary>Turns a second for the outermost dot: one turn in sixteen seconds.</summary>
    private const float Base = 0.0625f;

    /// <summary>
    /// How long the drawing-together takes, in seconds. Each orbit is exactly one turn
    /// per <see cref="Cycle"/> faster than the one outside it, which is what makes every
    /// pair close at the same moment rather than at six moments spread through it.
    /// </summary>
    private const float Cycle = 240f;

    /// <summary>
    /// What keeps the alignment from ever being exact: each speed is over by this much of
    /// a turn per cycle, times a root of its prime. Small enough that the dots plainly
    /// draw together, and irrational enough that they never actually arrive.
    /// </summary>
    private const float Slip = 0.02f;

    private static readonly float[] Primes = [1f, 2f, 3f, 5f, 7f, 11f, 13f];

    /// <summary>
    /// A minor over three octaves, one degree a dot, rising as the orbits tighten. Wide
    /// enough that two neighbors sounding together are a ninth apart rather than a
    /// second, which is the difference between a chord and a clash. Written out rather
    /// than snapped to the scale, because nothing here ever leaves it.
    /// </summary>
    private static readonly float[] Degrees = [45f, 52f, 57f, 60f, 64f, 67f, 72f];

    private const float Widest = 0.86f;

    private const float Tightest = 0.3f;

    public static Patch Build(ModuleCatalog modules)
    {
        if (!modules.HasProvider(Voice))
            throw new InvalidOperationException($"it needs the Voice plugin ({Voice}), which is not installed.");

        if (!modules.HasProvider(Picture))
            throw new InvalidOperationException($"it needs the Picture plugin ({Picture}), which is not installed.");

        return new IrrationalPreset(modules).Assemble();
    }

    private IrrationalPreset(ModuleCatalog modules)
        : base(modules)
    {
    }

    /// <summary>How fast the <paramref name="k"/>th dot goes round, in turns a second.</summary>
    private static float Speed(int k) => Base + ((k + (Slip * MathF.Sqrt(Primes[k]))) / Cycle);

    /// <summary>The orbit it runs on: the slow low ones outside, the quick high ones in.</summary>
    private static float Orbit(int k) => Widest - ((Widest - Tightest) * k / (Primes.Length - 1f));

    private Patch Assemble()
    {
        var clock = b.Add(NodeCatalog.TimeTypeId);

        var turns = new NodeInstance[Primes.Length];
        var strokes = new NodeInstance[Primes.Length];

        for (var k = 0; k < Primes.Length; k++)
        {
            turns[k] = Times(clock, Speed(k));

            // A knob a dot, resting where the piece is written and reaching from stopped
            // to twice as fast. Turning one is turning the whole shape: the four minutes
            // is what the seven speeds are to each other, not a number anything holds.
            Follows(turns[k], 1, Panel($"Orbit {k + 1}", 0.5f), 0f, 2f * Speed(k));

            // One stroke a turn, so a bell rings for the whole eleven to sixteen seconds
            // of it; the quick ones fall away sooner, which keeps the top clear.
            strokes[k] = Stroke(turns[k], 1f, 2.1f + (0.45f * k));
        }

        Box("The seven speeds");

        // --- how near each pair of neighbors is ------------------------------

        // Nought when they are opposite and one when together, sharpened so that only a
        // real approach counts. Neighbors only: every pair would be twenty-one signals
        // for a picture that can show one thing at a time.
        var passes = new NodeInstance[Primes.Length - 1];

        for (var k = 0; k < passes.Length; k++)
        {
            passes[k] = Power(
                Formula("1 - abs(fract(a - b + 0.5) - 0.5) * 2", new Read(turns[k]), new Read(turns[k + 1])),
                6f);
        }

        // One number for how much passing there is anywhere, which the drone leans on.
        var passing = Times(passes.Aggregate(Sum), 1f / passes.Length);

        Box("The passes");

        // --- the voices ------------------------------------------------------

        var bells = new NodeInstance[Primes.Length];

        for (var k = 0; k < Primes.Length; k++)
        {
            // A struck tone with one overtone an octave or a twelfth above it, and barely
            // any of that: a bell's ratio of 2.76 and a full index is the clang, and what
            // is wanted here is the part of a bell that is left after the strike.
            bells[k] = Bell(
                b.Add("audio.note", (0, Degrees[k])),
                strokes[k],
                k < 4 ? 2f : 3f,
                0.42f - (0.04f * k));
        }

        // Every voice into one, since the room is what they are heard in and a room is
        // not a thing you have one of per instrument. The high ones are let through a
        // little louder: the room darkens its tail, and without this the top of the chord
        // is the first thing it swallows.
        var struck = bells
            .Select((bell, k) => Times(bell, 0.78f + (0.08f * k)))
            .Aggregate(Sum);

        // Half a second and five sixths of one, which are not a ratio of each other, so
        // the repeats interleave rather than doubling up.
        var echo = Echo(struck, 3f, 6f, 10f, 0.55f, 0.6f, sideBySide: true);

        // A hall: the tail is most of what is heard, and it darkens as it goes. One room
        // and not two, because its second output is the same tail smeared the other way,
        // which is the stereo — a second room would be a second building.
        var room = b.Add(NodeCatalog.ReverbTypeId, (1, 0.95f), (2, 0.88f), (3, 1f));

        b.Wire(Sum(echo, Through("math.mul", echo, EchoRight)), 0, room, 0);

        // Two fifths a long way down that lean up as the dots draw together, which is the
        // only thing in the piece that says the alignment is coming before it arrives.
        var breath = Wander(0.05f, 3f, 0.45f, 1f);
        var floor = Tone(Formula("55 * (1 + a * 0.008)", new Read(passing)), Times(breath, 0.34f));
        var fifth = Tone(Formula("82.5 * (1 + a * 0.008)", new Read(passing)), Times(breath, 0.16f));

        Box("Voices");

        // --- the desk --------------------------------------------------------

        var dry = b.Add(DeskType);
        var master = b.Add(DeskType, (DeskTrim, 0.22f));

        // A little of the strike itself, so the attacks are still in the room rather than
        // only behind it.
        Channel(dry, 1, 0.34f, struck);
        Channel(dry, 2, 0.22f, floor);
        Channel(dry, 3, 0.14f, fifth);
        Channel(dry, 4, 0.3f, echo, echo, 0, EchoRight);

        // The room's two outputs are its stereo, so one channel takes both.
        Channel(master, 1, 0.5f, room, room, 0, 1);

        var desk = Chained(dry, master);

        Box("Desk");

        // --- the picture -----------------------------------------------------

        var coord = b.Add(NodeCatalog.CoordTypeId);

        NodeInstance? picture = null;

        for (var k = 0; k < Primes.Length; k++) picture = Ink(picture, Ring(coord, Orbit(k)), 0.3f, 0.36f, 0.5f);

        // A dot on each orbit, warm at the outside where the slow low ones are and cold
        // in the middle, so a glance says which is which without counting.
        for (var k = 0; k < Primes.Length; k++)
        {
            var age = k / (Primes.Length - 1f);
            var shown = Product(strokes[k], Plus(Times(passing, 0.5f), 0.5f));

            picture = Ink(
                picture,
                Marker(coord, turns[k], Orbit(k), shown, 0.075f - (0.006f * k)),
                1f - (0.55f * age),
                0.6f + (0.05f * age),
                0.28f + (0.62f * age));
        }

        // Where two neighbors would meet if they ever did: between their orbits, at the
        // angle of the faster one, as bright as the pass is close.
        for (var k = 0; k < passes.Length; k++)
        {
            picture = Ink(
                picture,
                Marker(coord, turns[k + 1], (Orbit(k) + Orbit(k + 1)) / 2f, Times(passes[k], 0.8f), 0.05f),
                1f,
                0.95f,
                0.82f);
        }

        var glow = b.Add(TrailsType, (TrailsZoom, 1.003f), (TrailsPersist, 0.8f));

        b.Wire(picture!, 0, glow, 0);

        var framed = Vignette(glow, 0.85f, 1.55f, 0.5f);
        var output = b.Add(NodeCatalog.OutputTypeId, (3, 0.56f));

        b.Wire(framed, 0, output, 0)
         .Wire(desk, 0, output, 1)
         .Wire(desk, 1, output, 2);

        Box("Picture");

        return b.Patch;

        // The faint circle a dot runs on: there so its place can be read, and so the
        // screen keeps its shape between passes.
        NodeInstance Ring(NodeInstance space, float radius)
        {
            var ring = b.Add(CircleType, (2, radius));
            var drawn = b.Add(FillType, (1, 0.004f), (2, 0.0045f));

            b.Wire(space, 0, ring, 0).Wire(space, 1, ring, 1).Wire(ring, 0, drawn, 0);

            return Times(drawn, 0.14f, 1);
        }

        // One dot at its own angle on its own orbit, as bright as it is struck.
        NodeInstance Marker(NodeInstance space, NodeInstance turn, float radius, NodeInstance level, float size)
        {
            var at = b.Add(CircleType);
            var lit = b.Add(FillType, (1, 0.03f));

            b.Wire(Wired("math.sub", space, Formula($"cos(a * 6.283185307179586) * {radius}", new Read(turn))), 0, at, 0)
             .Wire(Wired("math.sub", space, Formula($"sin(a * 6.283185307179586) * {radius}", new Read(turn)), 1), 0, at, 1)
             .Wire(Span(level, 0f, 1f, size * 0.45f, size * 1.6f), 0, at, 2)
             .Wire(at, 0, lit, 0);

            return Product(lit, Span(level, 0f, 1f, 0.2f, 1.1f));
        }
    }
}
