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
    /// ask whether a given node is it — see <see cref="SampleExtra"/>.
    /// </summary>
    public const string SampleTypeId = "audio.sample";

    /// <summary>
    /// The picture module. Named here for the reason the sample player is: it is
    /// the one module whose instance carries a picture, and the editor, the
    /// compiler and the assistant all have to ask whether a given node is it —
    /// see <see cref="PictureExtra"/>.
    /// </summary>
    public const string PictureTypeId = "picture";

    public const int CoordXPort = 0;
    public const int CoordYPort = 1;

    private static IEnumerable<NodeDef> Sources()
    {
        yield return new NodeDef(
            CoordTypeId, "Coordinates", ModuleCategories.Sources,
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
            "Screen position. x and y are normalized; x is widened by the aspect ratio. "
            + "Use radius or angle when a module needs polar coordinates.");

        // No rate knob, and that is the decision rather than an omission — see
        // ADR-0048. It was a second, hidden speed control: a Time at 0.2 feeding
        // an oscillator divides its pitch by five while the freq knob goes on
        // saying otherwise, and nothing about the patch shows where the fifth
        // went. Multiply is how you scale a signal here, as it is for every
        // other signal in the catalogue.
        yield return new NodeDef(
            TimeTypeId, "Time", ModuleCategories.Sources,
            [], [Num("t")],
            (em, _) => [em.Load(OpCode.LoadT)],
            "Elapsed seconds since the patch started. Use it for motion or time-varying signals. "
            + "Scale it with Multiply when you want a slower rhythm.");

        yield return new NodeDef(
            SampleTypeId, "Sample", ModuleCategories.Sources,
            [Domain("in"), Num("level", 1f, 0f, 2f), Num("trigger", 0f, 0f, 1f)],
            [Num("out"), Num("length")],
            EmitSample,
            "Plays a WAV file. 'in' is playback position in seconds; 'trigger' restarts from zero. "
            + "Use 'length' for loop timing or scrubbing. The file path is stored with the patch, "
            + "so moving or renaming it will break playback.")
        {
            Extras = [new SampleExtra()],
            Sinks = ModuleSinks.Audio,
        };

        yield return new NodeDef(
            PictureTypeId, "Image", ModuleCategories.Sources,
            [..Position()],
            [Col("color")],
            EmitPicture,
            "Loads an image file. x and y sample the image at that position; outside the image, "
            + "the result is black. Scale, translate, rotate, and warp control how it is mapped.")
        {
            Extras = [new PictureExtra()],
            Sinks = ModuleSinks.Video,
        };

        yield return new NodeDef(
            "value", "Value", ModuleCategories.Sources,
            [Num("value", 0.5f)], [Num("out")],
            (_, i) => [i[0]],
            "A knob. Handy when several modules should share one number.");
    }

    /// <summary>
    /// A picture read at a place, and black where there is no picture to read.
    /// </summary>
    /// <remarks>
    /// Three lines, because everything hard about this module happens elsewhere:
    /// the file is read outside the compiler, placed by
    /// <see cref="LoadedImage.At"/>, and lowered to a texture by the shader
    /// backend. Black is the same black that lies outside a picture's own edges,
    /// so a file that has gone and a file entirely off the frame draw alike.
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
    /// The trigger runs no playhead of its own: it remembers where 'in' had got to
    /// at the last edge, and what is read is the difference — so retriggering
    /// falls out rather than being handled. An edge and not a level, the opposite
    /// of the Quantiser's 'hold' and right for the opposite reason: "start again"
    /// is an instant, so a trigger of any width works.
    /// <para>
    /// The socket rests low, which is what lets it be optional: a knob at nought
    /// never rises, so the position is <c>in</c> itself. Resting it high would
    /// take the zero from wherever <c>in</c> was on the first evaluation, which is
    /// not nought for a clip being played backwards. The cost is that a player
    /// with a trigger wired in still plays once as the patch begins; a gate on the
    /// output from the same trigger is the patch-level fix.
    /// </para>
    /// <para>
    /// Two cells: where the clip is being read from, written as a clock rather
    /// than a signal — see <see cref="Emitter.ClockWrite"/>, since a signal's
    /// rails would stop it sixteen seconds in — and the trigger as it was, which
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
        // between them, and nought where no edge has come. Taken on the evaluation
        // the edge lands, so a trigger and the sound it starts are one moment.
        //
        // And nought again wherever there is no memory, which is the screen — see
        // Emitter.HasMemory. Left out, every evaluation with the trigger up looks
        // like a rising edge and the chart fills with the clip's first sample.
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