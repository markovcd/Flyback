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

    /// <summary>The knob module. Named here for the reason every other own mark is.</summary>
    public const string ValueTypeId = "value";

    public const int CoordXPort = 0;
    public const int CoordYPort = 1;
    public const int CoordAspectPort = 4;

    private static IEnumerable<NodeDef> Sources()
    {
        yield return new NodeDef(
            CoordTypeId, "Coordinates", ModuleCategories.Sources,
            [],
            [
                Num("x") with { Help = "Normalized, and widened by the aspect ratio." },
                Num("y", 0f, -1f, 1f) with { Help = "Normalized." },
                Num("radius") with { Help = SocketHelp.Radius },
                Num("angle", 0f, -MathF.PI, MathF.PI) with { Help = "Around the center, in radians." },
                Num("aspect") with
                {
                    Help = "How far x reaches either side, the same everywhere on the frame. "
                        + "Multiply a -1..1 signal by it to cross the whole width.",
                },
            ],
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
                    em.Load(OpCode.LoadAspect),
                ];
            },
            "Screen position, as x and y, or as 'radius' and 'angle' when a module needs polar "
            + "coordinates.");

        // No rate knob, and that is the decision rather than an omission — see
        // ADR-0048. It was a second, hidden speed control: a Time at 0.2 feeding
        // an oscillator divides its pitch by five while the freq knob goes on
        // saying otherwise, and nothing about the patch shows where the fifth
        // went. Multiply is how you scale a signal here, as it is for every
        // other signal in the catalog.
        yield return new NodeDef(
            TimeTypeId, "Time", ModuleCategories.Sources,
            [], [Num("t") with { Help = "Seconds since the patch started." }],
            (em, _) => [em.Load(OpCode.LoadT)],
            "The clock, for motion or time-varying signals. Scale it with Multiply when you want "
            + "a slower rhythm.");

        yield return new NodeDef(
            SampleTypeId, "Sample", ModuleCategories.Sources,
            [
                Domain("in", "The playback position, in seconds. Time without a wire, so it plays from the start."),
                Num("level", 1f, 0f, 2f) with { Help = "Multiplies the sound." },
                Num("trigger", 0f, 0f, 1f) with { Lenient = true, Help = "Restarts from zero as it rises." },
            ],
            [Num("out") with { Help = "The sound, times 'level'. Silent while no file is loaded." }, Num("length") with { Help = "The clip's length in seconds, for loop timing or scrubbing." }],
            EmitSample,
            "Plays a WAV file. The file path is stored with the patch, so moving or renaming it "
            + "will break playback.")
        {
            Extras = [new SampleExtra()],
            Sinks = ModuleSinks.Audio,
        };

        yield return new NodeDef(
            PictureTypeId, "Image", ModuleCategories.Sources,
            [..Position()],
            [Col("color") with { Help = "The image's color where 'x' and 'y' read it." }],
            EmitPicture,
            "Loads an image file, black outside the image. Scale, translate, rotate, and warp "
            + "control how it is mapped.")
        {
            Extras = [new PictureExtra()],
            Sinks = ModuleSinks.Video,
        };

        yield return new NodeDef(
            ValueTypeId, "Value", ModuleCategories.Sources,
            [Num("value", 0.5f) with { Help = "The number it holds." }], [Num("out") with { Help = "The number, for as many sockets as want it." }],
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