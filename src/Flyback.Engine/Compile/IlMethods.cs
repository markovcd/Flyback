using System.Reflection.Emit;
using System.Runtime.InteropServices;
using Flyback.Engine.Render;

namespace Flyback.Engine.Compile;

/// <summary>The methods of one shape, compiled to machine code and ready to be bound.</summary>
/// <remarks>Null wherever a part was not asked for, which the bound program hands to the interpreter.</remarks>
internal sealed class IlMethods
{
    private IlMethods(
        IlShape shape,
        IlParts parts,
        DynamicMethod[]? whole,
        SoundLegs? legs,
        int[]? legChunks,
        DynamicMethod[]? frame,
        DynamicMethod[]? row,
        DynamicMethod[]? pixel)
    {
        Shape = shape;
        Parts = parts;
        Whole = whole;
        Legs = legs;
        LegChunks = legChunks;
        Frame = frame;
        Row = row;
        Pixel = pixel;
    }

    public IlShape Shape { get; }

    public IlParts Parts { get; }

    public DynamicMethod[]? Whole { get; }

    /// <summary>Where the whole program is cut for cores to run in turn, or null when it runs on one.</summary>
    public SoundLegs? Legs { get; }

    /// <summary>The index in <see cref="Whole"/> of each leg's first chunk, and the count of chunks last.</summary>
    public int[]? LegChunks { get; }

    public DynamicMethod[]? Frame { get; }

    public DynamicMethod[]? Row { get; }

    public DynamicMethod[]? Pixel { get; }

    /// <param name="legs">
    /// The most legs to cut the whole program into, whatever it costs; nought to cut it
    /// into at most <paramref name="most"/>, and only if it is dear enough.
    /// </param>
    /// <param name="most">The most legs for a dear program; nought for <see cref="SoundCores.Most"/>.</param>
    public static IlMethods Build(CompiledPatch patch, IlParts parts, int legs = 0, int most = 0)
    {
        var outputs = (patch.OutputBase, patch.OutputBase + patch.OutputWidth);

        DynamicMethod[]? whole = null;
        SoundLegs? cut = null;
        int[]? legChunks = null;

        if (parts.HasFlag(IlParts.Whole))
        {
            var repeats = new Repeats(patch.Ops, outputs);
            cut = legs > 0
                ? SoundLegs.For(patch.Ops, outputs, repeats, legs, least: 0d)
                : SoundLegs.For(patch.Ops, outputs, repeats, most > 0 ? most : SoundCores.Most, SoundLegs.Least);
            int[] cuts = cut?.Cuts ?? [0, patch.Ops.Length];

            // Each leg is chunked on its own, so a leg is whole chunks; run in turn they are the program.
            var chunks = new List<DynamicMethod>();
            legChunks = new int[cuts.Length];

            for (var l = 0; l + 1 < cuts.Length; l++)
            {
                legChunks[l] = chunks.Count;
                chunks.AddRange(IlEmitter.EmitChunks("Whole", patch.Ops, cuts[l], cuts[l + 1], outputs, whole: true, repeats, cut?.Order));
            }

            legChunks[^1] = chunks.Count;
            whole = [.. chunks];
            if (cut is null) legChunks = null;
        }

        DynamicMethod[]? frame = null, row = null, pixel = null;

        if (parts.HasFlag(IlParts.Staged))
        {
            if (patch.Plan is { } plan)
            {
                frame = IlEmitter.EmitChunks("Frame", plan.Ops, 0, plan.RowAt, outputs);
                row = IlEmitter.EmitChunks("Row", plan.Ops, plan.RowAt, plan.PixelAt, outputs);
                pixel = IlEmitter.EmitChunks("Pixel", plan.Ops, plan.PixelAt, plan.Ops.Length, outputs);
            }
            else
            {
                // What the interpreter does for a program it cannot sort: the pixel
                // runs all of it, and the other two stages run nothing. Not the whole
                // program's own methods, which remember and so run on one thread.
                frame = row = IlEmitter.EmitChunks("Nothing", [], 0, 0, outputs);
                pixel = IlEmitter.EmitChunks("Whole", patch.Ops, 0, patch.Ops.Length, outputs);
            }
        }

        var methods = new IlMethods(new IlShape(patch), parts, whole, cut, legChunks, frame, row, pixel);
        methods.Prepare(patch);
        return methods;
    }

    /// <summary>
    /// Calls each method once, with no state and nothing live, because a dynamic
    /// method is only compiled to machine code when it is first called — and the
    /// first call must not be the audio callback's.
    /// </summary>
    /// <remarks>
    /// The chunks share nothing but read-only constants, so each is called on a
    /// bank of its own and they go through the JIT side by side, on half the cores
    /// and at the caller's priority to leave the audio and render threads theirs.
    /// </remarks>
    private void Prepare(CompiledPatch patch)
    {
        var context = IlContext.For(patch);
        var priority = Thread.CurrentThread.Priority;
        DynamicMethod[] chunks = [.. new[] { Whole, Frame, Row, Pixel }.SelectMany(part => part ?? []).Distinct()];

        Parallel.ForEach(
            chunks,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) },
            chunk =>
            {
                var thread = Thread.CurrentThread;
                var own = thread.Priority;
                thread.Priority = priority;

                try
                {
                    var registers = patch.AllocateRegisters();
                    var feedback = default(FeedbackFrame);
                    chunk.CreateDelegate<IlStage>(context)(
                        ref MemoryMarshal.GetArrayDataReference(registers), 0d, 0d, 0d, 1d, ref feedback, null, null, default);
                }
                finally
                {
                    thread.Priority = own;
                }
            });
    }
}
