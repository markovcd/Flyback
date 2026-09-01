namespace Flyback.Core.Compile;

/// <summary>
/// How often an op has to be run to draw a frame, which is decided by the
/// furthest of the three loads anything upstream of it reaches.
/// </summary>
public enum EvaluationStage
{
    /// <summary>
    /// Settled once for the whole picture: literals, the clock, the frame's
    /// shape and whatever is being played into it. Everything a
    /// <see cref="OpCode.Const"/>, <see cref="OpCode.LoadT"/>,
    /// <see cref="OpCode.LoadAspect"/> or <see cref="OpCode.LoadLive"/> feeds
    /// and that no coordinate reaches.
    /// </summary>
    Frame,

    /// <summary>Settled once per scanline: whatever <see cref="OpCode.LoadY"/> reaches and <see cref="OpCode.LoadX"/> does not.</summary>
    Row,

    /// <summary>What is left, and the only part a pixel actually has to pay for.</summary>
    Pixel,
}

/// <summary>
/// One program's ops sorted into the three stages, so a renderer can run each
/// where it belongs instead of running all of them half a million times.
/// </summary>
/// <remarks>
/// The whole of the saving is that a patch says far more about a frame than
/// about a pixel. Sixty per cent of a busy preset's ops never see a coordinate
/// — a scale factor, an envelope, a sequencer stepping on the clock — and the
/// interpreter was re-deriving every one of them for each of half a million
/// pixels. Of the largest preset in the catalogue, 598 ops, eleven per cent
/// depend on where you are; the rest is a frame's worth of arithmetic done once.
/// <para>
/// A reordering rather than three programs: the ops are the program's own, moved
/// into stage order and no further. A stage is the greatest of its inputs'
/// stages, so an op never precedes something it reads however the list is cut,
/// and the three runs together evaluate exactly what one run of the original
/// did.
/// </para>
/// <para>
/// Only for a caller drawing a picture, and <see cref="CompiledPatch.Plan"/> is
/// null wherever that is not what is happening. Reordering is safe because the
/// video path passes no <see cref="DelayState"/>: with none, every op in the
/// instruction set is a pure function of its inputs — a delay hands its input
/// through, an accumulator is its multiply, a cell reads zero and a tap does
/// nothing — so which of them ran first stops being a question. On the audio
/// path, where the state exists, it very much is one, and that path keeps
/// walking the program in the order it was written.
/// </para>
/// </remarks>
public sealed class FramePlan
{
    private FramePlan(Op[] ops, int rowAt, int pixelAt)
    {
        Ops = ops;
        RowAt = rowAt;
        PixelAt = pixelAt;
    }

    /// <summary>The program's ops, in stage order.</summary>
    public Op[] Ops { get; }

    /// <summary>Where the row's ops start, which is also where the frame's end.</summary>
    public int RowAt { get; }

    /// <summary>Where the pixel's ops start.</summary>
    public int PixelAt { get; }

    /// <summary>The half-open range of <see cref="Ops"/> belonging to <paramref name="stage"/>.</summary>
    public (int From, int To) Range(EvaluationStage stage) => stage switch
    {
        EvaluationStage.Frame => (0, RowAt),
        EvaluationStage.Row => (RowAt, PixelAt),
        _ => (PixelAt, Ops.Length),
    };

    /// <summary>
    /// Sorts <paramref name="ops"/> into stages, or answers null where the
    /// program is not one this can be done to.
    /// </summary>
    /// <remarks>
    /// The refusal is about single assignment. Working out a stage means reading
    /// back the stage of the register an op's input came from, and that only
    /// means anything while a register is written once — which is what the
    /// compiler's allocator does and what nothing here can check cheaply after
    /// the fact, so it is checked directly. A program that writes a register
    /// twice gets no plan and is walked whole, which is what every caller did
    /// before this existed.
    /// </remarks>
    public static FramePlan? For(Op[] ops, int registerCount)
    {
        ArgumentNullException.ThrowIfNull(ops);

        var stages = new EvaluationStage[ops.Length];
        var of = new EvaluationStage[registerCount];
        var written = new bool[registerCount];

        var counts = new int[3];

        for (var i = 0; i < ops.Length; i++)
        {
            var op = ops[i];

            var stage = op.Code switch
            {
                OpCode.LoadX => EvaluationStage.Pixel,
                OpCode.LoadY => EvaluationStage.Row,
                _ => EvaluationStage.Frame,
            };

            var inputs = OpShape.Inputs(op.Code);

            if (inputs > 0) stage = Later(stage, of[op.A]);
            if (inputs > 1) stage = Later(stage, of[op.B]);
            if (inputs > 2) stage = Later(stage, of[op.C]);

            for (var w = 0; w < OpShape.Outputs(op.Code); w++)
            {
                if (written[op.Out + w]) return null;

                written[op.Out + w] = true;
                of[op.Out + w] = stage;
            }

            stages[i] = stage;
            counts[(int)stage]++;
        }

        var sorted = new Op[ops.Length];
        var next = new[] { 0, counts[0], counts[0] + counts[1] };

        for (var i = 0; i < ops.Length; i++)
            sorted[next[(int)stages[i]]++] = ops[i];

        return new FramePlan(sorted, counts[0], counts[0] + counts[1]);
    }

    private static EvaluationStage Later(EvaluationStage a, EvaluationStage b) => a > b ? a : b;
}
