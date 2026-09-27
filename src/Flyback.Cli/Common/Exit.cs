namespace Flyback.Cli.Common;

/// <summary>What the program tells the shell it did.</summary>
/// <remarks>
/// Three answers rather than two, because "the patch is wrong" and "I could not
/// look at the patch" are different things to a script: the first is a result
/// and the second is a fault. A build that fails on the first has found
/// something; one that fails on the second has been pointed at the wrong path.
/// </remarks>
internal static class Exit
{
    public const int Ok = 0;

    /// <summary>The patch was read and something about it is wrong.</summary>
    public const int Problems = 1;

    /// <summary>The job could not be done at all — a file missing, a path unwritable.</summary>
    public const int Failed = 2;
}