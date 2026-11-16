namespace Flyback.Engine.Mix;

/// <summary>How much a mix check's finding matters.</summary>
public enum MixSeverity
{
    /// <summary>Worth a look; a patch may mean it.</summary>
    Notice,

    /// <summary>Something a listener will hear go wrong.</summary>
    Warning,
}
