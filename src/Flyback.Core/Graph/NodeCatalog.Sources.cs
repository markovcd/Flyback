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

    /// <summary>
    /// The picture module. Named here for the reason the sample player is: it is
    /// the one module whose instance carries a picture, and the editor, the
    /// compiler and the assistant all have to ask whether a given node is it —
    /// see <see cref="NodeInstance.Picture"/>.
    /// </summary>
    public const string PictureTypeId = "picture";

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
            [Domain("in"), Num("level", 1f, 0f, 2f), Num("trigger", 0f, 0f, 1f)],
            [Num("out"), Num("length")],
            EmitSample,
            "Plays a sound file. 'in' is how far into it to read, in seconds — so with nothing "
            + "patched it runs on the clock and plays once, at the speed it was recorded, and "
            + "then stops. Off either end is silence, which is how a one-shot ends. "
            + "'trigger' restarts it: on the way up it takes the position as zero, so the clip "
            + "plays from its beginning at its own pitch and tempo, and a trigger arriving "
            + "while it is still sounding cuts that short and starts again. Patch the same "
            + "gate that opens an envelope. Left alone it does nothing at all, so a player "
            + "with nothing patched here is what it always was. "
            + "Everything else is what you drive 'in' with rather than a knob this carries: a "
            + "Saw times 'length' loops it, a negative slope plays it backwards, Time times two "
            + "is double speed an octave up, and an envelope into 'in' scrubs. 'length' is how "
            + "long the file is in seconds, so a patch can loop or scale without being told. "
            + "Mono, 8 to 32 bit WAV. The patch stores the path rather than the audio, so the "
            + "file has to stay where it is — one that has moved is reported by name. A Probe "
            + "charts it, but not the triggering: a trigger is something that happened before "
            + "now and the screen has no before, so what is drawn is the clip read at 'in' with "
            + "the trigger ignored. Rewind to see the start of it. A Scope does show the "
            + "triggering, because it charts what the speakers played rather than working the "
            + "signal out again.")
        {
            Extras = [new SampleExtra()],
        };

        yield return new NodeDef(
            PictureTypeId, "Image", "Source",
            [..Position()],
            [Col("color")],
            EmitPicture,
            "Shows a picture from a file. The one module that brings something into a patch "
            + "from outside rather than working it out — everything else here is arithmetic, "
            + "and this is a photograph. It is placed at its own shape, filling the height and "
            + "as much of the width as it is wide, with black beyond its edges: a frame this "
            + "program exported lands exactly where it came from. 'x' and 'y' are where it is "
            + "being asked about, so a Scale before it zooms, a Translate slides it, a Rotate "
            + "turns it and a Warp bends it — the module decides nothing about the mapping "
            + "except what it is when nothing is patched in. PNG, 8 or 16 bit, not interlaced; "
            + "transparency is taken as black, since a colour here is three numbers and not "
            + "four. The patch stores the path rather than the picture, so the file has to stay "
            + "where it is — one that has moved is reported by name. Silent: the speakers have "
            + "nothing to do with a picture, so on that path it is black and the file is not "
            + "even opened.")
        {
            Extras = [new PictureExtra()],
        };

        yield return new NodeDef(
            "value", "Value", "Source",
            [Num("value", 0.5f)], [Num("out")],
            (_, i) => [i[0]],
            "A knob. Handy when several modules should share one number.");
    }

    /// <summary>
    /// A picture read at a place, and black where there is no picture to read.
    /// </summary>
    /// <remarks>
    /// Three lines, because everything that makes this module hard happens
    /// elsewhere: the file is read by a library outside the compiler, placed by
    /// <see cref="LoadedImage.At"/>, and lowered to a texture by the shader
    /// backend. What is left here is the one decision the module itself makes,
    /// which is what to do when it has nothing — black, for the reason the
    /// player answers silence, and the same black that lies outside a picture's
    /// own edges. So a patch showing a file that has gone draws what a patch
    /// showing a file that is entirely off the side of the frame draws, and
    /// there is one rule rather than two.
    /// </remarks>
    private static Slot[] EmitPicture(Emitter em, EmitContext node) =>
        node.Picture is { } picture
            ? [em.Picture(node[0], node[1], picture)]
            : [em.Coerce(em.Constant(0f), VideoChannels)];

    /// <summary>
    /// A clip read at a position, with a trigger that takes the position it
    /// arrives at as the start of the clip.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The trigger does not run a playhead of its own. It remembers where 'in'
    /// had got to when the last edge came, and what is read is the difference —
    /// so a clip driven by the clock plays at its own speed from the moment it
    /// was fired, and one driven by anything else is re-zeroed against that
    /// instead. Retriggering falls out rather than being handled: an edge
    /// arriving mid-clip moves the zero to now, and the position is nought
    /// again on the very evaluation it lands.
    /// </para>
    /// <para>
    /// An edge and not a level, which is the opposite of the Quantiser's 'hold'
    /// and right for the opposite reason. "Hold this note" is an interval and
    /// says when the note may not move; "start again" is an instant. So a
    /// trigger of any width works, down to a single evaluation.
    /// </para>
    /// <para>
    /// The socket rests low, and that is what lets it be optional: a knob at
    /// nought never rises, so the zero stays where the cell began and the
    /// position is <c>in</c> itself — which is exactly what this module did
    /// before the socket existed. That mattered more than it looks. Resting it
    /// high was tried first and takes the zero from wherever <c>in</c> happened
    /// to be on the first evaluation, which is nought for a clock, a saw or a
    /// sine and is <em>not</em> nought for a clip being played backwards. A
    /// default that quietly broke reverse is not a default.
    /// </para>
    /// <para>
    /// The cost of resting low is that a player with a trigger wired in still
    /// plays once as the patch begins, before any edge has arrived: until then
    /// it is a player with no trigger, and that is what one of those does.
    /// Telling the two apart would mean knowing whether the socket is patched,
    /// which nothing here can ask — the same wall the Quantiser's 'hold' met and
    /// answered by being a level. An edge cannot answer it that way, so this
    /// carries the wrinkle instead. A gate on the output from the same trigger
    /// is the patch-level fix, and a drum wants one anyway.
    /// </para>
    /// <para>
    /// Two cells. Where the clip is being read from is written as a clock rather
    /// than as a signal — see <see cref="Emitter.ClockWrite"/> — because it is a
    /// reading of a domain and the rails a signal is held to would stop it
    /// sixteen seconds into a session. The other is the trigger as it was, which
    /// is what makes an edge an edge.
    /// </para>
    /// </remarks>
    private static Slot[] EmitSample(Emitter em, EmitContext node)
    {
        // No clip is silence, and 'length' is nothing to divide a patch by.
        // Three things arrive here the same way — no file chosen, a file that
        // has gone, and the screen, which cannot play one — and silence is the
        // right answer to each. The complaint about the first two has already
        // been made by the compiler.
        if (node.Sample is not { } clip) return [em.Constant(0f), em.Constant(0f)];

        var one = em.Constant(1f);
        var position = node[0];

        var startCell = em.AllocateUnitSlot();
        var edgeCell = em.AllocateUnitSlot();

        var up = em.Binary(OpCode.Step, em.Constant(GateOpen), node[2]);
        var rise = em.Mul(up, em.Sub(one, em.UnitRead(edgeCell)));

        // Where the clip is being read from: moved to here on an edge, held
        // between them, and nought where no edge has ever come. Taken on the
        // evaluation the edge lands rather than the one after, so a trigger and
        // the sound it starts are the same moment.
        //
        // And nought again wherever there is no memory, which is the screen —
        // see Emitter.HasMemory. A trigger is a thing that happened before now,
        // so on a path with no before it cannot mean anything, and the honest
        // answer there is the module without one: the clip read at 'in'. Left
        // out, the cell behind the edge reads nought at every pixel, every
        // evaluation with the trigger up looks like a rising edge, and the chart
        // fills with the first sample of the clip — which is neither what the
        // speakers do nor what a memoryless reading of the patch is.
        var start = em.Mul(
            em.Ternary(OpCode.Mix, em.UnitRead(startCell), position, rise),
            em.HasMemory());

        em.ClockWrite(startCell, start);
        em.UnitWrite(edgeCell, up);

        return
        [
            em.Mul(em.Table(em.Sub(position, start), clip), node[1]),
            em.Constant(clip.Seconds),
        ];
    }
}