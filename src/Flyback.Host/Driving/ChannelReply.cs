namespace Flyback.Host.Driving;

/// <summary>What the editor answered one <see cref="ChannelRequest"/> with.</summary>
/// <param name="Id">The request's.</param>
/// <param name="Ok">Whether it did what was asked; a greeting turned away is not.</param>
/// <param name="Text">What it said.</param>
/// <param name="Png">A picture that goes with it, as base64, or null.</param>
internal sealed record ChannelReply(int Id, bool Ok, string Text, string? Png = null);
