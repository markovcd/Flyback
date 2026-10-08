namespace Flyback.Core.Compile;

/// <summary>
/// What an op owns by its position in the program: a renderer hands out delay
/// lines and phase cells in the order the ops that take them run.
/// </summary>
internal enum OpMemory
{
    None,

    /// <summary>A delay line, which a Delay or an Allpass reads and writes.</summary>
    Line,

    /// <summary>A phase cell, which a Phase accumulates in.</summary>
    Cell,
}
