namespace Flyback.Core.Render;

/// <summary>
/// Why a file could not be read as audio, or <see cref="None"/> where it could. A
/// value rather than an exception, for the callers' sake: a malformed sample is
/// something the compiler says about a patch, in the same sentence it says
/// everything else.
/// </summary>
public enum WavFault
{
    None,

    /// <summary>Nothing at that path.</summary>
    Missing,

    /// <summary>Something is there and it is not a RIFF/WAVE file.</summary>
    NotWave,

    /// <summary>A WAVE this reader does not know how to read.</summary>
    Unsupported,

    /// <summary>On another machine, where a patch is not allowed to reach.</summary>
    Elsewhere,

    /// <summary>A WAVE with no audio in it.</summary>
    Empty,
}