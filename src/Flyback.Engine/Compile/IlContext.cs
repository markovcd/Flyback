using Flyback.Core.Compile;

namespace Flyback.Engine.Compile;

/// <summary>What an emitted method reads that is neither an argument nor a register: one patch's own values.</summary>
internal sealed class IlContext
{
    public double[] Constants = [];
    public LoadedSample[] Tables = [];
    public LoadedImage[] Pictures = [];

    /// <summary>
    /// What the whole program's dearest ops were last asked and answered (<see cref="IlOps.RememberedPow"/>).
    /// Only the whole program reads it, and that runs on one thread at a time; the stages run side by side and never do.
    /// </summary>
    public double[] Memory = [];

    /// <summary>
    /// Constants are indexed by the register their op writes, so the same code
    /// finds them in any patch of the same shape without a table of where each went.
    /// </summary>
    public static IlContext For(CompiledPatch patch)
    {
        var constants = new double[patch.RegisterCount];

        foreach (var op in patch.Ops)
            if (op.Code is OpCode.Const)
                constants[op.Out] = op.K;

        return new IlContext
        {
            Constants = constants,
            Tables = patch.TableArray,
            Pictures = patch.PictureArray,
            Memory = IlOps.Memory(patch.Ops.Select(op => op.Code)),
        };
    }
}
