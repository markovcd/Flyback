namespace Flyback.Core.Compile;

/// <summary>What an emitted method reads that is neither an argument nor a register: one patch's own values.</summary>
internal sealed class IlContext
{
    public double[] Constants = [];
    public LoadedSample[] Tables = [];
    public LoadedImage[] Pictures = [];

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
        };
    }
}