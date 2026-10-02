namespace Flyback.Engine.Render;

/// <summary>
/// Why a file could not be read as audio, or <see cref="None"/> where it could. A
/// value rather than an exception, for the callers' sake: a malformed sample is
/// something the compiler says about a patch, in the same sentence it says
/// everything else.
/// </summary>
public enum SoundFault
{
    None,

    /// <summary>Nothing at that path.</summary>
    Missing,

    /// <summary>Something is there and it is neither a WAV nor an MP3.</summary>
    NotSound,

    /// <summary>A WAVE this reader does not know how to read.</summary>
    Unsupported,

    /// <summary>On another machine, where a patch is not allowed to reach.</summary>
    Elsewhere,

    /// <summary>A sound file with no audio in it.</summary>
    Empty,

    /// <summary>An MP3, on a machine with no ffmpeg to decode it.</summary>
    NoFfmpeg,

    /// <summary>An MP3 that ffmpeg would not decode.</summary>
    Undecoded,
}
