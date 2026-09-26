namespace Flyback.Core.Compile;

/// <summary>Which program playing somewhere a submission stands for.</summary>
public enum IlLane
{
    /// <summary>The patch's picture in the preview.</summary>
    Picture,

    /// <summary>The patch's sound.</summary>
    Sound,

    /// <summary>A preset's picture playing on its gallery tile.</summary>
    AuditionPicture,

    /// <summary>A preset's sound played while its tile is pointed at.</summary>
    AuditionSound,
}