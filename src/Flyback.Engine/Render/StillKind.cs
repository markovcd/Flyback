namespace Flyback.Engine.Render;

/// <summary>What a preset's still is: a picture, or why there is none.</summary>
public enum StillKind
{
    /// <summary>A frame of the preset's picture.</summary>
    Picture,

    /// <summary>A preset that is heard and never seen.</summary>
    SoundOnly,

    /// <summary>A preset with nothing wired to either half of the Output.</summary>
    Nothing,

    /// <summary>A preset that would not build or compile.</summary>
    Unavailable,
}
