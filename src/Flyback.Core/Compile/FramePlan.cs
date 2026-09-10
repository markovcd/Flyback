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
/// A patch says far more about a frame than about a pixel: of the largest preset
/// in the catalogue, 598 ops, eleven per cent depend on where you are and the rest
/// is a frame's worth of arithmetic done once.
/// <para>
/// A reordering rather than three programs. A stage is the greatest of its
/// inputs' stages, so an op never precedes something it reads and the three runs
/// together evaluate exactly what one run of the original did.
/// </para>
/// <para>
/// Only for a caller drawing a picture, and <see cref="CompiledPatch.Plan"/> is
/// null elsewhere. Reordering is safe because the video path passes no
/// <see cref="DelayState"/>: with none, every op is a pure function of its inputs
/// — a delay hands its input through, a cell reads zero — so which ran first stops
/// being a question. On the audio path it very much is one.
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
    /// Sorts <paramref name="ops"/> into stages, or answers null where the program
    /// is not one this can be done to.
    /// </summary>
    /// <remarks>
    /// The refusal is about single assignment: a stage is read back off the register
    /// an op's input came from, which only means anything while a register is
    /// written once. A program that writes one twice gets no plan and is walked
    /// whole.
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
