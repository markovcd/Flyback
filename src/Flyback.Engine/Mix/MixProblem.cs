namespace Flyback.Engine.Mix;

/// <summary>What a mix check can find wrong with a patch's sound.</summary>
public enum MixProblem
{
    /// <summary>Nothing reaches the speakers, or what does is silent.</summary>
    NoSound,

    /// <summary>No Limiter or Maximizer stands between the mix and the Output.</summary>
    NoMasterChain,

    /// <summary>Samples hit full scale, or the true peak goes over it.</summary>
    Clipping,

    /// <summary>What reaches the Output sits off zero.</summary>
    DcOffset,

    /// <summary>Next to nothing below 60 Hz.</summary>
    NoSub,

    /// <summary>The 200 to 500 Hz band crowds out the bass under it and everything over it.</summary>
    Muddy,

    /// <summary>The bass below 120 Hz differs between left and right.</summary>
    WideLowEnd,

    /// <summary>The loudness is far under what music is played at.</summary>
    TooQuiet,

    /// <summary>The loudness is far over what music is played at.</summary>
    TooLoud,

    /// <summary>A sound source on the way to the Output held still the whole window.</summary>
    SilentModule,
}
