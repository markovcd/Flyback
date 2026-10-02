namespace Flyback.Engine.Compile;

/// <summary>Which ways of running a program <see cref="IlProgram"/> builds as machine code.</summary>
[Flags]
public enum IlParts
{
    /// <summary><see cref="IlProgram.Evaluate"/>: the program in the order it was written, which is what the sound runs.</summary>
    Whole = 1,

    /// <summary><see cref="IlProgram.EvaluateStage"/>: the frame, row and pixel shares, which is what the picture runs.</summary>
    Staged = 2,

    All = Whole | Staged,
}