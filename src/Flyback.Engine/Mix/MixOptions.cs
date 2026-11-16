using Flyback.Core;

namespace Flyback.Engine.Mix;

/// <summary>What a mix check listens to.</summary>
/// <param name="Seconds">How long to listen.</param>
/// <param name="From">Where on the patch's clock to start. The sound is warmed for a second before it.</param>
/// <param name="SampleRate">The rate the sound is rendered at.</param>
public sealed record MixOptions(
    double Seconds = MixOptions.DefaultSeconds,
    double From = 0d,
    int SampleRate = GlobalConstants.SampleRate)
{
    /// <summary>Long enough for a bar or two of most songs and a stable loudness.</summary>
    public const double DefaultSeconds = 8d;

    /// <summary>The longest window: two minutes, which is a long render for a big patch.</summary>
    public const double MaxSeconds = 120d;
}
