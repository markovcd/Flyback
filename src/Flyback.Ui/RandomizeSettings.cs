using Flyback.Core.Graph;

namespace Flyback.Ui;

/// <summary>How the knob panel's randomize moves the knobs, and the controller that fires it.</summary>
public sealed class RandomizeSettings
{
    /// <summary>The longest glide the panel's glide knob reaches.</summary>
    public const double LongestGlide = 8;

    /// <summary>How far a knob may travel from where it is, 0 to 1; 1 is anywhere.</summary>
    public double Amount { get; set; } = 1;

    /// <summary>How long the knobs take to reach where they are going; nought jumps.</summary>
    public double GlideSeconds { get; set; } = 0.5;

    /// <summary>The controller button that randomizes, or null for none.</summary>
    public MidiBinding? Trigger { get; set; }

    /// <summary>Brings a hand-edited file's numbers into range.</summary>
    public void Clamp()
    {
        Amount = double.IsFinite(Amount) ? Math.Clamp(Amount, 0, 1) : 1;
        GlideSeconds = double.IsFinite(GlideSeconds) ? Math.Clamp(GlideSeconds, 0, LongestGlide) : 0.5;
    }
}
