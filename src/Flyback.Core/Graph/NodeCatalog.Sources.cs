using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    /// <summary>
    /// The clock. Named here because sockets are normalled to it rather than
    /// merely wired to it, and a type id that has to match one written in
    /// another file is worth writing once.
    /// </summary>
    public const string TimeTypeId = "time";

    /// <summary>Where on the screen you are, for the same reason.</summary>
    public const string CoordTypeId = "coord";

    /// <summary>
    /// The sample player. Named here because it is the one module whose instance
    /// carries a file, and the editor, the compiler and the assistant all have to
    /// ask whether a given node is it — see <see cref="NodeInstance.Sample"/>.
    /// </summary>
    public const string SampleTypeId = "audio.sample";

    public const int CoordXPort = 0;
    public const int CoordYPort = 1;

    private static IEnumerable<NodeDef> Sources()
    {
        yield return new NodeDef(
            CoordTypeId, "Coordinates", "Source",
            [], [Num("x"), Num("y"), Num("radius"), Num("angle")],
            (em, _) =>
            {
                var x = em.Load(OpCode.LoadX);
                var y = em.Load(OpCode.LoadY);
                return
                [
                    x,
                    y,
                    em.Binary(OpCode.Hypot, x, y),
                    em.Binary(OpCode.Atan2, y, x),
                ];
            },
            "Where you are on screen. y runs -1..1, x is widened by the aspect ratio. Every "
            + "'x' and 'y' socket is already reading this without a wire, so reach for one of "
            + "these when you want 'radius' or 'angle', or when the position going into a "
            + "module should be something other than the pixel's own.");

        // No rate knob, and that is the decision rather than an omission — see
        // ADR-0048. It was a second, hidden speed control: a Time at 0.2 feeding
        // an oscillator divides its pitch by five while the freq knob goes on
        // saying otherwise, and nothing about the patch shows where the fifth
        // went. Multiply is how you scale a signal here, as it is for every
        // other signal in the catalogue.
        yield return new NodeDef(
            TimeTypeId, "Time", "Source",
            [], [Num("t")],
            (em, _) => [em.Load(OpCode.LoadT)],
            "Seconds since the patch started. Every 'in' socket is already reading this "
            + "without a wire, so you need one of these only where a socket that is not an "
            + "'in' should move — Noise's 'z' to make it boil, or an angle to make it turn. "
            + "To run something slower, put a Multiply after this — 0.2 for a fifth of the "
            + "speed. Into an oscillator's 'in' it needs no scaling at all: that is what the "
            + "oscillator's 'freq' is.");

        yield return new NodeDef(
            SampleTypeId, "Sample", "Source",
            [Domain("in"), Num("level", 1f, 0f, 2f)],
            [Num("out"), Num("length")],
            (em, node) =>
            {
                // No clip is silence, and 'length' is nothing to divide a patch
                // by. Three things arrive here the same way — no file chosen, a
                // file that has gone, and the screen, which cannot play one —
                // and silence is the right answer to each. The complaint about
                // the first two has already been made by the compiler.
                if (node.Sample is not { } clip) return [em.Constant(0f), em.Constant(0f)];

                return
                [
                    em.Mul(em.Table(node[0], clip), node[1]),
                    em.Constant(clip.Seconds),
                ];
            },
            "Plays a sound file. 'in' is how far into it to read, in seconds — so with nothing "
            + "patched it runs on the clock and plays once, at the speed it was recorded, and "
            + "then stops. Everything else is what you drive it with rather than a knob it "
            + "carries: a Saw times 'length' loops it, a negative slope plays it backwards, "
            + "Time times two is double speed an octave up, and an envelope into 'in' scrubs. "
            + "Off either end is silence, which is how a one-shot ends. 'length' is how long "
            + "the file is in seconds, so a patch can loop or scale without being told. Mono, "
            + "16 to 32 bit WAV. The patch stores the path rather than the audio, so the file "
            + "has to stay where it is — one that has moved is reported by name. Audio only: a "
            + "picture is drawn all at once and a recording is a thing that happens over time, "
            + "so on the screen this is silence.")
        {
            TakesSample = true,
        };

        yield return new NodeDef(
            "value", "Value", "Source",
            [Num("value", 0.5f)], [Num("out")],
            (_, i) => [i[0]],
            "A knob. Handy when several modules should share one number.");
    }
}