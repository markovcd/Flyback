namespace Flyback.Engine.Render;

/// <summary>What <see cref="OversampleStepDown"/> made of the buffers it looked at.</summary>
/// <param name="Lower">The factor to play at from now on, or null to leave it where it is.</param>
/// <param name="Behind">Whether the sound fell behind at 1×, with no lower factor left to step down to.</param>
public readonly record struct StepDownVerdict(int? Lower, bool Behind)
{
    /// <summary>The sound keeps up, or has not been judged yet.</summary>
    public static readonly StepDownVerdict Keep = new(null, false);
}
